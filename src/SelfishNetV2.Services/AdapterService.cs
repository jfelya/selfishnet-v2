using Microsoft.Win32;
using SelfishNetV2.Core;
using SelfishNetV2.Drivers;

namespace SelfishNetV2.Services;

/// <summary>Adapter enumeration + system checks (admin rights, OS forwarding).</summary>
public sealed class AdapterService
{
    private readonly IPcapDriver? _driver;

    public AdapterService(IPcapDriver? driver) => _driver = driver;

    /// <summary>Usable adapters matched against the driver's device list (gateway-having first).</summary>
    public List<AdapterInfo> GetUsableAdapters()
    {
        IReadOnlyList<string> pcapNames = PcapNames();
        return NetworkAdapterEnumerator.GetUsableAdapters(pcapNames)
            .Where(a => !string.IsNullOrWhiteSpace(a.PcapName))
            .ToList();
    }

    /// <summary>True when a NIC exists but none has a matching pcap device. Used to
    /// show a precise message instead of the generic “no adapter” text.</summary>
    public bool HasAdapterWithoutPcapBinding()
    {
        var all = NetworkAdapterEnumerator.GetUsableAdapters(PcapNames());
        return all.Count > 0 && all.All(a => string.IsNullOrWhiteSpace(a.PcapName));
    }

    private IReadOnlyList<string> PcapNames() =>
        _driver?.EnumerateDevices().Select(d => d.Name).ToList()
        ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>
    /// Read-only status for the About window. Windows' legacy IP forwarding
    /// (IPEnableRouter) can route traffic around this app's userspace shaper on
    /// multi-homed machines. The app no longer warns or blocks on it — it is shown
    /// as information only.
    /// </summary>
    public static bool IsOsForwardingEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
            return key?.GetValue("IPEnableRouter") is int i && i == 1;
        }
        catch { return false; }
    }
}
