// FREE MODEL TEST PROTOTYPE — PcList equivalent (thread-safe device catalog).

using System.Net;
using System.Net.NetworkInformation;
using System.Collections.Concurrent;

namespace SelfishNetV2.Test.Core;

public sealed class DeviceManager
{
    private readonly ConcurrentDictionary<string, Device> _byIp = new();
    private readonly HashSet<string> _known = new();
    private readonly object _knownLock = new();

    public event Action<Device>? DeviceAdded;
    public event Action<Device>? DeviceRemoved;
    public event Action? Changed;

    public IEnumerable<Device> Devices => _byIp.Values.OrderBy(d => d.Ip.GetAddressBytes(), new IpComparer());

    public int Count => _byIp.Count;

    public Device AddOrUpdateTracked(IPAddress ip, PhysicalAddress mac, bool isGateway, bool isLocal)
    {
        string key = ip.ToString();
        bool isNew;
        lock (_knownLock) isNew = _known.Add(key);
        var dev = _byIp.AddOrUpdate(key,
            _ => new Device(ip, mac) { IsGateway = isGateway, IsLocalPc = isLocal, LastSeenUtc = DateTime.UtcNow },
            (_, existing) => {
                existing.UpdateMac(mac);
                existing.LastSeenUtc = DateTime.UtcNow;
                if (isGateway) existing.IsGateway = true;
                if (isLocal) existing.IsLocalPc = true;
                return existing;
            });
        if (isNew) DeviceAdded?.Invoke(dev);
        Changed?.Invoke();
        return dev;
    }

    public bool TryGetByIp(IPAddress ip, out Device? dev) => _byIp.TryGetValue(ip.ToString(), out dev);

    public Device? GetByMac(PhysicalAddress mac)
    {
        string s = mac.ToString();
        foreach (var d in _byIp.Values)
            if (d.Mac.ToString() == s) return d;
        return null;
    }

    public void Remove(Device dev)
    {
        if (_byIp.TryRemove(dev.Ip.ToString(), out _))
        {
            lock (_knownLock) _known.Remove(dev.Ip.ToString());
            DeviceRemoved?.Invoke(dev);
            Changed?.Invoke();
        }
    }

    public void Clear()
    {
        _byIp.Clear();
        lock (_knownLock) _known.Clear();
        Changed?.Invoke();
    }

    private sealed class IpComparer : IComparer<byte[]>
    {
        public int Compare(byte[]? a, byte[]? b)
        {
            if (a == null || b == null) return 0;
            uint x = ((uint)a[0] << 24) | ((uint)a[1] << 16) | ((uint)a[2] << 8) | a[3];
            uint y = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
            return x.CompareTo(y);
        }
    }
}
