using System.Net;
using System.Net.NetworkInformation;

namespace SelfishNetV2.Core;

/// <summary>A usable network adapter: managed NIC info + matched pcap device name.</summary>
public sealed record AdapterInfo(
    string Id,
    string Name,
    string Description,
    uint LocalIpKey,
    uint MaskKey,
    uint GatewayIpKey,      // 0 = no gateway
    ulong MacKey,
    string PcapName)        // "" when no matching pcap device was found
{
    public IPAddress LocalIp => NetKeys.UIntToIp(LocalIpKey);
    public IPAddress Mask => NetKeys.UIntToIp(MaskKey);
    public IPAddress? GatewayIp => GatewayIpKey == 0 ? null : NetKeys.UIntToIp(GatewayIpKey);
    public PhysicalAddress Mac => NetKeys.UInt64ToMac(MacKey);
    public string MacText => NetKeys.FormatMac(MacKey);
}

public static class NetworkAdapterEnumerator
{
    /// <summary>All operational, non-loopback adapters that have an IPv4 address.
    /// Adapters with a gateway sort first.</summary>
    public static List<AdapterInfo> GetUsableAdapters(IReadOnlyList<string> pcapDeviceNames)
    {
        var result = new List<AdapterInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            IPInterfaceProperties props;
            try { props = nic.GetIPProperties(); } catch { continue; }

            var v4 = props.UnicastAddresses.FirstOrDefault(a =>
                a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (v4?.Address == null || v4.IPv4Mask == null) continue;
            var gw = props.GatewayAddresses.FirstOrDefault(g =>
                g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                !g.Address.Equals(IPAddress.Any))?.Address;

            string pcap = MatchPcapName(nic.Id, pcapDeviceNames);
            result.Add(new AdapterInfo(
                nic.Id, nic.Name, nic.Description,
                NetKeys.IpToUInt(v4.Address), NetKeys.IpToUInt(v4.IPv4Mask),
                gw == null ? 0u : NetKeys.IpToUInt(gw),
                NetKeys.MacToUInt64(nic.GetPhysicalAddress()), pcap));
        }
        result.Sort((a, b) => (b.GatewayIpKey != 0).CompareTo(a.GatewayIpKey != 0));
        return result;
    }

    /// <summary>Npcap device names look like \Device\NPF_{GUID}; match by NIC GUID.
    /// No match returns empty: opening the first pcap device could shape the wrong LAN.</summary>
    internal static string MatchPcapName(string nicId, IReadOnlyList<string> pcapNames)
    {
        foreach (string p in pcapNames)
            if (p.Contains(nicId, StringComparison.OrdinalIgnoreCase)) return p;
        return "";
    }

    /// <summary>Every host address in the adapter's subnet (network+1 .. broadcast-1).</summary>
    public static IEnumerable<uint> EnumerateSubnet(uint localIpKey, uint maskKey)
    {
        uint net = localIpKey & maskKey;
        uint broadcast = net | ~maskKey;
        for (uint h = net + 1; h < broadcast; h++)
            yield return h;
    }
}
