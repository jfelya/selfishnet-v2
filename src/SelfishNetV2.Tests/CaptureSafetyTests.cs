using System.Diagnostics;
using SelfishNetV2.Core;
using SelfishNetV2.Drivers;
using SelfishNetV2.Engines;
using Xunit;

namespace SelfishNetV2.Tests;

/// <summary>
/// Failure paths at the capture boundary: a stuck driver must never have its handle
/// closed under a live thread, and a fatal receive error must stop shaping/heal.
/// </summary>
public class CaptureSafetyTests
{
    private const ulong Local = 0x020000000001;
    private const ulong Router = 0x5CAA00112233;
    private const ulong Victim = 0xAABBCCDDEE01;
    private const uint LocalIp = 0xC0A80132;
    private const uint RouterIp = 0xC0A80101;
    private const uint VictimIp = 0xC0A80164;

    private static AdapterInfo Adapter => new(
        "{00000000-0000-0000-0000-000000000001}", "Loopback", "Loopback test adapter",
        LocalIp, 0xFFFFFF00, RouterIp, Local, "loopback0");

    private static byte[] ArpReply(ulong senderMac, uint senderIp, ulong targetMac, uint targetIp) =>
        ArpFrameBuilder.Build(targetMac, senderMac, senderMac, senderIp, targetMac, targetIp, 2);

