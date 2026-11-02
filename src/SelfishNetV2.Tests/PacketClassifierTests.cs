using SelfishNetV2.Core;
using Xunit;

namespace SelfishNetV2.Tests;

/// <summary>Frame classification per ORIGINAL_FINDINGS F2 (source-MAC based) + VLAN walk.</summary>
public class PacketClassifierTests
{
    private const ulong Local = 0x020000000001;
    private const ulong Router = 0x5CAA00112233;
    private const ulong Victim = 0xAABBCCDDEE01;
    private const uint LocalIp = 0xC0A80132;   // 192.168.1.50
    private const uint RouterIp = 0xC0A80101;  // 192.168.1.1
    private const uint VictimIp = 0xC0A80164;  // 192.168.1.100
    private const uint ExternalIp = 0x08080808; // 8.8.8.8

    private static readonly ForwardingContext Ctx = new() { LocalMac = Local, LocalIp = LocalIp, RouterMac = Router };

    internal static byte[] IpFrame(ulong dstMac, ulong srcMac, uint srcIp, uint dstIp, int len = 60, bool vlanTagged = false)
    {
        var f = new byte[len];
        NetKeys.WriteMac(f, 0, dstMac);
        NetKeys.WriteMac(f, 6, srcMac);
        int l3;
        if (vlanTagged)
        {
            f[12] = 0x81; f[13] = 0x00; f[14] = 0x00; f[15] = 0x01; // 802.1Q, TCI 1
            f[16] = 0x08; f[17] = 0x00;
            l3 = 18;
        }
        else
        {
            f[12] = 0x08; f[13] = 0x00;
            l3 = 14;
        }
        f[l3] = 0x45; // IPv4 + IHL 20
        f[l3 + 8] = 64; // TTL
        void WIp(int off, uint ip) { f[off] = (byte)(ip >> 24); f[off + 1] = (byte)(ip >> 16); f[off + 2] = (byte)(ip >> 8); f[off + 3] = (byte)ip; }
        WIp(l3 + 12, srcIp);
        WIp(l3 + 16, dstIp);
        return f;
    }

    [Fact] public void Own_Traffic_IsCountedNotForwarded()
    {
        var f = IpFrame(Router, Local, LocalIp, ExternalIp);
        Assert.Equal(PacketFlow.Own, PacketClassifier.ClassifyFixed(f, Ctx, out _));
    }

    [Fact] public void RouterToLocal_IsLocalDownload()
    {
        var f = IpFrame(Local, Router, ExternalIp, LocalIp);
        Assert.Equal(PacketFlow.LocalDownload, PacketClassifier.ClassifyFixed(f, Ctx, out _));
    }

    [Fact] public void RouterToVictim_IsDownload_MatchedByDstIp()
    {
        var f = IpFrame(Local, Router, ExternalIp, VictimIp);
        Assert.Equal(PacketFlow.Download, PacketClassifier.ClassifyFixed(f, Ctx, out var m));
        Assert.Equal(VictimIp, m.DstIp);
    }

    [Fact] public void VictimToRouter_IsUpload_MatchedBySrcMac()
    {
        var f = IpFrame(Router, Victim, VictimIp, ExternalIp);
        Assert.Equal(PacketFlow.Upload, PacketClassifier.ClassifyFixed(f, Ctx, out var m));
        Assert.Equal(Victim, m.SrcMac);
    }

    [Fact] public void LanTraffic_AddressedToUs_IsNeverForwarded()
    {
        var f = IpFrame(Local, Victim, VictimIp, LocalIp);
        Assert.Equal(PacketFlow.Ignored, PacketClassifier.ClassifyFixed(f, Ctx, out _));
    }

    [Fact] public void ShortFrames_AreIgnored()
    {
        Assert.Equal(PacketFlow.Ignored, PacketClassifier.ClassifyFixed(new byte[20], Ctx, out _));
    }

    [Fact] public void VlanTagged_IsFoundByAuto_NotByFixed()
    {
        var f = IpFrame(Local, Router, ExternalIp, VictimIp, vlanTagged: true);
        Assert.Equal(PacketFlow.Download, PacketClassifier.ClassifyAuto(f, Ctx, out var m));
        Assert.Equal(VictimIp, m.DstIp);
        // Fixed offsets read the tag as part of the IP header → dst IP is garbage → not our victim.
        var fixedFlow = PacketClassifier.ClassifyFixed(f, Ctx, out var fm);
        Assert.True(fixedFlow != PacketFlow.Download || fm.DstIp != VictimIp);
    }

    [Fact] public void NonIpv4_IsIgnoredByAuto()
    {
        byte[] arp = ArpFrameBuilder.Build(Victim, Local, Local, RouterIp, Victim, VictimIp, 2);
        Assert.Equal(PacketFlow.Ignored, PacketClassifier.ClassifyAuto(arp, Ctx, out _));
    }
}
