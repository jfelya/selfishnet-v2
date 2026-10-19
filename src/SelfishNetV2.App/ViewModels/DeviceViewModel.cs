using SelfishNetV2.Core;
using SelfishNetV2.Services;

namespace SelfishNetV2.App.ViewModels;

/// <summary>
/// View-model wrapper over a catalog <see cref="Device"/>. Decoration (router/local
/// tags) is a VIEW concern computed at render time — the model never stores decorated
/// text (blueprint M6). Speeds/caps are formatted in the current unit mode.
/// </summary>
public sealed class DeviceViewModel : ObservableObject
{
    private readonly Device _dev;
    private readonly MainViewModel _host;
    private string _downText = "";
    private string _upText = "";
    private string _capDownText = "0";
    private string _capUpText = "0";

    public DeviceViewModel(Device dev, MainViewModel host)
    {
        _dev = dev;
        _host = host;
        RefreshSpeeds();
        RefreshCaps();
    }

    public Device Model => _dev;
    public uint IpKey => _dev.IpKey;

    public string IpText => NetKeys.FormatIp(_dev.IpKey);
    public string MacText => _dev.MacText;
    public bool IsGateway => _dev.IsGateway;
    public bool IsLocalPc => _dev.IsLocalPc;
    public bool IsSpecial => _dev.IsGateway || _dev.IsLocalPc;

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

    /// <summary>Cap shown in the current unit as plain text; "0" = unlimited.</summary>
    public string CapDownText
    {
        get => _capDownText;
        set
        {
            if (!decimal.TryParse(value, out decimal d) || d < 0) { RefreshCaps(); OnPropertyChanged(); return; }
            _host.SetCaps(_dev, SpeedFormatter.FromCapDisplay(d, _host.Units), null);
            RefreshCaps();
            OnPropertyChanged();
        }
    }

    public string CapUpText
    {
        get => _capUpText;
        set
        {
            if (!decimal.TryParse(value, out decimal d) || d < 0) { RefreshCaps(); OnPropertyChanged(); return; }
            _host.SetCaps(_dev, null, SpeedFormatter.FromCapDisplay(d, _host.Units));
            RefreshCaps();
            OnPropertyChanged();
        }
    }

    public bool Blocked
    {
        get => !_dev.Redirect;
        set { _host.SetBlocked(_dev, value); OnPropertyChanged(); }
    }

    public bool Targeted
    {
        get => _dev.Targeted;
        set { _host.SetTargeted(_dev, value); OnPropertyChanged(); }
    }

    public void RefreshSpeeds()
    {
        DownText = SpeedFormatter.FormatBytes(_dev.LastDownSample, _host.Units);
        UpText = SpeedFormatter.FormatBytes(_dev.LastUpSample, _host.Units);
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
