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
        public string SystemName { get; set; } = "";
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

    /// <summary>
    /// Applies remembered name/caps/block/target to a freshly seen device.
    /// Exact MAC first; if the MAC is unknown (phones rotate their Wi-Fi MAC, or the
    /// NIC was replaced) it inherits the settings of a known device last seen at the
    /// same IP. Returns true when settings were applied.
    /// </summary>
    public bool ApplyTo(Device dev)
    {
        if (!Storable(dev.MacKey)) return false;
        lock (_lock)
        {
            if (_map.TryGetValue(KeyOf(dev.MacKey), out var exact))
            {
                Apply(dev, exact);
                dev.KnownSettingsApplied = true;
                return true;
            }

            string ip = NetKeys.FormatIp(dev.IpKey);
            Entry? best = null;
            bool bestHasSettings = false;
            foreach (var candidate in _map.Values)
            {
                if (string.IsNullOrEmpty(candidate.LastIp) ||
                    !string.Equals(candidate.LastIp, ip, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool hasSettings = candidate.DownCapBytes != 0 || candidate.UpCapBytes != 0 ||
                                   candidate.Blocked || !string.IsNullOrWhiteSpace(candidate.CustomName);
                if (best == null ||
                    (hasSettings && !bestHasSettings) ||
                    (hasSettings == bestHasSettings && candidate.LastSeenUtc > best.LastSeenUtc))
                {
                    best = candidate;
                    bestHasSettings = hasSettings;
                }
            }

            if (best == null) return false;
            Apply(dev, best);
            dev.KnownSettingsApplied = true;
            return true;
        }
    }

    /// <summary>
    /// Second-chance match by resolved hostname: covers a device whose IP *and*
    /// randomized MAC both changed. Safe to call repeatedly; it only acts while the
    /// device has not already inherited settings.
    /// </summary>
    public bool TryApplyByName(Device dev, string systemName)
    {
        if (dev.KnownSettingsApplied || string.IsNullOrWhiteSpace(systemName)) return false;
        lock (_lock)
        {
            foreach (var candidate in _map.Values)
            {
                if (!string.IsNullOrWhiteSpace(candidate.SystemName) &&
                    string.Equals(candidate.SystemName, systemName, StringComparison.OrdinalIgnoreCase))
                {
                    Apply(dev, candidate);
                    dev.KnownSettingsApplied = true;
                    return true;
                }
            }
        }
        return false;
    }

    private static void Apply(Device dev, Entry e)
    {
        if (!string.IsNullOrWhiteSpace(e.CustomName)) dev.CustomName = e.CustomName;
        dev.CapDownBytesPerSec = e.DownCapBytes;
        dev.CapUpBytesPerSec = e.UpCapBytes;
        dev.Redirect = !e.Blocked;
        dev.Targeted = e.Targeted;
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
            if (!string.IsNullOrWhiteSpace(dev.Name) &&
                !string.Equals(dev.Name, NetKeys.FormatIp(dev.IpKey), StringComparison.Ordinal))
                e.SystemName = dev.Name;
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
