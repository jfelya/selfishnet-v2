using System.Net;

namespace SelfishNetV2.Core;

/// <summary>
/// A LAN device. Identity is numeric (IpKey/MacKey); all mutable state is
/// thread-safe (capture threads write, UI thread reads). Caps are bytes/sec,
/// 0 = unlimited. <see cref="Redirect"/> false = blocked. <see cref="Targeted"/>
/// = spoof checkbox (per-device poisoning, restored from the original).
/// </summary>
public sealed class Device
{
    public uint IpKey { get; }
    private long _macBits; // atomic via Volatile
    private volatile string _name = "";
    private volatile string? _customName;
    private volatile bool _isGateway;
    private volatile bool _isLocalPc;
    private volatile int _capDownBytesPerSec;
    private volatile int _capUpBytesPerSec;
    private volatile bool _redirect = true;
    private volatile bool _targeted = true;

    private int _downWindow, _upWindow;       // current 1-second window (legacy caps + display)
    private long _totalDown, _totalUp;        // lifetime totals
    private long _lastSeenTicks;
    private volatile int _lastDownSample;     // last completed 1-second window (for UI)
    private volatile int _lastUpSample;

    public TokenBucket DownBucket { get; } = new();
    public TokenBucket UpBucket { get; } = new();

    public Device(uint ipKey, ulong macKey)
    {
        IpKey = ipKey;
        _macBits = (long)macKey;
        _lastSeenTicks = DateTime.UtcNow.Ticks;
    }

    public IPAddress Ip => NetKeys.UIntToIp(IpKey);

    public ulong MacKey
    {
        get => (ulong)Volatile.Read(ref _macBits);
        set => Volatile.Write(ref _macBits, (long)value);
    }

    public string MacText => NetKeys.FormatMac(MacKey);

    public string Name { get => _name; set => _name = value; }
    public string? CustomName { get => _customName; set => _customName = value; }
    public bool IsGateway { get => _isGateway; set => _isGateway = value; }
    public bool IsLocalPc { get => _isLocalPc; set => _isLocalPc = value; }
    public int CapDownBytesPerSec { get => _capDownBytesPerSec; set => _capDownBytesPerSec = value; }
    public int CapUpBytesPerSec { get => _capUpBytesPerSec; set => _capUpBytesPerSec = value; }
    public bool Redirect { get => _redirect; set => _redirect = value; }
    public bool Targeted { get => _targeted; set => _targeted = value; }

    /// <summary>True when remembered settings were applied for this session (by MAC,
    /// last IP or hostname). Prevents a later hostname match from overwriting them.</summary>
    public bool KnownSettingsApplied { get; set; }

    /// <summary>True after the user explicitly renames, caps or blocks this device.
    /// A late DNS/hostname match must never overwrite a fresh user edit.</summary>
    public bool UserEdited { get; set; }

    public long TotalDownBytes => Interlocked.Read(ref _totalDown);
    public long TotalUpBytes => Interlocked.Read(ref _totalUp);
    public int DownWindow => Volatile.Read(ref _downWindow);
    public int UpWindow => Volatile.Read(ref _upWindow);
    public int LastDownSample { get => _lastDownSample; set => _lastDownSample = value; }
    public int LastUpSample { get => _lastUpSample; set => _lastUpSample = value; }

    public DateTime LastSeenUtc
    {
        get => new(Interlocked.Read(ref _lastSeenTicks), DateTimeKind.Utc);
        private set => Interlocked.Exchange(ref _lastSeenTicks, value.Ticks);
    }

    public bool HasKnownMac => MacKey != 0;

    public void Touch() => LastSeenUtc = DateTime.UtcNow;

    public void AddDown(int bytes)
    {
        Interlocked.Add(ref _downWindow, bytes);
        Interlocked.Add(ref _totalDown, bytes);
        Touch();
    }

    public void AddUp(int bytes)
    {
        Interlocked.Add(ref _upWindow, bytes);
        Interlocked.Add(ref _totalUp, bytes);
        Touch();
    }

    public int ResetDownWindow() => Interlocked.Exchange(ref _downWindow, 0);
    public int ResetUpWindow() => Interlocked.Exchange(ref _upWindow, 0);
}
