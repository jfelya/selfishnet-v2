using System.Text.Json;
using SelfishNetV2.Core;

namespace SelfishNetV2.Services;

/// <summary>
/// Persistent per-device memory keyed by MAC (stable across DHCP IP changes):
/// custom name, caps, blocked flag and spoof-target flag.
/// File: %AppData%\SelfishNetV2\known_devices.json
/// </summary>
public sealed class KnownDevicesStore
{
    private readonly Dictionary<string, Entry> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly bool _inMemory;

    private KnownDevicesStore(bool inMemory) => _inMemory = inMemory;

    /// <summary>Non-persistent store for tests (never touches disk).</summary>
    public static KnownDevicesStore InMemory() => new(inMemory: true);

    public sealed class Entry
    {
        public string? CustomName { get; set; }
        public int DownCapBytes { get; set; }
        public int UpCapBytes { get; set; }
        public bool Blocked { get; set; }
        public bool Targeted { get; set; } = true;
        public string LastIp { get; set; } = "";
        public DateTime LastSeenUtc { get; set; }
    }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SelfishNetV2", "known_devices.json");

    public static KnownDevicesStore Load()
    {
        var store = new KnownDevicesStore(inMemory: false);
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
        if (_inMemory) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_map,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
    }

    private static string Normalize(string mac) => mac.Trim().ToUpperInvariant();
    private static string KeyOf(ulong macKey) => Normalize(NetKeys.FormatMac(macKey));
    private static bool Storable(ulong macKey) => macKey != 0;

    /// <summary>Applies remembered name/caps/block/target to a freshly seen device. Returns true if known.</summary>
    public bool ApplyTo(Device dev)
    {
        if (!Storable(dev.MacKey)) return false;
        lock (_lock)
        {
            if (!_map.TryGetValue(KeyOf(dev.MacKey), out var e)) return false;
            if (!string.IsNullOrWhiteSpace(e.CustomName)) dev.CustomName = e.CustomName;
            dev.CapDownBytesPerSec = e.DownCapBytes;
            dev.CapUpBytesPerSec = e.UpCapBytes;
            dev.Redirect = !e.Blocked;
            dev.Targeted = e.Targeted;
            return true;
        }
    }

    public void Update(Device dev)
    {
        if (!Storable(dev.MacKey)) return;
        lock (_lock)
        {
            var e = GetOrCreate(dev.MacKey);
            e.CustomName = string.IsNullOrWhiteSpace(dev.CustomName) ? null : dev.CustomName.Trim();
            e.DownCapBytes = dev.CapDownBytesPerSec;
            e.UpCapBytes = dev.CapUpBytesPerSec;
            e.Blocked = !dev.Redirect;
            e.Targeted = dev.Targeted;
            e.LastIp = NetKeys.FormatIp(dev.IpKey);
            e.LastSeenUtc = DateTime.UtcNow;
            Save();
        }
    }

    private Entry GetOrCreate(ulong macKey)
    {
        string key = KeyOf(macKey);
        if (!_map.TryGetValue(key, out var e))
        {
            e = new Entry();
            _map[key] = e;
        }
        return e;
    }
}
