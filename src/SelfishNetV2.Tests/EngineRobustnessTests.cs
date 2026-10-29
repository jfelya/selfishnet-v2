using System.Diagnostics;
using SelfishNetV2.Core;
using SelfishNetV2.Drivers;
using SelfishNetV2.Engines;
using Xunit;

namespace SelfishNetV2.Tests;

/// <summary>
/// Robustness behaviors added after real-network feedback:
/// reactive ARP defense, broadcast-gratuitous healing, heal-on-untarget,
/// periodic auto-discovery, and crash resistance under large traffic bursts.
/// </summary>
public class EngineRobustnessTests
{
    private const ulong Local = 0x020000000001;
    private const ulong Router = 0x5CAA00112233;
    private const ulong Victim = 0xAABBCCDDEE01;
    private const uint LocalIp = 0xC0A80132;
    private const uint RouterIp = 0xC0A80101;
    private const uint VictimIp = 0xC0A80164;
    private const uint ExternalIp = 0x08080808;

    private static AdapterInfo Adapter => new(
        "{00000000-0000-0000-0000-000000000001}", "Loopback", "Loopback test adapter",
        LocalIp, 0xFFFFFF00, RouterIp, Local, "loopback0");

    private static AdapterInfo Adapter16 => new(
        "{00000000-0000-0000-0000-000000000001}", "Loopback", "Loopback /16 test adapter",
        0x0A000132, 0xFFFF0000, RouterIp, Local, "loopback0");

    private static byte[] ArpReply(ulong senderMac, uint senderIp, ulong targetMac, uint targetIp) =>
        ArpFrameBuilder.Build(targetMac, senderMac, senderMac, senderIp, targetMac, targetIp, 2);

    private static bool IsArp(byte[] f, ushort opcode)
    {
        if (f.Length != ArpFrameBuilder.FrameLength || f[12] != 0x08 || f[13] != 0x06) return false;
        ushort op = (ushort)((f[20] << 8) | f[21]);
        return op == opcode;
    }

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

    private static (LoopbackDriver driver, DeviceCatalog catalog, IEngineSession engine) OpenLearned(EngineKind kind)
    {
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        IEngineSession engine = kind == EngineKind.Legacy
            ? new LegacyEngine(catalog, driver)
            : new ModernEngine(catalog, driver);
        Assert.True(engine.Open(Adapter));
        driver.Inject(ArpReply(Router, RouterIp, Local, LocalIp));
        driver.Inject(ArpReply(Victim, VictimIp, Local, LocalIp));
        Assert.True(WaitUntil(() =>
            catalog.TryGetByIp(RouterIp, out var gw) && gw.HasKnownMac &&
            catalog.TryGetByIp(VictimIp, out var v) && v.HasKnownMac));
        return (driver, catalog, engine);
    }

    [Fact]
    public async Task Modern_ReactivePoison_AnswersVictimArpRequestInstantly()
    {
        var (driver, _, engine) = OpenLearned(EngineKind.Modern);
        using var _e = engine;
        Assert.True(await engine.StartShapingAsync());
        Assert.True(WaitUntil(() => driver.SentFrames.Count(f => IsArp(f, 2)) >= 1));

        int VictimPoisons() => driver.SentFrames.Count(f =>
        {
            if (!IsArp(f, 2)) return false;
            ArpFrame.TryParse(f, out var a);
            return a.EthDstMac == Victim && a.SenderIp == RouterIp && a.TargetIp == VictimIp;
        });

        int before = VictimPoisons();

        // Victim asks "who has the gateway?" — the engine must answer immediately,
        // not wait for the next 2 s round.
        driver.Inject(ArpFrameBuilder.Build(NetKeys.BroadcastMac, Victim, Victim, VictimIp, 0, RouterIp, 1));

        Assert.True(WaitUntil(() => VictimPoisons() - before >= 2, 1200),
            $"expected instant poison replies, saw {VictimPoisons() - before}");
        await engine.StopAndHealAsync();
    }

