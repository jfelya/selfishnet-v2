using System.Collections.Concurrent;

namespace SelfishNetV2.Core;

/// <summary>
/// Thread-safe device catalog owned by the application core — it SURVIVES engine
/// switches (engines come and go; devices, caps and names stay). O(1) lookups by
/// IPv4 key and by MAC key (dual index). Events are raised on capture threads;
/// subscribers must marshal to the UI thread.
/// </summary>
public sealed class DeviceCatalog
{
    private readonly ConcurrentDictionary<uint, Device> _byIp = new();
    private readonly ConcurrentDictionary<ulong, uint> _ipByMac = new();

    public event Action<Device>? DeviceAdded;
    public event Action<Device>? DeviceRemoved;
    /// <summary>Raised when an existing record mutates (MAC learned/changed, flags set).</summary>
    public event Action<Device>? DeviceUpdated;

    public int Count => _byIp.Count;

    public IEnumerable<Device> Devices => _byIp.Values;

    public Device AddOrUpdate(uint ipKey, ulong macKey, bool isGateway = false, bool isLocal = false)
    {
        bool isNew = !_byIp.ContainsKey(ipKey);
        var dev = _byIp.AddOrUpdate(ipKey,
            _ => new Device(ipKey, macKey) { IsGateway = isGateway, IsLocalPc = isLocal },
            (_, existing) =>
            {
                ulong oldMac = existing.MacKey;
                if (macKey != 0 && macKey != oldMac)
                {
                    existing.MacKey = macKey;
                    _ipByMac.TryRemove(oldMac, out _);
                    _ipByMac[macKey] = ipKey;
                }
                if (isGateway) existing.IsGateway = true;
                if (isLocal) existing.IsLocalPc = true;
                existing.Touch();
                return existing;
            });
        if (macKey != 0) _ipByMac[macKey] = ipKey;
        if (isNew) DeviceAdded?.Invoke(dev);
        else DeviceUpdated?.Invoke(dev);
        return dev;
    }

    public bool TryGetByIp(uint ipKey, out Device device) => _byIp.TryGetValue(ipKey, out device!);

    public bool TryGetByMac(ulong macKey, out Device device)
    {
        device = null!;
        return _ipByMac.TryGetValue(macKey, out uint ip) && _byIp.TryGetValue(ip, out device!);
    }

    public bool Remove(uint ipKey)
    {
        if (!_byIp.TryRemove(ipKey, out var dev)) return false;
        _ipByMac.TryRemove(dev.MacKey, out _);
        DeviceRemoved?.Invoke(dev);
        return true;
    }

    public void Clear()
    {
        var removed = _byIp.Values.ToArray();
        _byIp.Clear();
        _ipByMac.Clear();
        foreach (var dev in removed) DeviceRemoved?.Invoke(dev);
    }

    /// <summary>Raises <see cref="DeviceUpdated"/> for external mutations the catalog
    /// cannot see (e.g. a background hostname resolution completed).</summary>
    public void NotifyUpdated(Device dev)
    {
        DeviceUpdated?.Invoke(dev);
    }

    /// <summary>Sorted, stable snapshot for UI binding (allocation on the UI thread, never in hot paths).</summary>
    public List<Device> Snapshot() => _byIp.Values.OrderBy(d => d.IpKey).ToList();

    /// <summary>All currently spoofable targets: real devices, known MAC, not us, not the gateway.</summary>
    public List<Device> SpoofTargets() =>
        _byIp.Values.Where(d => d.Targeted && !d.IsLocalPc && !d.IsGateway && d.HasKnownMac).ToList();
}
