// FREE MODEL TEST PROTOTYPE — byte-for-byte ARP frame builder.
// Matches CArp::buildArpPacket: 42-byte Ethernet II + ARP (see AI_ENVIRONMENT_NOTES.md).

using System.Net;
using System.Net.NetworkInformation;

namespace SelfishNetV2.Test.Core;

public static class ArpPacket
{
    public const int FrameLength = 42;
    public const ushort EtherTypeArp = 0x0806;

    public static byte[] Build(PhysicalAddress dstMac, PhysicalAddress srcMac,
        PhysicalAddress senderMac, IPAddress senderIp,
        PhysicalAddress targetMac, IPAddress targetIp, ushort opcode)
    {
        var frame = new byte[FrameLength];
        CopyMac(dstMac, frame, 0);
        CopyMac(srcMac, frame, 6);
        frame[12] = 0x08; frame[13] = 0x06;           // EtherType ARP
        frame[14] = 0x00; frame[15] = 0x01;           // HW type Ethernet
        frame[16] = 0x08; frame[17] = 0x00;           // Proto IPv4
        frame[18] = 6;                                // HW addr size
        frame[19] = 4;                                // Proto addr size
        frame[20] = (byte)(opcode >> 8); frame[21] = (byte)(opcode & 0xFF);
        CopyMac(senderMac, frame, 22);
        CopyIp(senderIp, frame, 28);
        CopyMac(targetMac, frame, 32);
        CopyIp(targetIp, frame, 38);
        return frame;
    }

    public static byte[] BuildRequest(PhysicalAddress localMac, IPAddress localIp, IPAddress targetIp) =>
        Build(PhysicalAddress.Parse("FF-FF-FF-FF-FF-FF"), localMac, localMac, localIp,
            PhysicalAddress.Parse("00-00-00-00-00-00"), targetIp, opcode: 1);

    /// <summary>Poison pair element: tells <paramref name="targetIp"/> that <paramref name="impersonatedIp"/> is at our MAC.</summary>
    public static byte[] BuildSpoofReply(PhysicalAddress localMac, IPAddress impersonatedIp,
        PhysicalAddress targetMac, IPAddress targetIp) =>
        Build(targetMac, localMac, localMac, impersonatedIp, targetMac, targetIp, opcode: 2);

    /// <summary>Self-refresh frame (original Spoof packets 3-4 pattern): an ARP reply
    /// from the router to us, injected on the wire so our own listener re-learns
    /// the TRUE router MAC. Keeps the device table honest every spoof round.</summary>
    public static byte[] BuildSelfRefresh(PhysicalAddress localMac, IPAddress localIp,
        PhysicalAddress routerMac, IPAddress routerIp) =>
        Build(localMac, routerMac, routerMac, routerIp, localMac, localIp, opcode: 2);

    /// <summary>Genuine restore frame: tells <paramref name="targetIp"/> the real MAC of <paramref name="realIp"/>.</summary>
    public static byte[] BuildRestoreReply(PhysicalAddress realMac, IPAddress realIp,
        PhysicalAddress targetMac, IPAddress targetIp) =>
        Build(targetMac, realMac, realMac, realIp, targetMac, targetIp, opcode: 2);

    public static bool TryParse(byte[] frame, int length, out ushort opcode,
        out PhysicalAddress senderMac, out IPAddress senderIp,
        out PhysicalAddress targetMac, out IPAddress targetIp)
    {
        opcode = 0;
        senderMac = PhysicalAddress.None; senderIp = IPAddress.None;
        targetMac = PhysicalAddress.None; targetIp = IPAddress.None;
        if (length < FrameLength) return false;
        if (frame[12] != 0x08 || frame[13] != 0x06) return false;
        opcode = (ushort)((frame[20] << 8) | frame[21]);
        senderMac = new PhysicalAddress(frame[22..28]);
        senderIp = new IPAddress(frame[28..32]);
        targetMac = new PhysicalAddress(frame[32..38]);
        targetIp = new IPAddress(frame[38..42]);
        return true;
    }

    private static void CopyMac(PhysicalAddress mac, byte[] dst, int offset)
    {
        var b = mac.GetAddressBytes();
        if (b.Length != 6) throw new ArgumentException("MAC must be 6 bytes");
        Buffer.BlockCopy(b, 0, dst, offset, 6);
    }

    private static void CopyIp(IPAddress ip, byte[] dst, int offset)
    {
        var b = ip.GetAddressBytes();
        if (b.Length != 4) throw new ArgumentException("Only IPv4 supported");
        Buffer.BlockCopy(b, 0, dst, offset, 4);
    }
}
