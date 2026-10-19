using SelfishNetV2.Core;
using Xunit;

namespace SelfishNetV2.Tests;

/// <summary>
/// Golden-vector parity tests: every frame builder is diffed byte-for-byte against
/// the layout verified from the original binary (docs/ARCHITECTURE_ANALYSIS.md §3.1.3
/// + docs/ORIGINAL_FINDINGS.md). Any drift in the builder breaks these tests.
/// </summary>
public class ArpFrameBuilderParityTests
{
    private const ulong Local = 0x001122334455;   // 00:11:22:33:44:55
    private const ulong Router = 0x5CAA00112233;  // 5C:AA:00:11:22:33
    private const ulong Victim = 0xAABBCCDDEE01;  // AA:BB:CC:DD:EE:01
    private const uint LocalIp = 0xC0A80132;      // 192.168.1.50
    private const uint RouterIp = 0xC0A80101;     // 192.168.1.1
    private const uint VictimIp = 0xC0A80164;     // 192.168.1.100

    private static byte[] B(params int[] bytes) => bytes.Select(b => (byte)b).ToArray();

    [Fact]
    public void Request_MatchesGoldenVector()
    {
        byte[] expected = B(
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,       // dst: broadcast
            0x00, 0x11, 0x22, 0x33, 0x44, 0x55,       // src: local
            0x08, 0x06,                                // EtherType ARP
            0x00, 0x01,                                // HW type Ethernet
            0x08, 0x00,                                // Proto IPv4
            0x06, 0x04,                                // sizes
            0x00, 0x01,                                // opcode: request
            0x00, 0x11, 0x22, 0x33, 0x44, 0x55,       // sender MAC
            0xC0, 0xA8, 0x01, 0x32,                    // sender IP 192.168.1.50
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,       // target MAC: zero
            0xC0, 0xA8, 0x01, 0x01);                   // target IP 192.168.1.1
        byte[] frame = ArpFrameBuilder.Build(NetKeys.BroadcastMac, Local, Local, LocalIp, 0, RouterIp, 1);
        Assert.Equal(expected, frame);
    }

    [Fact]
    public void Poison_ToVictim_MatchesGoldenVector()
    {
        // "router is at us" → sent to the victim (findings F3 frame 1)
        byte[] frame = ArpFrameBuilder.Build(Victim, Local, Local, RouterIp, Victim, VictimIp, 2);
        Assert.Equal(42, frame.Length);
        Assert.Equal(B(0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0x01), frame[..6]);   // dst = victim MAC
        Assert.Equal(B(0x00, 0x11, 0x22, 0x33, 0x44, 0x55), frame[6..12]); // src = local MAC
        Assert.Equal(B(0x08, 0x06), frame[12..14]);
        Assert.Equal(B(0x00, 0x02), frame[20..22]);                        // opcode: reply
        Assert.Equal(B(0x00, 0x11, 0x22, 0x33, 0x44, 0x55), frame[22..28]);// sender MAC = us
        Assert.Equal(B(0xC0, 0xA8, 0x01, 0x01), frame[28..32]);            // sender IP = router
        Assert.Equal(B(0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0x01), frame[32..38]);
        Assert.Equal(B(0xC0, 0xA8, 0x01, 0x64), frame[38..42]);
    }

    [Fact]
    public void Poison_ToRouter_MatchesGoldenVector()
    {
        // "victim is at us" → sent to the router (findings F3 frame 2)
        byte[] frame = ArpFrameBuilder.Build(Router, Local, Local, VictimIp, Router, RouterIp, 2);
        Assert.Equal(B(0x5C, 0xAA, 0x00, 0x11, 0x22, 0x33), frame[..6]);
        Assert.Equal(B(0xC0, 0xA8, 0x01, 0x64), frame[28..32]);            // sender IP = victim
        Assert.Equal(B(0xC0, 0xA8, 0x01, 0x01), frame[38..42]);            // target IP = router
    }

    [Fact]
    public void SelfRefresh_MatchesGoldenVector()
    {
        // findings F3 frame 3: eth dst = us, eth src = router; "routerIP is at routerMAC" to us
        var buf = new byte[42];
        ArpFrameBuilder.BuildSelfRefresh(buf, Local, LocalIp, Router, RouterIp);
        Assert.Equal(B(0x00, 0x11, 0x22, 0x33, 0x44, 0x55), buf[..6]);     // eth dst = local
        Assert.Equal(B(0x5C, 0xAA, 0x00, 0x11, 0x22, 0x33), buf[6..12]);   // eth src = router
        Assert.Equal(B(0x00, 0x02), buf[20..22]);
        Assert.Equal(B(0x5C, 0xAA, 0x00, 0x11, 0x22, 0x33), buf[22..28]);  // ARP sender = router
        Assert.Equal(B(0xC0, 0xA8, 0x01, 0x01), buf[28..32]);
        Assert.Equal(B(0xC0, 0xA8, 0x01, 0x32), buf[38..42]);              // target = us
    }

    [Fact]
    public void InertSelf_MatchesGoldenVector()
    {
        // findings F3 frame 4: eth dst = eth src = us; "victimIP is at victimMAC"
        var buf = new byte[42];
        ArpFrameBuilder.BuildInertSelf(buf, Local, LocalIp, Victim, VictimIp);
        Assert.Equal(B(0x00, 0x11, 0x22, 0x33, 0x44, 0x55), buf[..6]);
        Assert.Equal(B(0x00, 0x11, 0x22, 0x33, 0x44, 0x55), buf[6..12]);
        Assert.Equal(B(0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0x01), buf[22..28]);
        Assert.Equal(B(0xC0, 0xA8, 0x01, 0x64), buf[28..32]);
    }

    [Fact]
    public void Restore_MatchesGoldenVector()
    {
        // genuine healing: tell victim the router's REAL MAC
        var buf = new byte[42];
        ArpFrameBuilder.BuildRestore(buf, Router, RouterIp, Victim, VictimIp);
        Assert.Equal(B(0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0x01), buf[..6]);
        Assert.Equal(B(0x5C, 0xAA, 0x00, 0x11, 0x22, 0x33), buf[6..12]);   // src = real router MAC
        Assert.Equal(B(0x00, 0x02), buf[20..22]);
        Assert.Equal(B(0x5C, 0xAA, 0x00, 0x11, 0x22, 0x33), buf[22..28]);
        Assert.Equal(B(0xC0, 0xA8, 0x01, 0x01), buf[28..32]);
    }

    [Fact]
    public void Parse_RoundTrips()
    {
        byte[] frame = ArpFrameBuilder.Build(Victim, Local, Local, RouterIp, Victim, VictimIp, 2);
        Assert.True(ArpFrame.TryParse(frame, out var a));
        Assert.Equal(Victim, a.EthDstMac);
        Assert.Equal(Local, a.EthSrcMac);
        Assert.Equal(2, a.Opcode);
        Assert.Equal(Local, a.SenderMac);
        Assert.Equal(RouterIp, a.SenderIp);
        Assert.Equal(Victim, a.TargetMac);
        Assert.Equal(VictimIp, a.TargetIp);
    }

    [Fact]
    public void Parse_RejectsNonArpAndShort()
    {
        byte[] ip = B(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x08, 0x00,
                      0x45, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                      1, 2, 3, 4, 5, 6, 7, 8);
        Assert.False(ArpFrame.TryParse(ip, out _));          // EtherType IPv4, not ARP
        Assert.False(ArpFrame.TryParse(new byte[20], out _)); // too short
        Assert.False(ArpFrame.TryParse(ReadOnlySpan<byte>.Empty, out _));
    }
}
