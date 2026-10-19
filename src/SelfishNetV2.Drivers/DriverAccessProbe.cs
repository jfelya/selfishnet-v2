namespace SelfishNetV2.Drivers;

public enum DriverAccess
{
    /// <summary>The driver opened a capture handle without elevation.</summary>
    Allowed,
    /// <summary>The driver reported an access/permission error — elevation is required.</summary>
    Denied,
    /// <summary>No device could be opened, but not because of permissions (e.g. no devices).</summary>
    Unknown,
}

/// <summary>
/// Checks whether the packet driver is usable without administrator rights.
/// Npcap installs with an option that restricts driver access to administrators
/// (the common default); when that option is cleared, standard users can capture
/// and inject. This probe opens a real handle briefly, so the app can run
/// unelevated whenever the environment allows it and ask for elevation only
/// when it is genuinely required.
/// </summary>
public static class DriverAccessProbe
{
    public static DriverAccess Probe(IPcapDriver driver)
    {
        IReadOnlyList<PcapDeviceInfo> devices;
        try { devices = driver.EnumerateDevices(); }
        catch { return DriverAccess.Unknown; }
        if (devices.Count == 0) return DriverAccess.Unknown;

        bool accessError = false;
        foreach (var dev in devices.Take(3))
        {
            try
            {
                using var capture = driver.OpenLive(dev.Name, filter: null, timeoutMs: 1);
                return DriverAccess.Allowed; // opened fine → no elevation needed
            }
            catch (PcapException ex)
            {
                if (LooksLikeAccessDenied(ex.Message)) accessError = true;
            }
            catch { /* try the next device */ }
        }
        return accessError ? DriverAccess.Denied : DriverAccess.Unknown;
    }

    /// <summary>True when a pcap error text indicates a permission problem.</summary>
    public static bool LooksLikeAccessDenied(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        string m = message.ToLowerInvariant();
        return m.Contains("access") || m.Contains("denied") || m.Contains("permission") ||
               m.Contains("administrator") || m.Contains("privilege");
    }
}
