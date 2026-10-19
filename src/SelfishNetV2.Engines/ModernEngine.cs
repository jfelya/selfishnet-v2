using SelfishNetV2.Core;
using SelfishNetV2.Drivers;

namespace SelfishNetV2.Engines;

/// <summary>
/// MODERN ENGINE — same wire protocol as the Legacy engine, better machinery:
///   · VLAN-aware classification (802.1Q/802.1ad tag walk), filter "ip or (vlan and ip)"
///   · smooth token-bucket pacing (hi-res clock, clamped bursts) instead of the
///     original's bursty 1-second hard drop
///   · low-latency capture (mintocopy = 1 → immediate wakeup per packet)
///   · the inert 4th spoof frame is dropped (documented deviation — the other
///     three frames, including the router-MAC self-refresh, are sent identically)
/// Zero-allocation parsing and numeric-key lookups are inherited from the base.
/// </summary>
public class ModernEngine : EngineSessionBase   // unsealed: tests subclass to tune intervals
{
    public ModernEngine(DeviceCatalog catalog, IPcapDriver driver) : base(catalog, driver) { }

    public override EngineKind Kind => EngineKind.Modern;

    protected override string IpFilter => "ip or (vlan and ip)";
    protected override bool SendInertSelfFrame => false;

    /// <summary>2 s re-poison (original: 5 s) — shrinks the window in which a device
    /// could keep the true router MAC after re-ARPing.</summary>
    protected override int SpoofIntervalMs => 2000;

    /// <summary>Answer ARP races instantly (victim asks for the gateway / router answers
    /// the victim / victim announces itself) instead of waiting for the next round.</summary>
    protected override bool ReactivePoison => true;

    protected override PacketFlow Classify(ReadOnlySpan<byte> frame, in ForwardingContext ctx, out PacketMatch match) =>
        PacketClassifier.ClassifyAuto(frame, ctx, out match);

    protected override void ConfigureIpCapture(PcapCaptureSession session) =>
        session.TrySetMinToCopy(1); // wake on every packet: MITM relay must add ~0 latency

    protected override bool TryPassDown(Device dev, int bytes) =>
        dev.DownBucket.TryConsume(dev.CapDownBytesPerSec, bytes);

    protected override bool TryPassUp(Device dev, int bytes) =>
        dev.UpBucket.TryConsume(dev.CapUpBytesPerSec, bytes);
}

public sealed class ModernEngineFactory : IEngineFactory
{
    public EngineKind Kind => EngineKind.Modern;
    public IEngineSession Create(DeviceCatalog catalog, IPcapDriver driver) => new ModernEngine(catalog, driver);
}