    [Fact]
    public async Task Heal_SendsBroadcastGratuitousAnnouncements()
    {
        var (driver, _, engine) = OpenLearned(EngineKind.Modern);
        using var _e = engine;
        Assert.True(await engine.StartShapingAsync());
        await engine.StopAndHealAsync();

        int BroadcastRouterAnnouncements = driver.SentFrames.Count(f =>
        {
            if (!IsArp(f, 2)) return false;
            ArpFrame.TryParse(f, out var a);
            return a.EthDstMac == NetKeys.BroadcastMac && a.SenderMac == Router && a.SenderIp == RouterIp;
        });
        int BroadcastVictimAnnouncements = driver.SentFrames.Count(f =>
        {
            if (!IsArp(f, 2)) return false;
            ArpFrame.TryParse(f, out var a);
            return a.EthDstMac == NetKeys.BroadcastMac && a.SenderMac == Victim && a.SenderIp == VictimIp;
        });

        Assert.Equal(3, BroadcastRouterAnnouncements);
        Assert.Equal(3, BroadcastVictimAnnouncements);
    }

    [Fact]
    public async Task UntargetMidSession_HealsDeviceBeforeDroppingIt()
    {
        var (driver, catalog, engine) = OpenLearned(EngineKind.Modern);
        using var _e = engine;
        Assert.True(await engine.StartShapingAsync());
        Assert.True(catalog.TryGetByIp(VictimIp, out var victim));

        int RestoresToVictim() => driver.SentFrames.Count(f =>
        {
            if (!IsArp(f, 2)) return false;
            ArpFrame.TryParse(f, out var a);
            return a.EthDstMac == Victim && a.SenderMac == Router && a.SenderIp == RouterIp && a.TargetIp == VictimIp;
        });

        int before = RestoresToVictim();
        victim!.Targeted = false;   // user unchecks "Spoof" while shaping

        // Next burst (<= 2 s) must heal the device instead of re-poisoning it.
        Assert.True(WaitUntil(() => RestoresToVictim() - before >= 35, 5000),
            $"expected 35 restore frames, saw {RestoresToVictim() - before}");
        await engine.StopAndHealAsync();
    }

    [Fact]
    public async Task HighVolumeBurst_DoesNotCrashAndKeepsForwarding()
    {
        // Regression for the original 2007 crash under sudden large transfers.
        var (driver, _, engine) = OpenLearned(EngineKind.Modern);
        using var _e = engine;
        Assert.True(await engine.StartShapingAsync());

        byte[] frame = PacketClassifierTests.IpFrame(Local, Router, ExternalIp, VictimIp, len: 1514);
        const int frames = 10_000;
        for (int i = 0; i < frames; i++)
            driver.Inject(frame);

        Assert.True(WaitUntil(() => engine.ForwardedDown + engine.Dropped >= frames, 30_000),
            $"processed {engine.ForwardedDown + engine.Dropped}/{frames}");
        Assert.Equal(EngineRunState.Shaping, engine.State);
        Assert.True(engine.ForwardedDown >= frames * 0.95,
            $"forwarded {engine.ForwardedDown}/{frames} (unlimited caps expected)");

        await engine.StopAndHealAsync();   // must still heal cleanly after the burst
        Assert.Equal(EngineRunState.Idle, engine.State);
    }

    [Fact]
    public async Task AutoDiscovery_SweepsPeriodically_WithoutManualScan()
    {
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        using var engine = new FastScanEngine(catalog, driver);
        Assert.True(engine.Open(Adapter));

        // No manual Discover call. Within a few seconds the background sweep must emit
        // one ARP request per host in the /24 (254 hosts).
        bool swept = WaitUntil(() => driver.SentFrames.Count(f => IsArp(f, 1)) >= 250, 8000);
        Assert.True(swept, $"saw {driver.SentFrames.Count(f => IsArp(f, 1))} ARP requests");
        await engine.StopAndHealAsync();
    }

