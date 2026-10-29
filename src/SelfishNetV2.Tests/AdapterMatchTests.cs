using SelfishNetV2.Core;
using Xunit;

namespace SelfishNetV2.Tests;

public class AdapterMatchTests
{
    [Fact]
    public void MatchPcapName_NoGuidMatch_ReturnsEmpty_NotTheFirstDevice()
    {
        var pcapNames = new[] { @"\Device\NPF_{11111111-1111-1111-1111-111111111111}" };
        Assert.Equal("", NetworkAdapterEnumerator.MatchPcapName(
            "{22222222-2222-2222-2222-222222222222}", pcapNames));
    }

    [Fact]
    public void MatchPcapName_MatchingGuid_ReturnsThatDevice()
    {
        var pcapNames = new[] { @"\Device\NPF_{11111111-1111-1111-1111-111111111111}" };
        Assert.Equal(pcapNames[0], NetworkAdapterEnumerator.MatchPcapName(
            "{11111111-1111-1111-1111-111111111111}", pcapNames));
    }
}
