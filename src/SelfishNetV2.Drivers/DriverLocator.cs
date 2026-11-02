namespace SelfishNetV2.Drivers;

/// <summary>Result of probing the system for a usable packet capture driver.</summary>
public sealed record DriverDetection(
    bool Available,
    string BackendName,
    string? LibraryPath,
    string Detail);

/// <summary>
/// Finds and loads the packet capture driver. Preference order:
///   1. Npcap   (C:\Windows\System32\Npcap\wpcap.dll + Packet.dll)
///   2. WinPcap (C:\Windows\System32\wpcap.dll)
/// Never attempts to register/load the obsolete 32-bit npf.sys directly.
/// </summary>
public static class DriverLocator
{
    public const string NpcapDir = @"C:\Windows\System32\Npcap";
    public const string NpcapWpcap = NpcapDir + @"\wpcap.dll";
    public const string WinPcapWpcap = @"C:\Windows\System32\wpcap.dll";

    public static DriverDetection Probe()
    {
        if (File.Exists(NpcapWpcap))
        {
            var d = TryLoad(NpcapWpcap, "Npcap");
            if (d.Available) return d;
        }
        if (File.Exists(WinPcapWpcap))
        {
            var d = TryLoad(WinPcapWpcap, "WinPcap-compat");
            if (d.Available) return d;
        }
        return new DriverDetection(false, "none", null,
            "Neither Npcap nor WinPcap was found. Install Npcap from https://npcap.com and restart the app.");
    }

    /// <summary>Loads the best available driver, or throws <see cref="PcapException"/>.</summary>
    public static IPcapDriver LoadBest()
    {
        var d = Probe();
        if (!d.Available || d.LibraryPath == null)
            throw new PcapException(d.Detail);
        return Load(d.LibraryPath, d.BackendName);
    }

    private static DriverDetection TryLoad(string path, string backend)
    {
        try
        {
            var api = WpcapNative.Load(path);
            var drv = new WpcapDriver(api, backend);
            _ = drv.EnumerateDevices(); // smoke test: must not throw
            return new DriverDetection(true, backend, path, "OK");
        }
        catch (Exception ex)
        {
            return new DriverDetection(false, backend, path, ex.Message);
        }
    }

    private static IPcapDriver Load(string path, string backend) => new WpcapDriver(WpcapNative.Load(path), backend);
}
