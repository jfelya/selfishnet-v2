using System.Collections.Concurrent;

namespace SelfishNetV2.Drivers;

/// <summary>
/// In-memory packet driver for tests/CI: injected frames are delivered to every open
/// capture (filtered by each capture's own BPF-ish filter), and every sent frame is
/// recorded in <see cref="SentFrames"/>. Lets the full engine stack run without hardware.
/// Filters understood: "arp", "ip", "ip or (vlan and ip)", null/empty (all frames).
/// </summary>
public sealed class LoopbackDriver : IPcapDriver
{
    private readonly ConcurrentBag<LoopbackCapture> _captures = new();
    private readonly ConcurrentQueue<byte[]> _sent = new();

    public string BackendName => "Loopback";

    /// <summary>Every frame ever passed to Send, in order (thread-safe snapshot on read).</summary>
    public IReadOnlyList<byte[]> SentFrames => _sent.ToArray();

    public IReadOnlyList<PcapDeviceInfo> EnumerateDevices() =>
        new List<PcapDeviceInfo> { new("loopback0", "Loopback test adapter") };

    /// <summary>Injects a frame as if it arrived from the wire (delivered to all open captures).</summary>
    public void Inject(ReadOnlySpan<byte> frame)
    {
        var copy = frame.ToArray();
        foreach (var c in _captures)
            c.Deliver(copy);
    }

    public IPcapCapture OpenLive(string deviceName, string? filter, int snaplen = 65535, int promisc = 0, int timeoutMs = 20)
    {
        var cap = new LoopbackCapture(this, deviceName, filter, Math.Max(1, timeoutMs));
        _captures.Add(cap);
        return cap;
    }

    private sealed class LoopbackCapture : IPcapCapture
    {
        private readonly LoopbackDriver _owner;
        private readonly Func<byte[], bool> _filter;
        private readonly Queue<byte[]> _inbox = new();
        private readonly object _gate = new();
        private readonly int _timeoutMs;
        private volatile bool _broken;
        private bool _disposed;

        internal LoopbackCapture(LoopbackDriver owner, string deviceName, string? filter, int timeoutMs)
        {
            _owner = owner;
            DeviceName = deviceName;
            _timeoutMs = timeoutMs;
            _filter = BuildFilter(filter);
        }

        public string DeviceName { get; }
        public int DataLink => 1; // Ethernet
        public string LastError { get; private set; } = "";

        private static Func<byte[], bool> BuildFilter(string? filter) => filter?.Trim() switch
        {
            null or "" => _ => true,
            "arp" => f => EtherType(f) == 0x0806,
            "ip" => f => EtherType(f) == 0x0800,
            "ip or (vlan and ip)" => f => IsIpv4MaybeVlan(f),
            var other => throw new PcapException($"LoopbackDriver does not implement filter '{other}'"),
        };

        private static ushort EtherType(byte[] f) =>
            f.Length < 14 ? (ushort)0 : (ushort)((f[12] << 8) | f[13]);

        private static bool IsIpv4MaybeVlan(byte[] f)
        {
            int off = 12;
            while (f.Length >= off + 2)
            {
                ushort et = (ushort)((f[off] << 8) | f[off + 1]);
                if (et == 0x0800) return true;
                if (et is 0x8100 or 0x88A8 or 0x9100) { off += 4; continue; }
                return false;
            }
            return false;
        }

        internal void Deliver(byte[] frame)
        {
            if (_disposed || !_filter(frame)) return;
            lock (_gate)
            {
                _inbox.Enqueue(frame);
                Monitor.PulseAll(_gate);
            }
        }

        public int Send(ReadOnlySpan<byte> frame)
        {
            if (_disposed) return -1;
            _owner._sent.Enqueue(frame.ToArray());
            return 0;
        }

        public ReceiveResult Receive(out PacketView view)
        {
            view = default;
            if (_disposed) return ReceiveResult.Broken;
            lock (_gate)
            {
                while (_inbox.Count == 0 && !_broken && !_disposed)
                {
                    if (!Monitor.Wait(_gate, _timeoutMs))
                        return ReceiveResult.Timeout;
                }
                if (_broken || _disposed) return ReceiveResult.Broken;
                byte[] frame = _inbox.Dequeue();
                view = new PacketView(frame, frame.Length);
                return ReceiveResult.Packet;
            }
        }

        public void BreakLoop()
        {
            _broken = true;
            lock (_gate) Monitor.PulseAll(_gate);
        }

        public void TrySetMinToCopy(int bytes) { /* no-op */ }

        public void Dispose()
        {
            _disposed = true;
            lock (_gate) Monitor.PulseAll(_gate);
        }
    }
}
