namespace SelfishNetV2.Core;

/// <summary>Zero-allocation parser for Ethernet II + ARP frames (42 bytes).</summary>
public readonly struct ArpFrame
{
    public ulong EthDstMac { get; init; }
    public ulong EthSrcMac { get; init; }
    public ushort Opcode { get; init; }
    public ulong SenderMac { get; init; }
    public uint SenderIp { get; init; }
    public ulong TargetMac { get; init; }
    public uint TargetIp { get; init; }

    public static bool TryParse(ReadOnlySpan<byte> frame, out ArpFrame arp)
    {
        arp = default;
        if (frame.Length < ArpFrameBuilder.FrameLength) return false;
        if (frame[12] != 0x08 || frame[13] != 0x06) return false; // EtherType ARP
        arp = new ArpFrame
        {
            EthDstMac = NetKeys.MacToUInt64(frame[..6]),
            EthSrcMac = NetKeys.MacToUInt64(frame.Slice(6, 6)),
            Opcode = (ushort)((frame[20] << 8) | frame[21]),
            SenderMac = NetKeys.MacToUInt64(frame.Slice(22, 6)),
            SenderIp = NetKeys.IpToUInt(frame.Slice(28, 4)),
            TargetMac = NetKeys.MacToUInt64(frame.Slice(32, 6)),
            TargetIp = NetKeys.IpToUInt(frame.Slice(38, 4)),
        };
        return true;
    }
}
