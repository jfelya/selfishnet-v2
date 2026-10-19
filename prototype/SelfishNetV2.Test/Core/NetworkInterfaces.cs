// FREE MODEL TEST PROTOTYPE — adapter enumeration + gateway detection.

using System.Net;
using System.Net.NetworkInformation;

namespace SelfishNetV2.Test.Core;

public sealed record AdapterInfo(
    string Id,
    string Name,
    string Description,
    IPAddress LocalIp,
    IPAddress Mask,
    IPAddress? GatewayIp,
    PhysicalAddress Mac,
    string PcapName);

public static class NetworkInterfaces
{
    public static List<AdapterInfo> GetUsableAdapters()
    {
        var result = new List<AdapterInfo>();
        var pcapNames = NpcapInterop.ListDeviceNames();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var props = nic.GetIPProperties();
            var v4 = props.UnicastAddresses.FirstOrDefault(a =>
                a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (v4?.Address == null || v4.IPv4Mask == null) continue;
            var gw = props.GatewayAddresses.FirstOrDefault(g =>
                g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                !IPAddress.Parse("0.0.0.0").Equals(g.Address))?.Address;

            string pcap = MatchPcapName(nic, pcapNames);
            result.Add(new AdapterInfo(nic.Id, nic.Name, nic.Description,
                v4.Address, v4.IPv4Mask, gw, nic.GetPhysicalAddress(), pcap));
        }
        // Gateway-having adapters first (MsgNoAdapter otherwise).
        result.Sort((a, b) => (b.GatewayIp != null).CompareTo(a.GatewayIp != null));
        return result;
    }

    private static string MatchPcapName(NetworkInterface nic, List<string> pcapNames)
    {
        // Npcap names look like \Device\NPF_{GUID}. Match by GUID in NIC Id.
        foreach (var p in pcapNames)
            if (p.Contains(nic.Id, StringComparison.OrdinalIgnoreCase))
                return p;
        // Fallback: index-based guess is unreliable; return empty and let caller pick by order.
        return pcapNames.FirstOrDefault() ?? "";
    }

    public static IEnumerable<IPAddress> EnumerateSubnet(IPAddress localIp, IPAddress mask)
    {
        byte[] ip = localIp.GetAddressBytes();
        byte[] m = mask.GetAddressBytes();
        uint ipU = ((uint)ip[0] << 24) | ((uint)ip[1] << 16) | ((uint)ip[2] << 8) | ip[3];
        uint mU = ((uint)m[0] << 24) | ((uint)m[1] << 16) | ((uint)m[2] << 8) | m[3];
        uint net = ipU & mU;
        uint broadcast = net | ~mU;
        for (uint h = net + 1; h < broadcast; h++)
        {
            yield return new IPAddress(new byte[] {
                (byte)(h >> 24), (byte)(h >> 16), (byte)(h >> 8), (byte)h });
        }
    }

    public static bool IsForwardingEnabled()
    {
        // Best-effort: read IPEnableRouter registry value.
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
            object? v = key?.GetValue("IPEnableRouter");
            return v is int i && i == 1;
        }
        catch { return false; }
    }
}
