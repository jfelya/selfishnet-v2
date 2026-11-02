using System.Diagnostics;
using SelfishNetV2.Core;
using SelfishNetV2.Drivers;
using SelfishNetV2.Engines;
using SelfishNetV2.Services;
using Xunit;

namespace SelfishNetV2.Tests;

/// <summary>
/// Full-stack engine tests against the in-memory LoopbackDriver — no hardware, no
/// admin rights needed. Verifies learning, poisoning burst shape, forwarding with MAC
/// rewriting, blocking, caps, healing, and the runtime engine swap.
/// </summary>
public class EngineIntegrationTests
{
    protected const ulong Local = 0x020000000001;
    protected const ulong Router = 0x5CAA00112233;
    protected const ulong Victim = 0xAABBCCDDEE01;
    protected const uint LocalIp = 0xC0A80132;   // 192.168.1.50
    protected const uint RouterIp = 0xC0A80101;  // 192.168.1.1
    protected const uint VictimIp = 0xC0A80164;  // 192.168.1.100
    private const uint ExternalIp = 0x08080808;

    protected static AdapterInfo TestAdapter => new(
        "{00000000-0000-0000-0000-000000000001}", "Loopback", "Loopback test adapter",
        LocalIp, 0xFFFFFF00, RouterIp, Local, "loopback0");

    private static IEngineSession CreateEngine(EngineKind kind, DeviceCatalog catalog, LoopbackDriver driver) =>
        kind == EngineKind.Legacy ? new LegacyEngine(catalog, driver) : new ModernEngine(catalog, driver);

    protected static byte[] ArpReply(ulong senderMac, uint senderIp, ulong targetMac, uint targetIp) =>
        ArpFrameBuilder.Build(targetMac, senderMac, senderMac, senderIp, targetMac, targetIp, 2);

    private static bool IsArpReply(byte[] f) =>
        f.Length == ArpFrameBuilder.FrameLength && f[12] == 0x08 && f[13] == 0x06 && f[20] == 0 && f[21] == 2;

