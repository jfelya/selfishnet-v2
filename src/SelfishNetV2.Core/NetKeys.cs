using System.Net;
using System.Net.NetworkInformation;

namespace SelfishNetV2.Core;

/// <summary>
/// Numeric identity helpers for the packet hot path. Rule: no string identity
/// (ToString on IP/MAC) and no heap allocation in per-packet code — IPv4 addresses
/// are compared as <see cref="uint"/>, MAC addresses as <see cref="ulong"/>.
/// </summary>
public static class NetKeys
{
    public static uint IpToUInt(IPAddress ip) => IpToUInt(ip.GetAddressBytes());

    public static uint IpToUInt(ReadOnlySpan<byte> b) =>
        ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];

    public static IPAddress UIntToIp(uint v) =>
        new(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });

    public static ulong MacToUInt64(PhysicalAddress mac) => MacToUInt64(mac.GetAddressBytes());

    public static ulong MacToUInt64(ReadOnlySpan<byte> b) =>
        ((ulong)b[0] << 40) | ((ulong)b[1] << 32) | ((ulong)b[2] << 24) |
        ((ulong)b[3] << 16) | ((ulong)b[4] << 8) | b[5];

    public static PhysicalAddress UInt64ToMac(ulong v) =>
        new(new[]
        {
            (byte)(v >> 40), (byte)(v >> 32), (byte)(v >> 24),
            (byte)(v >> 16), (byte)(v >> 8), (byte)v
        });

    /// <summary>Broadcast MAC as a numeric key.</summary>
    public const ulong BroadcastMac = 0xFFFFFFFFFFFF;

    /// <summary>Formats a MAC key as "AA:BB:CC:DD:EE:FF" (display only, never in hot path).</summary>
    public static string FormatMac(ulong v) =>
        $"{(byte)(v >> 40):X2}:{(byte)(v >> 32):X2}:{(byte)(v >> 24):X2}:" +
        $"{(byte)(v >> 16):X2}:{(byte)(v >> 8):X2}:{(byte)v:X2}";

    /// <summary>Parses "AA:BB:CC:DD:EE:FF" or "AA-BB-CC-DD-EE-FF" or "AABBCCDDEEFF".</summary>
    public static ulong ParseMac(string text)
    {
        ReadOnlySpan<char> s = text.AsSpan().Trim();
        Span<byte> b = stackalloc byte[6];
        int bi = 0, i = 0;
        while (i < s.Length && bi < 6)
        {
            if (s[i] is ':' or '-') { i++; continue; }
            int hi = HexVal(s[i]); int lo = i + 1 < s.Length ? HexVal(s[i + 1]) : -1;
            if (hi < 0 || lo < 0) return 0;
            b[bi++] = (byte)((hi << 4) | lo);
            i += 2;
        }
        return bi == 6 ? MacToUInt64(b) : 0;

        static int HexVal(char c) =>
            c is >= '0' and <= '9' ? c - '0' :
            c is >= 'a' and <= 'f' ? c - 'a' + 10 :
            c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
    }

    /// <summary>Formats an IPv4 key as "a.b.c.d" (display only).</summary>
    public static string FormatIp(uint v) => $"{v >> 24}.{(v >> 16) & 0xFF}.{(v >> 8) & 0xFF}.{v & 0xFF}";

    /// <summary>Writes a MAC key into a frame buffer (hot-path MAC rewriting).</summary>
    public static void WriteMac(Span<byte> dst, int offset, ulong mac)
    {
        dst[offset] = (byte)(mac >> 40);
        dst[offset + 1] = (byte)(mac >> 32);
        dst[offset + 2] = (byte)(mac >> 24);
        dst[offset + 3] = (byte)(mac >> 16);
        dst[offset + 4] = (byte)(mac >> 8);
        dst[offset + 5] = (byte)mac;
    }
}
