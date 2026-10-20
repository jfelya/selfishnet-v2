using System.Security.Principal;
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
        IReadOnlyList<string> pcapNames = _driver?.EnumerateDevices()
            .Select(d => d.Name).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
        return NetworkAdapterEnumerator.GetUsableAdapters(pcapNames);
    }

    public static bool IsAdministrator()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

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
