// Persistent per-device memory, keyed by MAC (stable across DHCP IP changes).
// Stores the user's custom name + caps + blocked flag so they come back every session.
// File: %AppData%\SelfishNetV2.FreeModelTest\known_devices.json

using System.Net.NetworkInformation;
using System.Text.Json;

namespace SelfishNetV2.Test.Services;

public sealed class KnownDevices
{
    private readonly Dictionary<string, Entry> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public sealed class Entry
    {
        public string? CustomName { get; set; }
        public int DownCapBytes { get; set; }
        public int UpCapBytes { get; set; }
        public bool Blocked { get; set; }
        public string LastIp { get; set; } = "";
        public DateTime LastSeenUtc { get; set; }
    }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SelfishNetV2.FreeModelTest", "known_devices.json");

    public static KnownDevices Load()
    {
        var store = new KnownDevices();
        try
        {
            if (File.Exists(FilePath))
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath));
                if (map != null)
                    foreach (var kv in map)
                        store._map[Normalize(kv.Key)] = kv.Value;
            }
        }
        catch { /* start empty on corrupt file */ }
        return store;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_map,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
    }

    private static string Normalize(string mac) => mac.Trim().ToUpperInvariant();
    private static string KeyOf(PhysicalAddress mac) => Normalize(mac.ToString());
    private static bool Storable(PhysicalAddress mac) => KeyOf(mac).Trim('0').Length > 0;

    /// <summary>Applies remembered name/caps/block to a freshly seen device. Returns true if known.</summary>
    public bool ApplyTo(Core.Device dev)
    {
        if (!Storable(dev.Mac)) return false;
        lock (_lock)
        {
            if (!_map.TryGetValue(KeyOf(dev.Mac), out var e)) return false;
            if (!string.IsNullOrWhiteSpace(e.CustomName)) dev.CustomName = e.CustomName;
            dev.CapDownBytesPerSec = e.DownCapBytes;
            dev.CapUpBytesPerSec = e.UpCapBytes;
            dev.Redirect = !e.Blocked;
            return true;
        }
    }

    public void SetName(PhysicalAddress mac, string? customName, string ip)
    {
        if (!Storable(mac)) return;
        lock (_lock)
        {
            var e = GetOrCreate(mac);
            e.CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName.Trim();
            e.LastIp = ip; e.LastSeenUtc = DateTime.UtcNow;
            Save();
        }
    }

    public void SetCaps(PhysicalAddress mac, int downBytes, int upBytes, string ip)
    {
        if (!Storable(mac)) return;
        lock (_lock)
        {
            var e = GetOrCreate(mac);
            e.DownCapBytes = downBytes; e.UpCapBytes = upBytes;
            e.LastIp = ip; e.LastSeenUtc = DateTime.UtcNow;
            Save();
        }
    }

    public void SetBlocked(PhysicalAddress mac, bool blocked, string ip)
    {
        if (!Storable(mac)) return;
        lock (_lock)
        {
            var e = GetOrCreate(mac);
            e.Blocked = blocked;
            e.LastIp = ip; e.LastSeenUtc = DateTime.UtcNow;
            Save();
        }
    }

    private Entry GetOrCreate(PhysicalAddress mac)
    {
        string key = KeyOf(mac);
        if (!_map.TryGetValue(key, out var e))
        {
            e = new Entry();
            _map[key] = e;
        }
        return e;
    }
}