    private static bool WaitUntil(Func<bool> cond, int timeoutMs = 4000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond()) return true;
            Thread.Sleep(15);
        }
        return cond();
    }

    [Fact]
    public void JoinTimeout_DoesNotDisposeLiveCapture()
    {
        var capture = new BlockingCapture();
        var session = new PcapCaptureSession(capture, "blocked");
        try
        {
            session.Start();
            Assert.True(WaitUntil(() => capture.ReceiveStarted, 2000));

            Assert.False(session.Stop(50));
            session.Dispose();

            Assert.False(capture.Disposed,
                "a capture whose receive thread is still alive must be leaked, never closed");
        }
        finally
        {
            capture.Release();
        }
    }

    [Fact]
    public void ReceiveError_MarksSessionNotRunning()
    {
        var capture = new FaultingCapture();
        using var session = new PcapCaptureSession(capture, "faulted");
        session.Start();

        Assert.True(WaitUntil(() => capture.ReceiveCount > 0, 2000));
        Thread.Sleep(100);

        Assert.False(session.Running, "the receive thread has exited after a driver error");
    }

    [Fact]
    public async Task ReceiveError_StopsShapingAndHeals()
    {
        var driver = new FailingDriver();
        var catalog = new DeviceCatalog();
        using var engine = new ModernEngine(catalog, driver);
        Assert.True(engine.Open(Adapter));
        driver.Inject(ArpReply(Router, RouterIp, Local, LocalIp));
        driver.Inject(ArpReply(Victim, VictimIp, Local, LocalIp));
        Assert.True(WaitUntil(() =>
            catalog.TryGetByIp(RouterIp, out var gw) && gw.HasKnownMac &&
            catalog.TryGetByIp(VictimIp, out var v) && v.HasKnownMac));

        Assert.True(await engine.StartShapingAsync());
        int before = driver.SentFrames.Count(IsRestoreToVictim);

        Assert.NotNull(driver.IpCapture);
        driver.IpCapture!.Fail();

        Assert.True(WaitUntil(() => engine.State == EngineRunState.Idle, 4000),
            $"engine state was {engine.State}");
        Assert.True(driver.SentFrames.Count(IsRestoreToVictim) > before,
            "a fatal receive error must heal the devices we poisoned");
    }

    [Fact]
    public void SweepClose_DoesNotDisposeArpCapture()
    {
        var driver = new BlockingSendDriver();
        var catalog = new DeviceCatalog();
        using var engine = new FastJoinEngine(catalog, driver);
        Assert.True(engine.Open(Adapter));
        driver.ArmSend();
        engine.StartDiscovery();
        Assert.True(WaitUntil(() => driver.ArpCapture!.SendStarted, 2000));

        engine.Close();

        Assert.False(driver.ArpCapture!.Disposed,
            "a capture used by a still-running sweep must be leaked, not closed");
        driver.ReleaseSend();
    }

    [Fact]
    public async Task ImmediateIpError_DoesNotStayShaping()
    {
        var driver = new ImmediateIpErrorDriver();
        var catalog = new DeviceCatalog();
        using var engine = new ModernEngine(catalog, driver);
        Assert.True(engine.Open(Adapter));
        driver.Inject(ArpReply(Router, RouterIp, Local, LocalIp));
        driver.Inject(ArpReply(Victim, VictimIp, Local, LocalIp));
        Assert.True(WaitUntil(() =>
            catalog.TryGetByIp(RouterIp, out var gw) && gw.HasKnownMac &&
            catalog.TryGetByIp(VictimIp, out var v) && v.HasKnownMac));

        await engine.StartShapingAsync();

        Assert.True(WaitUntil(() => engine.State != EngineRunState.Shaping, 3000),
            $"engine state was {engine.State}");
    }

    [Fact]
    public async Task FailedHeal_KeepsClaimForCloseRetry()
    {
        var driver = new FailingSendDriver();
        var catalog = new DeviceCatalog();
        using var engine = new ModernEngine(catalog, driver);
        Assert.True(engine.Open(Adapter));
        driver.Inject(ArpReply(Router, RouterIp, Local, LocalIp));
        driver.Inject(ArpReply(Victim, VictimIp, Local, LocalIp));
        Assert.True(WaitUntil(() =>
            catalog.TryGetByIp(RouterIp, out var gw) && gw.HasKnownMac &&
            catalog.TryGetByIp(VictimIp, out var v) && v.HasKnownMac));

        driver.FailSends = true;
        Assert.True(await engine.StartShapingAsync());
        await engine.StopAndHealAsync();
        Assert.Equal(0, driver.SentFrames.Count(IsRestoreToVictim));

        driver.FailSends = false;
        engine.Close();
        Assert.True(WaitUntil(() => driver.SentFrames.Count(IsRestoreToVictim) >= 35, 3000),
            "a failed heal claim must be retried on close");
    }

    [Fact]
    public async Task StuckSpoofSend_IsLeakedNotClosed()
    {
        var driver = new BlockingSendDriver();
        var catalog = new DeviceCatalog();
        using var engine = new FastJoinEngine(catalog, driver);
        Assert.True(engine.Open(Adapter));
        driver.Inject(ArpReply(Router, RouterIp, Local, LocalIp));
        driver.Inject(ArpReply(Victim, VictimIp, Local, LocalIp));
        Assert.True(WaitUntil(() =>
            catalog.TryGetByIp(RouterIp, out var gw) && gw.HasKnownMac &&
            catalog.TryGetByIp(VictimIp, out var v) && v.HasKnownMac));

        driver.ArmSend();
        Assert.True(await engine.StartShapingAsync());
        Assert.True(WaitUntil(() => driver.ArpCapture!.SendStarted, 2000));

        await engine.StopAndHealAsync();
        engine.Close();

        Assert.False(driver.ArpCapture!.Disposed,
            "a capture used by a stuck spoof send must be leaked, not closed");
        driver.ReleaseSend();
    }

    private static bool IsRestoreToVictim(byte[] f)
    {
        if (f.Length != ArpFrameBuilder.FrameLength || f[12] != 0x08 || f[13] != 0x06)
            return false;
        ArpFrame.TryParse(f, out var a);
        return a.Opcode == ArpFrameBuilder.OpcodeReply &&
               a.EthDstMac == Victim && a.SenderMac == Router &&
               a.SenderIp == RouterIp && a.TargetIp == VictimIp;
    }

    private sealed class BlockingCapture : IPcapCapture
    {
        private readonly ManualResetEventSlim _release = new(false);
        private readonly ManualResetEventSlim _started = new(false);

        public string DeviceName => "blocked";
        public string LastError => "";
        public bool ReceiveStarted => _started.IsSet;
        public bool Disposed { get; private set; }

        public int Send(ReadOnlySpan<byte> frame) => 0;

        public ReceiveResult Receive(out PacketView view)
        {
            view = default;
            _started.Set();
            _release.Wait();
            return ReceiveResult.Broken;
        }

        public void BreakLoop() { /* deliberately does not unblock: simulate a stuck driver */ }

        public void TrySetMinToCopy(int bytes) { }

        public void Release() => _release.Set();

        public void Dispose() => Disposed = true;
    }

    private sealed class FaultingCapture : IPcapCapture
    {
        public string DeviceName => "faulted";
        public string LastError => "injected receive error";
        public int ReceiveCount;
        public int Send(ReadOnlySpan<byte> frame) => 0;

        public ReceiveResult Receive(out PacketView view)
        {
            Interlocked.Increment(ref ReceiveCount);
            view = default;
            return ReceiveResult.Error;
        }

        public void BreakLoop() { }
        public void TrySetMinToCopy(int bytes) { }
        public void Dispose() { }
    }

    /// <summary>Delegates capture I/O to LoopbackDriver, but can fault only the IP capture.</summary>
    private sealed class FailingDriver : IPcapDriver
    {
        private readonly LoopbackDriver _inner = new();
        public FailingCapture? IpCapture { get; private set; }

        public string BackendName => "Loopback/fault";
        public IReadOnlyList<PcapDeviceInfo> EnumerateDevices() => _inner.EnumerateDevices();
        public IReadOnlyList<byte[]> SentFrames => _inner.SentFrames;
        public void Inject(ReadOnlySpan<byte> frame) => _inner.Inject(frame);

        public IPcapCapture OpenLive(string deviceName, string? filter, int snaplen = 65535,
            int promisc = 0, int timeoutMs = 20)
        {
            var capture = _inner.OpenLive(deviceName, filter, snaplen, promisc, timeoutMs);
            if (filter == "ip or (vlan and ip)")
            {
                IpCapture = new FailingCapture(capture);
                return IpCapture;
            }
            return capture;
        }
    }

    private sealed class FailingCapture : IPcapCapture
    {
        private readonly IPcapCapture _inner;
        private volatile bool _failed;

        public FailingCapture(IPcapCapture inner) => _inner = inner;
        public string DeviceName => _inner.DeviceName;
        public string LastError => _failed ? "injected receive error" : _inner.LastError;

        public void Fail() => _failed = true;
        public int Send(ReadOnlySpan<byte> frame) => _inner.Send(frame);

        public ReceiveResult Receive(out PacketView view)
        {
            if (_failed)
            {
                view = default;
                return ReceiveResult.Error;
            }
            return _inner.Receive(out view);
        }

        public void BreakLoop() => _inner.BreakLoop();
        public void TrySetMinToCopy(int bytes) => _inner.TrySetMinToCopy(bytes);
        public void Dispose() => _inner.Dispose();
    }

    private sealed class FastJoinEngine : ModernEngine
    {
        public FastJoinEngine(DeviceCatalog catalog, IPcapDriver driver) : base(catalog, driver) { }
        protected override int JoinTimeoutMs => 50;
        protected override int AutoScanIntervalMs => 0;
    }

    private sealed class ImmediateIpErrorDriver : IPcapDriver
    {
        private readonly LoopbackDriver _inner = new();
        public string BackendName => "Loopback/immediate-error";
        public IReadOnlyList<PcapDeviceInfo> EnumerateDevices() => _inner.EnumerateDevices();
        public void Inject(ReadOnlySpan<byte> frame) => _inner.Inject(frame);

        public IPcapCapture OpenLive(string deviceName, string? filter, int snaplen = 65535,
            int promisc = 0, int timeoutMs = 20)
        {
            if (filter == "ip or (vlan and ip)") return new FaultingCapture();
            return _inner.OpenLive(deviceName, filter, snaplen, promisc, timeoutMs);
        }
    }

    private sealed class FailingSendDriver : IPcapDriver
    {
        private readonly LoopbackDriver _inner = new();
        public bool FailSends { get; set; }
        public string BackendName => "Loopback/failing-send";
        public IReadOnlyList<PcapDeviceInfo> EnumerateDevices() => _inner.EnumerateDevices();
        public IReadOnlyList<byte[]> SentFrames => _inner.SentFrames;
        public void Inject(ReadOnlySpan<byte> frame) => _inner.Inject(frame);

        public IPcapCapture OpenLive(string deviceName, string? filter, int snaplen = 65535,
            int promisc = 0, int timeoutMs = 20)
        {
            var capture = _inner.OpenLive(deviceName, filter, snaplen, promisc, timeoutMs);
            return filter == "arp" ? new FailableSendCapture(capture, this) : capture;
        }
    }

    private sealed class FailableSendCapture : IPcapCapture
    {
        private readonly IPcapCapture _inner;
        private readonly FailingSendDriver _owner;

        public FailableSendCapture(IPcapCapture inner, FailingSendDriver owner)
        {
            _inner = inner;
            _owner = owner;
        }

        public string DeviceName => _inner.DeviceName;
        public string LastError => _inner.LastError;
        public int Send(ReadOnlySpan<byte> frame) => _owner.FailSends ? -1 : _inner.Send(frame);
        public ReceiveResult Receive(out PacketView view) => _inner.Receive(out view);
        public void BreakLoop() => _inner.BreakLoop();
        public void TrySetMinToCopy(int bytes) => _inner.TrySetMinToCopy(bytes);
        public void Dispose() => _inner.Dispose();
    }

    private sealed class BlockingSendDriver : IPcapDriver
    {
        private readonly LoopbackDriver _inner = new();
        public BlockingSendCapture? ArpCapture { get; private set; }
        public string BackendName => "Loopback/blocking-send";
        public IReadOnlyList<PcapDeviceInfo> EnumerateDevices() => _inner.EnumerateDevices();
        public void Inject(ReadOnlySpan<byte> frame) => _inner.Inject(frame);
        public void ArmSend() => ArpCapture?.Arm();
        public void ReleaseSend() => ArpCapture?.ReleaseSend();

        public IPcapCapture OpenLive(string deviceName, string? filter, int snaplen = 65535,
            int promisc = 0, int timeoutMs = 20)
        {
            var capture = _inner.OpenLive(deviceName, filter, snaplen, promisc, timeoutMs);
            if (filter == "arp")
            {
                ArpCapture = new BlockingSendCapture(capture);
                return ArpCapture;
            }
            return capture;
        }
    }

    private sealed class BlockingSendCapture : IPcapCapture
    {
        private readonly IPcapCapture _inner;
        private readonly ManualResetEventSlim _sendGate = new(false);
        private readonly ManualResetEventSlim _sendStarted = new(false);
        private volatile bool _armed;

        public BlockingSendCapture(IPcapCapture inner) => _inner = inner;
        public string DeviceName => _inner.DeviceName;
        public string LastError => _inner.LastError;
        public bool Disposed { get; private set; }
        public bool SendStarted => _sendStarted.IsSet;

        public void Arm() => _armed = true;

        public int Send(ReadOnlySpan<byte> frame)
        {
            if (_armed)
            {
                _sendStarted.Set();
                _sendGate.Wait();
            }
            return _inner.Send(frame);
        }

        public void ReleaseSend() => _sendGate.Set();
        public ReceiveResult Receive(out PacketView view) => _inner.Receive(out view);
        public void BreakLoop() => _inner.BreakLoop();
        public void TrySetMinToCopy(int bytes) => _inner.TrySetMinToCopy(bytes);

        public void Dispose()
        {
            Disposed = true;
            _inner.Dispose();
        }
    }
}
