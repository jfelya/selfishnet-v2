// FREE MODEL TEST PROTOTYPE — Npcap/WinPcap dynamic interop (no NuGet).
// Prefers Npcap (C:\Windows\System32\Npcap\wpcap.dll), falls back to System32\wpcap.dll.
// Never touches legacy 32-bit npf.sys registration.

using System.Runtime.InteropServices;
using System.Text;

namespace SelfishNetV2.Test.Core;

public static class DriverCheck
{
    public static string? ActiveDllPath { get; private set; }
    public static bool IsAvailable { get; private set; }
    public static string BackendName { get; private set; } = "none";

    static DriverCheck() => Refresh();

    public static void Refresh()
    {
        string npcap = @"C:\Windows\System32\Npcap\wpcap.dll";
        string winpcap = @"C:\Windows\System32\wpcap.dll";
        if (File.Exists(npcap)) { ActiveDllPath = npcap; IsAvailable = true; BackendName = "Npcap"; return; }
        if (File.Exists(winpcap)) { ActiveDllPath = winpcap; IsAvailable = true; BackendName = "WinPcap-compat"; return; }
        ActiveDllPath = null; IsAvailable = false; BackendName = "none";
    }
}

internal static class NativeLoader
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool SetDllDirectory(string? lpPathName);
}

/// <summary>Minimal wpcap.dll P/Invoke surface used by the engine.</summary>
public static class NpcapInterop
{
    static NpcapInterop()
    {
        // Prefer Npcap directory for dependency resolution (Packet.dll lives next to it).
        try
        {
            if (File.Exists(@"C:\Windows\System32\Npcap\wpcap.dll"))
                NativeLoader.SetDllDirectory(@"C:\Windows\System32\Npcap");
            else
                NativeLoader.LoadLibrary(DriverCheck.ActiveDllPath ?? "wpcap.dll");
        }
        catch { /* loader fallback handled by DllImport errors at call time */ }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PcapPkthdr
    {
        public long tv_sec;   // time_t (8 bytes on 64-bit MSVC)
        public long tv_usec;
        public uint caplen;
        public uint len;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BpfProgram
    {
        public uint bf_len;
        public IntPtr bf_insns;
    }

    [DllImport("wpcap.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    private static extern int pcap_findalldevs(ref IntPtr alldevs, StringBuilder errbuf);

    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void pcap_freealldevs(IntPtr alldevs);

    [DllImport("wpcap.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr pcap_open_live(string dev, int snaplen, int promisc, int to_ms, StringBuilder errbuf);

    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void pcap_close(IntPtr p);

    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int pcap_next_ex(IntPtr p, ref IntPtr pkt_header, ref IntPtr pkt_data);

    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int pcap_sendpacket(IntPtr p, byte[] buf, int size);

    [DllImport("wpcap.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    private static extern int pcap_compile(IntPtr p, out BpfProgram fp, string str, int optimize, uint netmask);

    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int pcap_setfilter(IntPtr p, ref BpfProgram fp);

    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void pcap_freecode(ref BpfProgram fp);

    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr pcap_geterr(IntPtr p);

    public static IntPtr OpenLive(string deviceName, string filter, out string error,
        int promisc = 0, int timeoutMs = 20)
    {
        var err = new StringBuilder(256);
        // Original-faithful: snaplen 65535, NON-promiscuous (options=0), tiny timeout.
        IntPtr handle = pcap_open_live(deviceName, 65535, promisc, timeoutMs, err);
        if (handle == IntPtr.Zero)
        {
            error = err.ToString();
            return IntPtr.Zero;
        }
        if (!string.IsNullOrWhiteSpace(filter))
        {
            if (pcap_compile(handle, out var prog, filter, 1, 0xFFFFFF00) != 0 ||
                pcap_setfilter(handle, ref prog) != 0)
            {
                error = Marshal.PtrToStringAnsi(pcap_geterr(handle)) ?? "pcap_setfilter failed";
                pcap_freecode(ref prog);
                pcap_close(handle);
                return IntPtr.Zero;
            }
            pcap_freecode(ref prog);
        }
        error = "";
        return handle;
    }

    public static List<string> ListDeviceNames()
    {
        var names = new List<string>();
        IntPtr all = IntPtr.Zero;
        var err = new StringBuilder(256);
        try
        {
            if (pcap_findalldevs(ref all, err) != 0 || all == IntPtr.Zero) return names;
            IntPtr cur = all;
            while (cur != IntPtr.Zero)
            {
                // pcap_if: next(0:ptr) + name(8:char*) on 64-bit; description at offset 16.
                IntPtr next = Marshal.ReadIntPtr(cur, 0);
                IntPtr namePtr = Marshal.ReadIntPtr(cur, IntPtr.Size);
                string? name = Marshal.PtrToStringAnsi(namePtr);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
                cur = next;
            }
        }
        catch { /* listing is best-effort */ }
        finally { if (all != IntPtr.Zero) try { pcap_freealldevs(all); } catch { } }
        return names;
    }
}
