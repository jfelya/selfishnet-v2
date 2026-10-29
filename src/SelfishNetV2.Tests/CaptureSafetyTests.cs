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

            session.Stop(50);
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
}
