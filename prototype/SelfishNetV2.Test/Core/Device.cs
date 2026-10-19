// FREE MODEL TEST PROTOTYPE — PC class (device record, per ARCHITECTURE_ANALYSIS §3.1).

using System.Net;
using System.Net.NetworkInformation;

namespace SelfishNetV2.Test.Core;

public sealed class Device
{
    public IPAddress Ip { get; }
    public PhysicalAddress Mac { get; private set; }
    public string Name { get; set; } = "";
    /// <summary>User-chosen name, persisted by MAC in KnownDevices. Displayed when set.</summary>
    public string? CustomName { get; set; }
    public bool IsGateway { get; set; }
    public bool IsLocalPc { get; set; }

    /// <summary>Bytes/sec caps. 0 = unlimited. UI edits in the current unit; engine stores bytes.</summary>
    public int CapDownBytesPerSec { get; set; }
    public int CapUpBytesPerSec { get; set; }

    /// <summary>redirect==true → forward; false → drop (Blocked checkbox inverts this).</summary>
    public bool Redirect { get; set; } = true;

    // 1-second window counters (reset by UI timer) + lifetime totals.
    public int DownBytesSinceReset;
    public int UpBytesSinceReset;
    public long TotalDownBytes;
    public long TotalUpBytes;

    // Last 1-second window sample (raw bytes) + lifetime totals.
    public int LastDownBytes { get; set; }
    public int LastUpBytes { get; set; }

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    public TokenBucket DownBucket { get; } = new();
    public TokenBucket UpBucket { get; } = new();

    public Device(IPAddress ip, PhysicalAddress mac)
    {
        Ip = ip;
        Mac = mac;
    }

    public void UpdateMac(PhysicalAddress mac) => Mac = mac;

    public void AddDown(int bytes)
    {
        Interlocked.Add(ref DownBytesSinceReset, bytes);
        Interlocked.Add(ref TotalDownBytes, bytes);
        LastSeenUtc = DateTime.UtcNow;
    }

    public void AddUp(int bytes)
    {
        Interlocked.Add(ref UpBytesSinceReset, bytes);
        Interlocked.Add(ref TotalUpBytes, bytes);
        LastSeenUtc = DateTime.UtcNow;
    }

    public int ResetDown() => Interlocked.Exchange(ref DownBytesSinceReset, 0);
    public int ResetUp() => Interlocked.Exchange(ref UpBytesSinceReset, 0);
}
