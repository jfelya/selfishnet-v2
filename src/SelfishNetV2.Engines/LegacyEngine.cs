using SelfishNetV2.Core;
using SelfishNetV2.Drivers;

namespace SelfishNetV2.Engines;

/// <summary>
/// LEGACY ENGINE — spec-locked replica of the original 2007 mechanics
/// (corrected per docs/ORIGINAL_FINDINGS.md):
///   · non-promiscuous capture, "ip" filter, fixed L2 offsets (no VLAN walk)
///   · spoof burst = FOUR frames per victim (incl. the inert codegen leftover)
///   · re-poison every 5000 ms; genuine restore ×35 on stop
///   · hard 1-second cap window: drop when cap != 0 &amp;&amp; cap &lt;= bytesThisSecond
///   · source-MAC classification; router MAC re-read per packet
/// Exists for A/B comparison and as the fallback when the Modern engine misbehaves.
/// </summary>
public sealed class LegacyEngine : EngineSessionBase
{
    public LegacyEngine(DeviceCatalog catalog, IPcapDriver driver) : base(catalog, driver) { }

    public override EngineKind Kind => EngineKind.Legacy;

    protected override string IpFilter => "ip";
    protected override bool SendInertSelfFrame => true;

    // Spec-locked: original 5 s cadence, no reactive replies. Robust healing is still
    // shared with the Modern engine (safety behavior, not wire-protocol behavior).

    protected override PacketFlow Classify(ReadOnlySpan<byte> frame, in ForwardingContext ctx, out PacketMatch match) =>
        PacketClassifier.ClassifyFixed(frame, ctx, out match);

    /// <summary>Original hard-window semantics: cap != 0 &amp;&amp; cap &lt;= bytesThisSecond → drop.</summary>
    protected override bool TryPassDown(Device dev, int bytes)
    {
        int cap = dev.CapDownBytesPerSec;
        return cap == 0 || cap > dev.DownWindow;
    }

    protected override bool TryPassUp(Device dev, int bytes)
    {
        int cap = dev.CapUpBytesPerSec;
        return cap == 0 || cap > dev.UpWindow;
    }
}

public sealed class LegacyEngineFactory : IEngineFactory
{
    public EngineKind Kind => EngineKind.Legacy;
    public IEngineSession Create(DeviceCatalog catalog, IPcapDriver driver) => new LegacyEngine(catalog, driver);
}
