using System.Runtime.InteropServices;

namespace SelfishNetV2.Drivers;

/// <summary>
/// Guards against ABI drift at the driver boundary (defect C1 class):
/// compile-time-known struct layout is asserted once at startup, and engines run
/// <see cref="ValidateFrameHeader"/> on the first packet of every session so a
/// mismatched header fails loudly instead of corrupting parses.
/// </summary>
public static class PcapAbiSelfTest
{
    /// <summary>Throws when the managed pcap_pkthdr layout diverges from the native 16-byte struct.</summary>
    public static void ValidateStructs()
    {
        if (Marshal.SizeOf<PcapPkthdr>() != 16)
            throw new PcapException($"pcap_pkthdr ABI mismatch: expected 16 bytes, got {Marshal.SizeOf<PcapPkthdr>()}");
        if (Marshal.OffsetOf<PcapPkthdr>(nameof(PcapPkthdr.CapLen)) != 8 ||
            Marshal.OffsetOf<PcapPkthdr>(nameof(PcapPkthdr.Len)) != 12)
            throw new PcapException("pcap_pkthdr field offsets mismatch (caplen@8, len@12 expected)");
    }

    /// <summary>Sanity-checks a header read from a live capture (first frame of a session).</summary>
    public static bool ValidateFrameHeader(uint capLen, uint wireLen, out string problem)
    {
        problem = "";
        if (capLen < 14) problem = $"caplen {capLen} < ethernet header";
        else if (capLen > 65535) problem = $"caplen {capLen} > snaplen";
        else if (wireLen < capLen) problem = $"wirelen {wireLen} < caplen {capLen}";
        return problem.Length == 0;
    }
}
