using System.Security.Principal;
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

}
