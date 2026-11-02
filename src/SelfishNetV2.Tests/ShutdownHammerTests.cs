using SelfishNetV2.Core;
using SelfishNetV2.Drivers;
using SelfishNetV2.Engines;
using Xunit;

namespace SelfishNetV2.Tests;

/// <summary>
/// C2-class regression: hammer start/stop/dispose cycles. Any use-after-close of a
/// capture handle, deadlock in the shutdown ritual, or leaked thread shows up here.
/// </summary>
public class ShutdownHammerTests
{
    [Fact]
    public void CaptureSession_StartStopDispose_Hammer()
    {
        var driver = new LoopbackDriver();
        for (int i = 0; i < 300; i++)
        {
            var cap = driver.OpenLive("loopback0", "ip");
            var session = new PcapCaptureSession(cap, "hammer");
            int received = 0;
            session.PacketReceived += _ => Interlocked.Increment(ref received);
            session.Start();
            driver.Inject(PacketClassifierTests.IpFrame(1, 2, 3, 4));
            session.Stop();
            session.Dispose(); // must be safe after Stop, and idempotent
            session.Dispose();
        }
    }

    [Fact]
    public async Task Engine_OpenShapingClose_Hammer()
    {
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        var adapter = EngineIntegrationTestsAdapter.Get();
        for (int i = 0; i < 30; i++)
        {
            using var engine = new ModernEngine(catalog, driver);
            Assert.True(engine.Open(adapter));
            driver.Inject(EngineIntegrationTestsHelper.GatewayReply());
            Assert.True(EngineIntegrationTestsHelper.WaitUntil(() =>
                catalog.TryGetByIp(EngineIntegrationTestsHelper.RouterIp, out var gw) && gw.HasKnownMac));
            Assert.True(await engine.StartShapingAsync());
            await engine.StopAndHealAsync();
            engine.Close();
        }
    }
}

/// <summary>Shared constants/helpers for engine tests.</summary>
internal static class EngineIntegrationTestsHelper
{
    public const ulong Local = 0x020000000001;
    public const ulong Router = 0x5CAA00112233;
    public const uint LocalIp = 0xC0A80132;
    public const uint RouterIp = 0xC0A80101;

    public static byte[] GatewayReply() =>
        ArpFrameBuilder.Build(Local, Router, Router, RouterIp, Local, LocalIp, 2);

    public static bool WaitUntil(Func<bool> cond, int timeoutMs = 4000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond()) return true;
            Thread.Sleep(15);
        }
        return cond();
    }
}

internal static class EngineIntegrationTestsAdapter
{
    public static AdapterInfo Get() => new(
        "{00000000-0000-0000-0000-000000000001}", "Loopback", "Loopback test adapter",
        EngineIntegrationTestsHelper.LocalIp, 0xFFFFFF00, EngineIntegrationTestsHelper.RouterIp,
        EngineIntegrationTestsHelper.Local, "loopback0");
}
