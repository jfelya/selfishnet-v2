using System.Reflection;
using System.Runtime.Versioning;
using SelfishNetV2.Drivers;
using Xunit;

namespace SelfishNetV2.Tests;

/// <summary>
/// Guards the Windows compatibility contract.
///
/// The build declares a minimum supported OS of Windows 10 1607 (build 14393) —
/// the floor imposed by the .NET 8 runtime — via Directory.Build.targets. That
/// value is emitted as an assembly attribute and makes the platform analyzer
/// (CA1416) reject any API that would not exist on that floor. Every Windows 10
/// release from 1607 through 22H2 and every Windows 11 release is covered.
/// </summary>
public class CompatibilityTests
{
    [Fact]
    public void Build_Declares_Windows10_1607_As_MinimumSupportedOS()
    {
        var platforms = typeof(DriverLocator).Assembly
            .GetCustomAttributes<SupportedOSPlatformAttribute>()
            .Select(a => a.PlatformName)
            .ToList();

        // The attribute carries the full version, e.g. "Windows10.0.14393.0".
        Assert.Contains(platforms, p =>
            p.StartsWith("windows10.0.14393", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PcapHeader_Layout_Is_ArchitectureIndependent()
    {
        // The native pcap_pkthdr (32-bit C longs, LLP64) is 16 bytes on both x64
        // and x86 → the packet parsing code is identical on 32-bit Windows.
        Assert.Equal(16, System.Runtime.InteropServices.Marshal.SizeOf<PcapPkthdr>());
    }
}
