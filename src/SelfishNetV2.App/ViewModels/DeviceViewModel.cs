using System.Diagnostics;
using SelfishNetV2.Core;
using SelfishNetV2.Services;

namespace SelfishNetV2.App.ViewModels;

/// <summary>
/// View-model wrapper over a catalog <see cref="Device"/>. Decoration (router/local
/// tags) is a VIEW concern computed at render time — the model never stores decorated
/// text (blueprint M6). Speeds/caps are formatted in the current unit mode.
///
/// Live speeds are computed from the device's lifetime byte counters on every UI tick
/// (4 Hz), so the grid updates faster than the engine's 1 s sampling window without
/// disturbing the legacy cap windows.
/// </summary>
public sealed class DeviceViewModel : ObservableObject
{
    private readonly Device _dev;
    private readonly MainViewModel _host;
    private string _downText = "";
    private string _upText = "";
    private string _capDownText = "0";
    private string _capUpText = "0";

    private long _lastTotalDown;
    private long _lastTotalUp;
    private long _lastTick;
    private double _downBps;
    private double _upBps;

    public DeviceViewModel(Device dev, MainViewModel host)
    {
        _dev = dev;
        _host = host;
        _lastTotalDown = dev.TotalDownBytes;
        _lastTotalUp = dev.TotalUpBytes;
        _lastTick = Stopwatch.GetTimestamp();
    }

    public Device Model => _dev;
    public uint IpKey => _dev.IpKey;

    public string IpText => NetKeys.FormatIp(_dev.IpKey);
    public string MacText => _dev.MacText;
    public bool IsGateway => _dev.IsGateway;
    public bool IsLocalPc => _dev.IsLocalPc;
    public bool IsSpecial => _dev.IsGateway || _dev.IsLocalPc;
    /// <summary>Blocking is meaningless for our own PC and the gateway (never forwarded).</summary>
    public bool CanEditFlags => !IsSpecial;

    public double DownBps => _downBps;
    public double UpBps => _upBps;

    public string DisplayName
    {
        get
        {
            string name = !string.IsNullOrWhiteSpace(_dev.CustomName) ? _dev.CustomName! : _dev.Name;
            if (string.IsNullOrWhiteSpace(name)) name = IpText;
            return name;
        }
        set
        {
            string cleaned = (value ?? "").Trim();
            string current = _dev.CustomName ?? "";
            if (cleaned == current) return;
            if (cleaned == DisplayName) return; // unchanged after decoration strip
            _host.RenameDevice(_dev, cleaned.Length == 0 ? null : cleaned);
            OnPropertyChanged();
        }
    }

    /// <summary>Role tag rendered by the view as a badge: "(Router)" / "(Local)" — never stored.</summary>
    public string RoleTag => _dev.IsLocalPc
        ? $"[{LocalizationManager.Get("LocalHost")}]"
        : _dev.IsGateway ? $"[{LocalizationManager.Get("Gateway")}]" : "";

    public string DownText { get => _downText; private set => Set(ref _downText, value); }
    public string UpText { get => _upText; private set => Set(ref _upText, value); }

    /// <summary>Cap shown in the current unit as plain text; "0" = unlimited.
    /// Empty or non-numeric input commits as 0 (the UI only allows digits).</summary>
    public string CapDownText
    {
        get => _capDownText;
        set
        {
            int bytes = ParseCap(value);
            _host.SetCaps(_dev, bytes, null);
            RefreshCaps();
            OnPropertyChanged();
        }
    }

    public string CapUpText
    {
        get => _capUpText;
        set
        {
            int bytes = ParseCap(value);
            _host.SetCaps(_dev, null, bytes);
            RefreshCaps();
            OnPropertyChanged();
        }
    }

    private int ParseCap(string? text)
    {
        string cleaned = (text ?? "").Trim();
        if (cleaned.Length == 0) return 0;
        return decimal.TryParse(cleaned, out decimal value) && value > 0
            ? SpeedFormatter.FromCapDisplay(value, _host.Units)
            : 0;
    }

    public bool Blocked
    {
        get => !_dev.Redirect;
        set { _host.SetBlocked(_dev, value); OnPropertyChanged(); }
    }

    /// <summary>Recomputes the instantaneous rate from the lifetime counters.</summary>
    public void UpdateLiveSpeed(long nowTicks)
    {
        double dt = (nowTicks - _lastTick) / (double)Stopwatch.Frequency;
        if (dt <= 0) return;

        long down = _dev.TotalDownBytes;
        long up = _dev.TotalUpBytes;
        _downBps = Math.Max(0, down - _lastTotalDown) / dt;
        _upBps = Math.Max(0, up - _lastTotalUp) / dt;
        _lastTotalDown = down;
        _lastTotalUp = up;
        _lastTick = nowTicks;

        DownText = SpeedFormatter.FormatBytes(_downBps, _host.Units);
        UpText = SpeedFormatter.FormatBytes(_upBps, _host.Units);
    }

    /// <summary>Re-formats the last computed rates (language/unit change).</summary>
    public void RefreshSpeeds()
    {
        DownText = SpeedFormatter.FormatBytes(_downBps, _host.Units);
        UpText = SpeedFormatter.FormatBytes(_upBps, _host.Units);
    }

    public void RefreshCaps()
    {
        _capDownText = SpeedFormatter.ToCapDisplay(_dev.CapDownBytesPerSec, _host.Units).ToString("0");
        _capUpText = SpeedFormatter.ToCapDisplay(_dev.CapUpBytesPerSec, _host.Units).ToString("0");
        OnPropertyChanged(nameof(CapDownText));
        OnPropertyChanged(nameof(CapUpText));
    }

    public void RefreshIdentity()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(MacText));
        OnPropertyChanged(nameof(RoleTag));
    }
}