    private static int CountRestoresToVictim(LoopbackDriver driver) => driver.SentFrames.Count(f =>
    {
        if (!IsArp(f, 2)) return false;
        ArpFrame.TryParse(f, out var a);
        return a.EthDstMac == Victim && a.SenderMac == Router && a.SenderIp == RouterIp && a.TargetIp == VictimIp;
    });

    [Fact]
    public void Discovery_StopsAtHostBudget_AndRaisesCompleted()
    {
        var driver = new LoopbackDriver();
        var catalog = new DeviceCatalog();
        using var engine = new BoundedScanEngine(catalog, driver);
        bool completed = false;
        engine.DiscoveryCompleted += (_, _) => completed = true;

        Assert.True(engine.Open(Adapter16));
        engine.StartDiscovery();
        Thread.Sleep(200); // let the current (unbounded) sweep run briefly
        engine.StopDiscovery();

        Assert.True(WaitUntil(() => completed, 2000), "discovery completion event");
        // Count only the sweep's 10.0.0.0/16 targets; Open may also send one
        // gateway ARP request for the unrelated test-router IP.
        int requests = driver.SentFrames.Count(f =>
        {
            if (!IsArp(f, 1)) return false;
            ArpFrame.TryParse(f, out var a);
            return (a.TargetIp & 0xFFFF0000) == 0x0A000000;
        });
        Assert.InRange(requests, 1, 8);
    }

    [Fact]
    public async Task Close_AfterSuccessfulStop_DoesNotReHeal()
    {
        // Disposing the engine must not re-send the whole healing burst when every
        // claim was already resolved by Stop (no spurious traffic on exit).
        var (driver, _, engine) = OpenLearned(EngineKind.Modern);
        using var _e = engine;
        Assert.True(await engine.StartShapingAsync());
        await engine.StopAndHealAsync();

        int before = CountRestoresToVictim(driver);
        engine.Close();
        Thread.Sleep(150);
        Assert.Equal(before, CountRestoresToVictim(driver));
    }

    [Fact]
    public async Task Close_RetriesOutstandingHealClaims_WhenGatewayMacWasLost()
    {
        // If a stop could not heal (gateway MAC unknown), the claim is kept and a
        // later Close must retry it instead of giving up silently.
        var (driver, catalog, engine) = OpenLearned(EngineKind.Modern);
        using var _e = engine;
        Assert.True(await engine.StartShapingAsync());
        Assert.True(catalog.TryGetByIp(RouterIp, out var gateway));
        gateway!.MacKey = 0;               // simulate mid-session gateway MAC loss

        await engine.StopAndHealAsync();   // cannot heal: claim is retained
        int before = CountRestoresToVictim(driver);
        Assert.Equal(0, before);

        gateway.MacKey = Router;           // gateway known again
        engine.Close();                    // must retry the outstanding claim
        Assert.True(WaitUntil(() => CountRestoresToVictim(driver) - before >= 35, 3000),
            $"expected retry heal on close, saw {CountRestoresToVictim(driver) - before}");
    }

    /// <summary>Modern engine with a fast auto-scan interval so the test stays quick.</summary>
    private sealed class FastScanEngine : ModernEngine
    {
        public FastScanEngine(DeviceCatalog catalog, IPcapDriver driver) : base(catalog, driver) { }
        protected override int AutoScanIntervalMs => 1000;
    }

    /// <summary>No auto-scan, no pacing, and a tiny host budget for a fast bounded-sweep test.</summary>
    private sealed class BoundedScanEngine : ModernEngine
    {
        public BoundedScanEngine(DeviceCatalog catalog, IPcapDriver driver) : base(catalog, driver) { }
        protected override int AutoScanIntervalMs => 0;
        protected override int DiscoveryPacingMs => 0;
        protected override int MaxSweepHosts => 8;
    }
}
