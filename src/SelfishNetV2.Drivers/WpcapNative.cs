using System.Runtime.InteropServices;
using System.Text;

namespace SelfishNetV2.Drivers;

/// <summary>
/// THE native pcap_pkthdr layout for Windows builds of WinPcap/Npcap.
/// Native C 'long' is 32 bits on Windows (LLP64), so the struct is 16 bytes:
///   int32 tv_sec | int32 tv_usec | uint32 caplen | uint32 len
/// The free-model prototype declared 64-bit longs and read caplen/len out of bounds
/// (blueprint defect C1). Layout is enforced by PcapAbiSelfTest + unit tests.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PcapPkthdr
{
    public int TvSec;
    public int TvUsec;
    public uint CapLen;
    public uint Len;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BpfProgram
{
    public uint bf_len;
    public IntPtr bf_insns;
}

/// <summary>
/// Dynamically loaded wpcap.dll surface via unmanaged function pointers (no DllImport
/// search-order surprises: we load the exact DLL we detected). Required symbols must
/// all resolve; optional extensions tolerate absence.
/// </summary>
internal static unsafe class WpcapNative
{
    internal sealed class Api
    {
        internal IntPtr Lib;
        internal delegate* unmanaged[Cdecl]<IntPtr*, byte*, int> FindAllDevs;
        internal delegate* unmanaged[Cdecl]<IntPtr, void> FreeAllDevs;
        internal delegate* unmanaged[Cdecl]<byte*, int, int, int, byte*, IntPtr> OpenLive;
        internal delegate* unmanaged[Cdecl]<IntPtr, void> Close;
        internal delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, IntPtr*, int> NextEx;
        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int> SendPacket;
        internal delegate* unmanaged[Cdecl]<IntPtr, BpfProgram*, byte*, int, uint, int> Compile;
        internal delegate* unmanaged[Cdecl]<IntPtr, BpfProgram*, int> SetFilter;
        internal delegate* unmanaged[Cdecl]<BpfProgram*, void> FreeCode;
        internal delegate* unmanaged[Cdecl]<IntPtr, byte*> GetErr;
        internal delegate* unmanaged[Cdecl]<IntPtr, void> BreakLoop;
        // Optional (present in Npcap and WinPcap ≥ 4.0; tolerated when missing):
        internal delegate* unmanaged[Cdecl]<IntPtr, int, int> SetMinToCopy;
    }

    private static readonly (string Name, bool Required)[] Symbols =
    {
        ("pcap_findalldevs", true), ("pcap_freealldevs", true), ("pcap_open_live", true),
        ("pcap_close", true), ("pcap_next_ex", true), ("pcap_sendpacket", true),
        ("pcap_compile", true), ("pcap_setfilter", true), ("pcap_freecode", true),
        ("pcap_geterr", true), ("pcap_breakloop", true),
        ("pcap_setmintocopy", false),
    };

    /// <summary>Loads wpcap.dll from an exact path. Throws <see cref="PcapException"/> when
    /// the library or any required symbol is missing. Caller should FreeLibrary on failure.</summary>
    internal static Api Load(string fullPath)
    {
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            SetDllDirectory(dir); // so Packet.dll (dependency) resolves next to wpcap.dll

        IntPtr lib = LoadLibrary(fullPath);
        if (lib == IntPtr.Zero)
            throw new PcapException($"LoadLibrary failed for {fullPath} (Win32 error {Marshal.GetLastWin32Error()})");

        var api = new Api { Lib = lib };
        var missing = new List<string>();
        foreach (var (name, required) in Symbols)
        {
            IntPtr proc = GetProcAddress(lib, name);
            if (proc == IntPtr.Zero)
            {
                if (required) missing.Add(name);
                continue;
            }
            switch (name)
            {
                case "pcap_findalldevs": api.FindAllDevs = (delegate* unmanaged[Cdecl]<IntPtr*, byte*, int>)proc; break;
                case "pcap_freealldevs": api.FreeAllDevs = (delegate* unmanaged[Cdecl]<IntPtr, void>)proc; break;
                case "pcap_open_live": api.OpenLive = (delegate* unmanaged[Cdecl]<byte*, int, int, int, byte*, IntPtr>)proc; break;
                case "pcap_close": api.Close = (delegate* unmanaged[Cdecl]<IntPtr, void>)proc; break;
                case "pcap_next_ex": api.NextEx = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, IntPtr*, int>)proc; break;
                case "pcap_sendpacket": api.SendPacket = (delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int>)proc; break;
                case "pcap_compile": api.Compile = (delegate* unmanaged[Cdecl]<IntPtr, BpfProgram*, byte*, int, uint, int>)proc; break;
                case "pcap_setfilter": api.SetFilter = (delegate* unmanaged[Cdecl]<IntPtr, BpfProgram*, int>)proc; break;
                case "pcap_freecode": api.FreeCode = (delegate* unmanaged[Cdecl]<BpfProgram*, void>)proc; break;
                case "pcap_geterr": api.GetErr = (delegate* unmanaged[Cdecl]<IntPtr, byte*>)proc; break;
                case "pcap_breakloop": api.BreakLoop = (delegate* unmanaged[Cdecl]<IntPtr, void>)proc; break;
                case "pcap_setmintocopy": api.SetMinToCopy = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)proc; break;
            }
        }
        if (missing.Count > 0)
        {
            FreeLibrary(lib);
            throw new PcapException($"{fullPath} is missing required exports: {string.Join(", ", missing)}");
        }
        return api;
    }

    internal static byte[] ToAnsi(string s) => Encoding.ASCII.GetBytes(s + '\0');

    internal static string PtrToStringSafe(byte* p)
    {
        if (p == null) return "";
        int len = 0;
        while (p[len] != 0) len++;
        return len == 0 ? "" : Encoding.ASCII.GetString(p, len);
    }

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string? lpPathName);
}
