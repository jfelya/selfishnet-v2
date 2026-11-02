using System.Runtime.InteropServices;
using SelfishNetV2.Drivers;
using Xunit;

namespace SelfishNetV2.Tests;

/// <summary>Guards the C1 defect class: pcap_pkthdr must be the native 16-byte LLP64 layout.</summary>
public class PcapAbiTests
{
    [Fact]
    public void PcapPkthdr_Is16BytesWithCorrectOffsets()
    {
        Assert.Equal(16, Marshal.SizeOf<PcapPkthdr>());
        Assert.Equal(0, Marshal.OffsetOf<PcapPkthdr>(nameof(PcapPkthdr.TvSec)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<PcapPkthdr>(nameof(PcapPkthdr.TvUsec)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<PcapPkthdr>(nameof(PcapPkthdr.CapLen)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<PcapPkthdr>(nameof(PcapPkthdr.Len)).ToInt32());
    }

    [Fact]
    public void SelfTest_ValidateStructs_Passes()
    {
        var ex = Record.Exception(PcapAbiSelfTest.ValidateStructs);
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(14u, 14u, true)]
    [InlineData(60u, 60u, true)]
    [InlineData(1514u, 1514u, true)]
    [InlineData(65535u, 65535u, true)]
    [InlineData(13u, 13u, false)]      // shorter than an Ethernet header
    [InlineData(65536u, 65536u, false)]// beyond snaplen
    [InlineData(100u, 60u, false)]     // wire < captured is impossible
    public void FrameHeader_Sanity(uint capLen, uint wireLen, bool ok)
    {
        Assert.Equal(ok, PcapAbiSelfTest.ValidateFrameHeader(capLen, wireLen, out _));
    }
}
