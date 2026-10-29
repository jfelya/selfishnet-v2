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
    private readonly string _filePath;

    private KnownDevicesStore(bool inMemory, string filePath)
    {
        _inMemory = inMemory;
        _filePath = filePath;
    }

    /// <summary>Non-persistent store for tests (never touches disk).</summary>
    public static KnownDevicesStore InMemory() => new(inMemory: true, filePath: "");

    /// <summary>Number of successful disk writes; test instrumentation only.</summary>
    internal int SaveCount { get; private set; }

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

    private static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SelfishNetV2", "known_devices.json");

    public static KnownDevicesStore Load(string? filePath = null)
    {
        string path = filePath ?? DefaultFilePath;
        var store = new KnownDevicesStore(inMemory: false, filePath: path);
        // A corrupt main file falls back to the last complete backup. Only when
        // both are unreadable does the store start empty.
        var map = TryReadMap(path) ?? TryReadMap(path + ".bak");
        if (map != null)
            foreach (var kv in map)
                store._map[Normalize(kv.Key)] = kv.Value;
        return store;
    }

    private static Dictionary<string, Entry>? TryReadMap(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    private void Save()
    {
        if (_inMemory) return;
        try
        {
            AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(_map,
                new JsonSerializerOptions { WriteIndented = true }));
            SaveCount++;
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

            // A same-IP match is only safe when the new MAC looks rotated rather
            // than like a different physical device that received the old DHCP lease.
            if (!NetKeys.IsLocallyAdministered(dev.MacKey)) return false;

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
        if (dev.KnownSettingsApplied || dev.UserEdited || string.IsNullOrWhiteSpace(systemName)) return false;
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
        // Spoofing is unconditional now (the per-device column was removed), so the
        // legacy Targeted flag is deliberately not restored.
    }

    public void Update(Device dev) => UpdateMany(new[] { dev });

    /// <summary>Update several devices under one lock and one disk write.</summary>
    public void UpdateMany(IEnumerable<Device> devices)
    {
        lock (_lock)
        {
            bool any = false;
            foreach (var dev in devices)
            {
                if (!Storable(dev.MacKey)) continue;
                var e = GetOrCreate(dev.MacKey);
                e.CustomName = string.IsNullOrWhiteSpace(dev.CustomName) ? null : dev.CustomName.Trim();
                e.DownCapBytes = dev.CapDownBytesPerSec;
                e.UpCapBytes = dev.CapUpBytesPerSec;
                e.Blocked = !dev.Redirect;
                e.Targeted = true; // spoofing is unconditional; normalise legacy files
                e.LastIp = NetKeys.FormatIp(dev.IpKey);
                if (!string.IsNullOrWhiteSpace(dev.Name) &&
                    !string.Equals(dev.Name, NetKeys.FormatIp(dev.IpKey), StringComparison.Ordinal))
                    e.SystemName = dev.Name;
                e.LastSeenUtc = DateTime.UtcNow;
                any = true;
            }
            if (any) Save();
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