    protected static bool WaitUntil(Func<bool> cond, int timeoutMs = 4000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond()) return true;
            Thread.Sleep(15);
        }
        return cond();
    }

    /// <summary>Opens the engine and teaches it the gateway + victim MACs via ARP replies.</summary>
    private static IEngineSession OpenLearned(EngineKind kind, DeviceCatalog catalog, LoopbackDriver driver)
    {
        var engine = CreateEngine(kind, catalog, driver);
        Assert.True(engine.Open(TestAdapter));
        driver.Inject(ArpReply(Router, RouterIp, Local, LocalIp));   // gateway MAC learned
        driver.Inject(ArpReply(Victim, VictimIp, Local, LocalIp));   // victim learned
        Assert.True(WaitUntil(() =>
            catalog.TryGetByIp(RouterIp, out var gw) && gw.HasKnownMac &&
            catalog.TryGetByIp(VictimIp, out var v) && v.HasKnownMac));
        return engine;
    }

    [Theory]
    [InlineData(EngineKind.Legacy)]
    [InlineData(EngineKind.Modern)]
    public void Open_LearnsDevices_FromArpReplies(EngineKind kind)
    {
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        using var engine = OpenLearned(kind, catalog, driver);
        Assert.True(catalog.TryGetByMac(Router, out var gw) && gw.IsGateway);
        Assert.True(catalog.TryGetByIp(VictimIp, out var victim) && !victim.IsGateway && !victim.IsLocalPc);
        Assert.True(catalog.TryGetByIp(LocalIp, out var self) && self.IsLocalPc);
    }

    [Theory]
    [InlineData(EngineKind.Legacy)]
    [InlineData(EngineKind.Modern)]
    public async Task StartShaping_FailsClosed_WhenGatewayMacUnknown(EngineKind kind)
    {
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        using var engine = CreateEngine(kind, catalog, driver);
        Assert.True(engine.Open(TestAdapter));
        EngineErrorKind? error = null;
        engine.Error += (_, e) => error = e.Kind;
        bool ok = await engine.StartShapingAsync();
        Assert.False(ok);
        Assert.Equal(EngineErrorKind.GatewayMacUnknown, error);
        Assert.Equal(EngineRunState.Idle, engine.State);
    }

    [Theory]
    [InlineData(EngineKind.Legacy)]
    [InlineData(EngineKind.Modern)]
    public async Task StartShaping_WithoutGateway_FailsClosed(EngineKind kind)
    {
        // Previously a gateway-less adapter (VPN/link-local) could "start" capturing
        // without ever spoofing — indistinguishable from a broken app. It must fail.
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        using var engine = CreateEngine(kind, catalog, driver);
        Assert.True(engine.Open(TestAdapter with { GatewayIpKey = 0 }));

        EngineErrorKind? error = null;
        engine.Error += (_, e) => error = e.Kind;

        Assert.False(await engine.StartShapingAsync());
        Assert.Equal(EngineErrorKind.NoGateway, error);
        Assert.Equal(EngineRunState.Idle, engine.State);
    }

    [Fact]
    public async Task SpoofBursts_PoisonBothDirections_WithSelfRefresh()
    {
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        using var engine = OpenLearned(EngineKind.Modern, catalog, driver);
        int before = driver.SentFrames.Count;
        Assert.True(await engine.StartShapingAsync());
        Assert.True(WaitUntil(() => driver.SentFrames.Count(IsArpReply) >= 3));

        var replies = driver.SentFrames.Skip(before).Where(IsArpReply).Select(f =>
        {
            ArpFrame.TryParse(f, out var a);
            return a;
        }).ToList();

        // 1. poison to victim: "router is at us"
        Assert.Contains(replies, a => a.EthDstMac == Victim && a.SenderMac == Local && a.SenderIp == RouterIp && a.TargetIp == VictimIp);
        // 2. poison to router: "victim is at us"
        Assert.Contains(replies, a => a.EthDstMac == Router && a.SenderMac == Local && a.SenderIp == VictimIp && a.TargetIp == RouterIp);
        // 3. self-refresh: "router is at routerMAC" to us (eth src = router)
        Assert.Contains(replies, a => a.EthDstMac == Local && a.EthSrcMac == Router && a.SenderIp == RouterIp && a.TargetIp == LocalIp);
        await engine.StopAndHealAsync();
    }

    [Fact]
    public async Task Legacy_SendsInertFourthFrame_ModernDoesNot()
    {
        foreach (var (kind, expected) in new[] { (EngineKind.Legacy, 4), (EngineKind.Modern, 3) })
        {
            var driver = new LoopbackDriver();
            var catalog = new DeviceCatalog();
            using var engine = OpenLearned(kind, catalog, driver);
            int before = driver.SentFrames.Count;
            Assert.True(await engine.StartShapingAsync());
            Assert.True(WaitUntil(() => driver.SentFrames.Count(IsArpReply) - (driver.SentFrames.Take(before).Count(IsArpReply)) >= expected));
            await engine.StopAndHealAsync();
        }
    }

    [Theory]
    [InlineData(EngineKind.Legacy)]
    [InlineData(EngineKind.Modern)]
    public async Task Download_IsForwarded_WithRewrittenMacs(EngineKind kind)
    {
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        using var engine = OpenLearned(kind, catalog, driver);
        Assert.True(await engine.StartShapingAsync());

        byte[] frame = PacketClassifierTests.IpFrame(dstMac: Local, srcMac: Router, srcIp: ExternalIp, dstIp: VictimIp);
        driver.Inject(frame);

        Assert.True(WaitUntil(() => driver.SentFrames.Any(f =>
            f.Length == frame.Length &&
            f.AsSpan(..6).SequenceEqual(NetKeys.UInt64ToMac(Victim).GetAddressBytes()) &&
            f.AsSpan(6..12).SequenceEqual(NetKeys.UInt64ToMac(Local).GetAddressBytes()))));
        await engine.StopAndHealAsync();
    }

    [Theory]
    [InlineData(EngineKind.Legacy)]
    [InlineData(EngineKind.Modern)]
    public async Task Upload_IsForwarded_ToRouterMac(EngineKind kind)
    {
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        using var engine = OpenLearned(kind, catalog, driver);
        Assert.True(await engine.StartShapingAsync());

        driver.Inject(PacketClassifierTests.IpFrame(dstMac: Local, srcMac: Victim, srcIp: VictimIp, dstIp: ExternalIp));

        Assert.True(WaitUntil(() => driver.SentFrames.Any(f =>
            f.Length == 60 &&
            f.AsSpan(..6).SequenceEqual(NetKeys.UInt64ToMac(Router).GetAddressBytes()) &&
            f.AsSpan(6..12).SequenceEqual(NetKeys.UInt64ToMac(Local).GetAddressBytes()))));
        await engine.StopAndHealAsync();
    }

    [Theory]
    [InlineData(EngineKind.Legacy)]
    [InlineData(EngineKind.Modern)]
    public async Task Blocked_Device_GetsNoForwarding(EngineKind kind)
    {
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        using var engine = OpenLearned(kind, catalog, driver);
        Assert.True(catalog.TryGetByIp(VictimIp, out var victim));
        victim!.Redirect = false; // Blocked
        Assert.True(await engine.StartShapingAsync());

        int before = driver.SentFrames.Count(f => f.Length == 60);
        driver.Inject(PacketClassifierTests.IpFrame(Local, Router, ExternalIp, VictimIp));
        driver.Inject(PacketClassifierTests.IpFrame(Local, Victim, VictimIp, ExternalIp));
        Thread.Sleep(300);
        Assert.Equal(before, driver.SentFrames.Count(f => f.Length == 60)); // nothing forwarded
        Assert.True(engine.Dropped >= 2);
        await engine.StopAndHealAsync();
    }

    [Fact]
    public async Task Legacy_HardCap_AllowsThenDrops()
    {
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        using var engine = OpenLearned(EngineKind.Legacy, catalog, driver);
        Assert.True(catalog.TryGetByIp(VictimIp, out var victim));
        victim!.CapDownBytesPerSec = 100; // 100 B/s hard window
        Assert.True(await engine.StartShapingAsync());

        for (int i = 0; i < 5; i++)
            driver.Inject(PacketClassifierTests.IpFrame(Local, Router, ExternalIp, VictimIp));
        Thread.Sleep(300);
        int forwarded = driver.SentFrames.Count(f => f.Length == 60);
        Assert.InRange(forwarded, 1, 2); // first passes, rest dropped (may straddle one 1s reset)
        await engine.StopAndHealAsync();
    }

    [Fact]
    public async Task Modern_TokenBucket_AllowsBurstThenShapes()
    {
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        using var engine = OpenLearned(EngineKind.Modern, catalog, driver);
        Assert.True(catalog.TryGetByIp(VictimIp, out var victim));
        victim!.CapDownBytesPerSec = 60; // 60 B/s → capacity 3 tokens (50 ms)
        Assert.True(await engine.StartShapingAsync());

        for (int i = 0; i < 3; i++)
            driver.Inject(PacketClassifierTests.IpFrame(Local, Router, ExternalIp, VictimIp));
        Thread.Sleep(200); // well under the ~1 s needed to refill one frame's worth
        int forwarded = driver.SentFrames.Count(f => f.Length == 60);
        Assert.Equal(1, forwarded); // one frame passed on deficit; the rest are dropped
        await engine.StopAndHealAsync();
    }

    [Fact]
    public async Task Modern_Forwards_VlanTaggedDownload()
    {
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        using var engine = OpenLearned(EngineKind.Modern, catalog, driver);
        Assert.True(await engine.StartShapingAsync());

        driver.Inject(PacketClassifierTests.IpFrame(Local, Router, ExternalIp, VictimIp, vlanTagged: true));
        Assert.True(WaitUntil(() => driver.SentFrames.Any(f =>
            f.AsSpan(..6).SequenceEqual(NetKeys.UInt64ToMac(Victim).GetAddressBytes()))));
        await engine.StopAndHealAsync();
    }

    [Fact]
    public async Task Modern_DoesNotForward_WhenRouterMacUnknown()
    {
        // victim poisoned but router MAC unknown → uploads must DROP, never hit broadcast (M2)
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        using var engine = CreateEngine(EngineKind.Modern, catalog, driver);
        Assert.True(engine.Open(TestAdapter));
        driver.Inject(ArpReply(Router, RouterIp, Local, LocalIp)); // gateway known so shaping can start…
        Assert.True(WaitUntil(() => catalog.TryGetByIp(RouterIp, out var gw) && gw.HasKnownMac));
        driver.Inject(ArpReply(Victim, VictimIp, Local, LocalIp));
        Assert.True(WaitUntil(() => catalog.TryGetByIp(VictimIp, out var v) && v.HasKnownMac));
        Assert.True(await engine.StartShapingAsync());

        // …then simulate the gateway MAC being lost mid-session:
        Assert.True(catalog.TryGetByIp(RouterIp, out var gw2));
        gw2!.MacKey = 0;

        driver.Inject(PacketClassifierTests.IpFrame(Local, Victim, VictimIp, ExternalIp));
        Thread.Sleep(200);
        Assert.DoesNotContain(driver.SentFrames, f => f.Length == 60);
        Assert.True(engine.Dropped >= 1);
        await engine.StopAndHealAsync();
    }

    [Theory]
    [InlineData(EngineKind.Legacy)]
    [InlineData(EngineKind.Modern)]
    public async Task Stop_Heals_EveryTarget_35Pairs(EngineKind kind)
    {
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        using var engine = OpenLearned(kind, catalog, driver);
        Assert.True(await engine.StartShapingAsync());
        await engine.StopAndHealAsync();

        var restores = driver.SentFrames.Where(IsArpReply).Select(f =>
        {
            ArpFrame.TryParse(f, out var a);
            return a;
        }).ToList();
        // 35 genuine "router is at routerMAC" frames to the victim…
        Assert.Equal(35, restores.Count(a => a.EthDstMac == Victim && a.SenderMac == Router && a.SenderIp == RouterIp && a.TargetIp == VictimIp));
        // …and 35 "victim is at victimMAC" frames to the router
        Assert.Equal(35, restores.Count(a => a.EthDstMac == Router && a.SenderMac == Victim && a.SenderIp == VictimIp && a.TargetIp == RouterIp));
        Assert.Equal(EngineRunState.Idle, engine.State);
    }

    [Fact]
    public async Task Orchestrator_AdapterSwitchWhileShaping_HealsOldLanFirst()
    {
        // C3-class guard: changing adapter while shaping must heal the OLD LAN before
        // the device catalog (and its target list) is dropped.
        var driver = new LoopbackDriver();
        var orch = new EngineOrchestrator(driver, KnownDevicesStore.InMemory(), EngineKind.Modern);
        Assert.True(orch.OpenAdapter(TestAdapter));
        driver.Inject(ArpReply(Router, RouterIp, Local, LocalIp));
        driver.Inject(ArpReply(Victim, VictimIp, Local, LocalIp));
        Assert.True(WaitUntil(() =>
            orch.Catalog.TryGetByIp(RouterIp, out var gw) && gw.HasKnownMac &&
            orch.Catalog.TryGetByIp(VictimIp, out var v) && v.HasKnownMac));
        Assert.True(await orch.StartShapingAsync());

        int sentBeforeSwitch = driver.SentFrames.Count;

        // Switch to a different adapter (different subnet) while shaping is active.
        var other = TestAdapter with
        {
            Id = "{00000000-0000-0000-0000-000000000002}",
            Name = "Loopback 2",
            LocalIpKey = 0x0A000001,   // 10.0.0.1
            GatewayIpKey = 0x0A0000FE // 10.0.0.254
        };
        Assert.True(orch.OpenAdapter(other));

        var afterSwitch = driver.SentFrames.Skip(sentBeforeSwitch).Where(IsArpReply).Select(f =>
        {
            ArpFrame.TryParse(f, out var a);
            return a;
        }).ToList();

        // 35 genuine "router is at routerMAC" frames to the OLD victim before the catalog dropped it.
        Assert.Equal(35, afterSwitch.Count(a =>
            a.EthDstMac == Victim && a.SenderMac == Router && a.SenderIp == RouterIp && a.TargetIp == VictimIp));
        // The old LAN devices are gone; the new adapter is opening.
        Assert.False(orch.Catalog.TryGetByIp(VictimIp, out _));
        orch.Dispose();
    }

    [Fact]
    public async Task Orchestrator_Swap_HealsThenSwitchesEngine()
    {
        var driver = new LoopbackDriver();
        var orch = new EngineOrchestrator(driver, KnownDevicesStore.InMemory(), EngineKind.Modern);
        Assert.True(orch.OpenAdapter(TestAdapter));
        driver.Inject(ArpReply(Router, RouterIp, Local, LocalIp));
        driver.Inject(ArpReply(Victim, VictimIp, Local, LocalIp));
        Assert.True(WaitUntil(() =>
            orch.Catalog.TryGetByIp(RouterIp, out var gw) && gw.HasKnownMac &&
            orch.Catalog.TryGetByIp(VictimIp, out var v) && v.HasKnownMac));
        Assert.True(await orch.StartShapingAsync());

        int sentBeforeSwap = driver.SentFrames.Count;
        Assert.True(await orch.SwitchEngineAsync(EngineKind.Legacy));

        Assert.Equal(EngineKind.Legacy, orch.CurrentKind);
        Assert.Equal(EngineRunState.Shaping, orch.State);
        // the outgoing Modern engine healed the victim during the swap (35 pairs):
        var afterSwap = driver.SentFrames.Skip(sentBeforeSwap).Where(IsArpReply).Select(f =>
        {
            ArpFrame.TryParse(f, out var a);
            return a;
        }).ToList();
        Assert.Contains(afterSwap, a => a.EthDstMac == Victim && a.SenderMac == Router && a.TargetIp == VictimIp);
        // and the new Legacy engine keeps poisoning (4-frame bursts):
        Assert.True(WaitUntil(() => afterSwap.Count(a => a.EthDstMac == Victim && a.SenderMac == Local) >= 1 ||
            driver.SentFrames.Skip(sentBeforeSwap).Where(IsArpReply).Select(f => { ArpFrame.TryParse(f, out var a); return a; })
                .Count(a => a.EthDstMac == Victim && a.SenderMac == Local) >= 1));
        await orch.StopAndHealAsync();
        orch.Dispose();
    }
}
