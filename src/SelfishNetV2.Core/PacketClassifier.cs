namespace SelfishNetV2.Core;

/// <summary>Traffic direction of one captured IPv4 frame (per ORIGINAL_FINDINGS F2).</summary>
public enum PacketFlow
{
    Ignored = 0,
    /// <summary>Emitted by us (src MAC == local). Counted on the local row, never forwarded.</summary>
    Own,
    /// <summary>Router → us. Counted on the local row, never forwarded.</summary>
    LocalDownload,
    /// <summary>Router → victim (match by destination IP). Rewrite dst=victim, src=us.</summary>
    Download,
    /// <summary>Victim → router (match by source MAC). Rewrite dst=router, src=us.</summary>
    Upload,
}

/// <summary>Everything the classifier needs, in numeric form. RouterMac is re-read
/// from the catalog per packet by the engine (the original re-read it every loop).</summary>
public readonly struct ForwardingContext
{
    public required ulong LocalMac { get; init; }
    public required uint LocalIp { get; init; }
    public required ulong RouterMac { get; init; }
}

/// <summary>Lookup key for the device table. Download → DstIp; Upload → SrcMac.</summary>
public readonly struct PacketMatch
{
    public uint DstIp { get; init; }
    public ulong SrcMac { get; init; }
}

/// <summary>
/// Zero-allocation frame classifier. The original relied on the "ip" BPF filter and
/// used fixed offsets; the Modern engine additionally walks 802.1Q VLAN tags.
/// </summary>
public static class PacketClassifier
{
    public const int EthernetHeaderLength = 14;

    /// <summary>Original behavior: assumes untagged Ethernet II, IPv4 at offset 14.
    /// Src IP at 26..29, dst IP at 30..33. Caller guarantees an IPv4 frame.</summary>
    public static PacketFlow ClassifyFixed(ReadOnlySpan<byte> frame, in ForwardingContext ctx, out PacketMatch match) =>
        Classify(frame, EthernetHeaderLength, ctx, out match);

    /// <summary>Modern behavior: walks 802.1Q/802.1ad tags; returns Ignored for non-IPv4.</summary>
    public static PacketFlow ClassifyAuto(ReadOnlySpan<byte> frame, in ForwardingContext ctx, out PacketMatch match)
    {
        int l3 = FindIpv4Offset(frame);
        if (l3 < 0)
        {
            match = default;
            return PacketFlow.Ignored;
        }
        return Classify(frame, l3, ctx, out match);
    }

    /// <summary>Layer-3 offset of the IPv4 header, or -1 when the frame is not IPv4
    /// (handles 802.1Q 0x8100, QinQ 0x88A8 / 0x9100 tags).</summary>
    public static int FindIpv4Offset(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < EthernetHeaderLength) return -1;
        int offset = 12;
        ushort etherType = ReadUInt16(frame, offset);
        while (etherType is 0x8100 or 0x88A8 or 0x9100)
        {
            offset += 4; // tag: TCI(2) + next EtherType(2)
            if (frame.Length < offset + 2 + 2) return -1;
            etherType = ReadUInt16(frame, offset);
        }
        return etherType == 0x0800 ? offset + 2 : -1;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> frame, int offset) =>
        (ushort)((frame[offset] << 8) | frame[offset + 1]);

    private static PacketFlow Classify(ReadOnlySpan<byte> frame, int l3Offset, in ForwardingContext ctx, out PacketMatch match)
    {
        match = default;
        if (frame.Length < l3Offset + 20) return PacketFlow.Ignored; // minimal IPv4 header

        ulong srcMac = NetKeys.MacToUInt64(frame.Slice(6, 6));
        if (srcMac == ctx.LocalMac) return PacketFlow.Own;

        uint srcIp = NetKeys.IpToUInt(frame.Slice(l3Offset + 12, 4));
        uint dstIp = NetKeys.IpToUInt(frame.Slice(l3Offset + 16, 4));
        _ = srcIp; // src IP not needed for classification (kept for debugging clarity)

        if (srcMac == ctx.RouterMac)
        {
            if (dstIp == ctx.LocalIp) return PacketFlow.LocalDownload;
            match = new PacketMatch { DstIp = dstIp };
            return PacketFlow.Download;
        }

        // Upload path: traffic explicitly addressed to us is never forwarded.
        if (dstIp == ctx.LocalIp) return PacketFlow.Ignored;
        match = new PacketMatch { SrcMac = srcMac };
        return PacketFlow.Upload;
    }
}
