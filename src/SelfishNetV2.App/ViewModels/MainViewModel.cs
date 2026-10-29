using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using SelfishNetV2.Core;
using SelfishNetV2.Engines;
using SelfishNetV2.Services;
using SelfishNetV2.App.Theming;

namespace SelfishNetV2.App.ViewModels;

/// <summary>
/// Orchestration hub for the shell. All engine events arrive on background threads and
/// are marshaled to the UI dispatcher here — the view layer never thinks about threads.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly EngineOrchestrator? _orch;
    private readonly AdapterService _adapters;
    private readonly SettingsStore _settings;
    private readonly string? _driverProblem;
    private readonly Dispatcher _ui;
    private bool _driverRestricted;
    private readonly Dictionary<uint, DeviceViewModel> _byIp = new();

    private string _statusText = "";
    private string _devicesCountText = "";
    private string _totalTrafficText = "";
    private string _engineBadge = "";
    private string _startStopText = "";
    private bool _isShaping;
    private bool _isBusy;
    private string _busyText = "";
    private bool _isEngineLegacy;
    private bool _isEngineModern = true;
    private DeviceViewModel? _selectedDevice;
    private readonly DispatcherTimer _fastTimer = new();

    public MainViewModel(EngineOrchestrator? orch, AdapterService adapters, SettingsStore settings,
        string? driverProblem, bool driverRestricted = false)
    {
        _orch = orch;
        _adapters = adapters;
        _settings = settings;
        _driverProblem = driverProblem;
        _driverRestricted = driverRestricted;
        _ui = Dispatcher.CurrentDispatcher;

        Units = SpeedFormatter.Parse(_settings.UnitMode);
        Theme = _settings.Theme == "Light" ? AppTheme.Light : AppTheme.Dark;
        LocalizationManager.SetLanguage(_settings.Language == "English"
            ? LocalizationManager.Language.English : LocalizationManager.Language.SpanishLA);

        Devices = new ObservableCollection<DeviceViewModel>();

        DiscoverCommand = new RelayCommand(_ => Discover(), _ => CanUseEngine);
        ToggleShapingCommand = new RelayCommand(async _ => await ToggleShapingAsync(), _ => CanUseEngine);
        PickAdapterCommand = new RelayCommand(_ => PickAdapter(), _ => CanUseEngine);
        ToggleUnitsCommand = new RelayCommand(_ => ToggleUnits());
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        AboutCommand = new RelayCommand(_ => ShowAbout());
        SwitchEngineCommand = new RelayCommand(async p => await SwitchEngineAsync(p as string), _ => CanUseEngine);
        ToggleLanguageCommand = new RelayCommand(_ => ToggleLanguage());
        OpenNpcapDownloadCommand = new RelayCommand(_ => OpenNpcapDownload());
        ResetLimitsCommand = new RelayCommand(_ => ResetLimits(), _ => CanUseEngine);

        // Live grid refresh: 4 Hz from lifetime counters (the engine still samples
        // once per second for its cap windows and charts).
        _fastTimer.Interval = TimeSpan.FromMilliseconds(250);
        _fastTimer.Tick += (_, _) => OnFastTick();
        _fastTimer.Start();

        LocalizationManager.OnLanguageChanged += ApplyLanguage;

        if (_orch != null)
        {
            _isEngineModern = _orch.CurrentKind == EngineKind.Modern;
            _isEngineLegacy = !_isEngineModern;
            _orch.StateChanged += (_, _) => Dispatch(RefreshState);
            _orch.StatsTick += (_, _) => Dispatch(OnStatsTick);
            _orch.DiscoveryCompleted += (_, _) => Dispatch(() => { if (!IsBusy) RefreshState(); });
            _orch.EngineChanged += (_, _) => Dispatch(RefreshState);
            _orch.Error += (_, e) => Dispatch(() => OnEngineError(e));
            _orch.Catalog.DeviceAdded += d => Dispatch(() => AddRow(d));
            _orch.Catalog.DeviceRemoved += d => Dispatch(() => RemoveRow(d));
            _orch.Catalog.DeviceUpdated += d => Dispatch(() => RefreshRow(d));
        }
        ApplyLanguage();
        RefreshState();
    }

    // ---------------- surface state ----------------

    public ObservableCollection<DeviceViewModel> Devices { get; }
    public string DriverProblem => _driverProblem ?? "";
    public UnitMode Units { get; private set; }
    public AppTheme Theme { get; private set; }

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string DevicesCountText { get => _devicesCountText; private set => Set(ref _devicesCountText, value); }
    public string TotalTrafficText { get => _totalTrafficText; private set => Set(ref _totalTrafficText, value); }
    public string EngineBadge { get => _engineBadge; private set => Set(ref _engineBadge, value); }
    public string StartStopText { get => _startStopText; private set => Set(ref _startStopText, value); }
    public bool IsShaping
    {
        get => _isShaping;
        private set
        {
            if (Set(ref _isShaping, value))
                OnPropertyChanged(nameof(ShapingTooltip));
        }
    }
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public bool HasDriverProblem => _orch == null;
    public string ShapingTooltip => IsShaping
        ? LocalizationManager.Get("BtnStopTooltip")
        : LocalizationManager.Get("BtnStartTooltip");
    public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }
    public bool IsEngineLegacy { get => _isEngineLegacy; private set => Set(ref _isEngineLegacy, value); }
    public bool IsEngineModern { get => _isEngineModern; private set => Set(ref _isEngineModern, value); }

    public DeviceViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set => Set(ref _selectedDevice, value);
    }

    private bool CanUseEngine => _orch != null && !IsBusy;

    /// <summary>Raised on the UI thread ~1/s: totalDown, totalUp, selectedDown, selectedUp (bytes/s).</summary>
    public event Action<double, double, double, double>? ChartTick;

    /// <summary>Raised on the UI thread when a modal message must be shown (title = AppTitle).</summary>
    public event Action<string, string, bool>? MessageRequested; // text, title, isError

    /// <summary>Static UI labels changed (language or unit mode) → window re-applies headers etc.</summary>
    public event Action? UiStringsChanged;

    /// <summary>Theme was switched → the shell should repaint custom-drawn controls.</summary>
    public event Action? ThemeChanged;

    /// <summary>The driver denied access mid-session → the shell offers an elevated restart.</summary>
    public event Action? ElevationRequested;

    public RelayCommand DiscoverCommand { get; }
    public RelayCommand ToggleShapingCommand { get; }
    public RelayCommand PickAdapterCommand { get; }
    public RelayCommand ToggleUnitsCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand AboutCommand { get; }
    public RelayCommand SwitchEngineCommand { get; }
    public RelayCommand ToggleLanguageCommand { get; }
    public RelayCommand OpenNpcapDownloadCommand { get; }
    public RelayCommand ResetLimitsCommand { get; }

    // ---------------- device edits (apply + persist via orchestrator) ----------------

    public void RenameDevice(Device dev, string? name) => _orch?.SetCustomName(dev, name);

    public void SetCaps(Device dev, int? downBytes, int? upBytes) =>
        _orch?.SetCaps(dev, downBytes ?? dev.CapDownBytesPerSec, upBytes ?? dev.CapUpBytesPerSec);

    public void SetBlocked(Device dev, bool blocked) => _orch?.SetBlocked(dev, blocked);

    // ---------------- commands ----------------

    private void Discover()
    {
        if (_orch?.Session == null) { PickAdapter(); return; }
        StatusText = LocalizationManager.Get("StatusScanning");
        _orch.Discover();
    }

    private async Task ToggleShapingAsync()
    {
        if (_orch == null) return;
        if (_orch.Session == null) { PickAdapter(); if (_orch.Session == null) return; }
        if (IsShaping)
        {
            await RunBusyAsync(LocalizationManager.Get("StatusStopped"), async () =>
            {
                await _orch.StopAndHealAsync();
            });
            StatusText = LocalizationManager.Get("StatusRestored");
        }
        else
        {
            bool ok = false;
            await RunBusyAsync(LocalizationManager.Get("StatusResolvingGateway"), async () =>
            {
                ok = await _orch.StartShapingAsync();
            });
            if (!ok) RefreshState();
        }
    }

    private void PickAdapter()
    {
        if (_orch == null) return;
        var list = _adapters.GetUsableAdapters();
        if (list.Count == 0)
        {
            string key = _adapters.HasAdapterWithoutPcapBinding() ? "MsgNoPcapAdapter" : "MsgNoAdapter";
            MessageRequested?.Invoke(LocalizationManager.Get(key), LocalizationManager.Get("AppTitle"), true);
            return;
        }
        var win = new Views.AdapterWindow(list, _settings.LastAdapterId)
        {
            Owner = Application.Current.MainWindow
        };
        if (win.ShowDialog() != true || win.Selected == null) return;
        var selected = win.Selected;
        IsBusy = true;
        Task.Run(() =>
        {
            bool ok = _orch.OpenAdapter(selected);
            Dispatch(() =>
            {
                IsBusy = false;
                if (ok)
                {
                    _settings.LastAdapterId = selected.Id;
                    _settings.Save();
                }
                else
                {
                    MessageRequested?.Invoke(LocalizationManager.Get("MsgDriverError", "open failed"),
                        LocalizationManager.Get("AppTitle"), true);
                }
                RefreshState();
            });
        });
    }

    private async Task SwitchEngineAsync(string? kindName)
    {
        if (_orch == null || kindName == null) return;
        var kind = kindName == "Legacy" ? EngineKind.Legacy : EngineKind.Modern;
        if (kind == _orch.CurrentKind) { RefreshEngineToggles(); return; }
        IsBusy = true;
        BusyText = LocalizationManager.Get("StatusSwitching", kind);
        bool ok = await _orch.SwitchEngineAsync(kind);
        _settings.Engine = kind.ToString();
        _settings.Save();
        IsBusy = false;
        RefreshState();
        if (!ok)
            MessageRequested?.Invoke(LocalizationManager.Get("MsgDriverError", "engine switch failed"),
                LocalizationManager.Get("AppTitle"), true);
    }

    private void ToggleUnits()
    {
        Units = Units == UnitMode.Bytes ? UnitMode.Bits : UnitMode.Bytes;
        _settings.UnitMode = Units.ToString();
        _settings.Save();
        ApplyLanguage();          // headers carry the unit name
        foreach (var d in Devices) { d.RefreshSpeeds(); d.RefreshCaps(); }
        OnPropertyChanged(nameof(Units));
    }

    private void ToggleTheme()
    {
        Theme = Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        _settings.Theme = Theme.ToString();
        _settings.Save();
        ThemeManager.Apply(Theme);
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(ThemeButtonText));
        ThemeChanged?.Invoke();
    }

    private void ToggleLanguage()
    {
        var l = LocalizationManager.CurrentLanguage == LocalizationManager.Language.English
            ? LocalizationManager.Language.SpanishLA
            : LocalizationManager.Language.English;
        LocalizationManager.SetLanguage(l);   // fires OnLanguageChanged → ApplyLanguage
        _settings.Language = l.ToString();
        _settings.Save();
    }

    /// <summary>Opens the official Npcap download page (the only external prerequisite).</summary>
    private static void OpenNpcapDownload()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://npcap.com/#download") { UseShellExecute = true });
        }
        catch { /* no browser / blocked: the URL is also shown in the banner tooltip */ }
    }

    private void ShowAbout()
    {
        var session = _orch?.Session;
        var adapter = _orch?.CurrentAdapter;

        string gatewayMac = "-";
        if (adapter != null && adapter.GatewayIpKey != 0 &&
            _orch!.Catalog.TryGetByIp(adapter.GatewayIpKey, out var gateway) && gateway.HasKnownMac)
            gatewayMac = gateway.MacText;

        string engineName = _orch == null
            ? "-"
            : LocalizationManager.Get(_orch.CurrentKind == EngineKind.Modern ? "EngineModern" : "EngineLegacy");

        var info = new AboutInfo(
            Version: typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "2.0.0",
            EngineName: engineName,
            DriverBackend: _orch?.DriverBackend ?? "-",
            SystemText: $"{RuntimeInformation.OSDescription.Trim()} ({RuntimeInformation.ProcessArchitecture})",
            RuntimeText: RuntimeInformation.FrameworkDescription.Trim(),
            AdapterName: adapter?.Name ?? "-",
            LocalText: adapter == null ? "-" : $"{NetKeys.FormatIp(adapter.LocalIpKey)}  ({adapter.MacText})",
            GatewayText: adapter == null || adapter.GatewayIpKey == 0 ? "-" : NetKeys.FormatIp(adapter.GatewayIpKey),
            GatewayMac: gatewayMac,
            State: session?.State ?? EngineRunState.Closed,
            Devices: _orch?.Catalog.Count ?? 0,
            Targets: _orch?.Catalog.SpoofTargets().Count ?? 0,
            ForwardedDown: session?.ForwardedDown ?? 0,
            ForwardedUp: session?.ForwardedUp ?? 0,
            Dropped: session?.Dropped ?? 0,
            SendFailures: session?.SpoofSendFailures ?? 0,
            OsForwarding: AdapterService.IsOsForwardingEnabled());

        new Views.AboutWindow(info) { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    // ---------------- engine event handling ----------------

    private void OnEngineError(EngineErrorEventArgs e)
    {
        if (e.Kind == EngineErrorKind.AccessDenied)
        {
            _driverRestricted = true;
            RefreshState();
            ElevationRequested?.Invoke();
            return;
        }
        string text = e.Kind switch
        {
            EngineErrorKind.GatewayMacUnknown => LocalizationManager.Get("MsgGatewayUnknown"),
            EngineErrorKind.NoGateway => LocalizationManager.Get("MsgNoGateway"),
            EngineErrorKind.SendFailed => LocalizationManager.Get("MsgSendFailed", e.Detail),
            EngineErrorKind.AbiMismatch => LocalizationManager.Get("MsgAbiError", e.Detail),
            EngineErrorKind.DriverOpen => LocalizationManager.Get("MsgDriverError", e.Detail),
            EngineErrorKind.ReceiveError => LocalizationManager.Get("MsgDriverError", e.Detail),
            _ => LocalizationManager.Get("MsgDriverError", e.Detail),
        };
        MessageRequested?.Invoke(text, LocalizationManager.Get("AppTitle"), true);
    }

    // ---------------- grid sync ----------------

    private void AddRow(Device dev)
    {
        if (_byIp.ContainsKey(dev.IpKey)) return;
        var vm = new DeviceViewModel(dev, this);
        _byIp[dev.IpKey] = vm;
        // insert sorted by IP (catalog order)
        int idx = Devices.Count(d => d.IpKey < dev.IpKey);
        Devices.Insert(idx, vm);
        RefreshCounts();
    }

    private void RemoveRow(Device dev)
    {
        if (_byIp.Remove(dev.IpKey, out var vm))
        {
            if (SelectedDevice == vm) SelectedDevice = null;
            Devices.Remove(vm);
        }
        RefreshCounts();
    }

    private void RefreshRow(Device dev)
    {
        if (!_byIp.TryGetValue(dev.IpKey, out var vm)) return;
        vm.RefreshIdentity();
        vm.RefreshCaps();
        vm.RefreshFlags();
    }

    private void OnStatsTick()
    {
        double totalDown = 0, totalUp = 0;
        foreach (var dev in _orch!.Catalog.Devices)
        {
            totalDown += dev.LastDownSample;
            totalUp += dev.LastUpSample;
        }
        RefreshCounts();
        double selDown = SelectedDevice?.Model.LastDownSample ?? 0;
        double selUp = SelectedDevice?.Model.LastUpSample ?? 0;
        ChartTick?.Invoke(totalDown, totalUp, selDown, selUp);
    }

    /// <summary>Fast UI sampling (4 Hz): per-device rates from lifetime byte counters.</summary>
    private void OnFastTick()
    {
        if (_orch == null) return;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double totalDown = 0, totalUp = 0;
        foreach (var vm in Devices)
        {
            vm.UpdateLiveSpeed(now);
            totalDown += vm.DownBps;
            totalUp += vm.UpBps;
        }
        TotalTrafficText = LocalizationManager.Get("TotalTraffic",
            SpeedFormatter.FormatBytes(totalDown, Units), SpeedFormatter.FormatBytes(totalUp, Units));
    }

    private void RefreshCounts() =>
        DevicesCountText = LocalizationManager.Get("DevicesCount", Devices.Count);

    private void RefreshState()
    {
        var state = _orch?.State ?? EngineRunState.Closed;
        IsShaping = state == EngineRunState.Shaping;
        StartStopText = IsShaping ? LocalizationManager.Get("BtnStop") : LocalizationManager.Get("BtnStart");
        RefreshEngineToggles();
        StatusText = _orch == null
            ? LocalizationManager.Get("StatusNoDriver")
            : _driverRestricted && state != EngineRunState.Shaping
                ? LocalizationManager.Get("StatusDriverRestricted")
                : state switch
                {
                    EngineRunState.Shaping => LocalizationManager.Get("StatusRedirecting",
                        _orch.Catalog.SpoofTargets().Count),
                    EngineRunState.Idle => LocalizationManager.Get("StatusReady") + $" ({_orch.DriverBackend})",
                    _ => LocalizationManager.Get("StatusNoAdapter"),
                };
        EngineBadge = _orch == null ? "" : $"{(_orch.CurrentKind == EngineKind.Modern ? LocalizationManager.Get("EngineModern") : LocalizationManager.Get("EngineLegacy"))} · {_orch.DriverBackend}";
    }

    private void RefreshEngineToggles()
    {
        IsEngineLegacy = _orch?.CurrentKind == EngineKind.Legacy;
        IsEngineModern = !IsEngineLegacy;
    }

    // ---------------- localization ----------------

    public string UnitsButtonText => LocalizationManager.Get("BtnUnits", SpeedFormatter.CapUnit(Units));

    /// <summary>Label only shows the current theme ("Oscuro"/"Claro", "Dark"/"Light").</summary>
    public string ThemeButtonText => Theme == AppTheme.Dark
        ? LocalizationManager.Get("ThemeDark")
        : LocalizationManager.Get("ThemeLight");

    /// <summary>Label only shows the current language's own name ("Español"/"English").</summary>
    public string LanguageButtonText => LocalizationManager.CurrentLanguage == LocalizationManager.Language.English
        ? "English"
        : "Español";

    private void ApplyLanguage()
    {
        OnPropertyChanged(nameof(UnitsButtonText));
        OnPropertyChanged(nameof(ThemeButtonText));
        OnPropertyChanged(nameof(LanguageButtonText));
        OnPropertyChanged(nameof(ShapingTooltip));
        RefreshState();
        foreach (var d in Devices) { d.RefreshIdentity(); d.RefreshSpeeds(); d.RefreshCaps(); }
        UiStringsChanged?.Invoke();
    }

    // ---------------- startup / shutdown ----------------

    /// <summary>Called once from the window's Loaded event: reopens the remembered
    /// adapter, or prompts for one when there are no settings yet.</summary>
    public async Task InitializeAsync()
    {
        if (_orch == null) return;
        var adapters = _adapters.GetUsableAdapters();
        if (adapters.Count == 0)
        {
            MessageRequested?.Invoke(LocalizationManager.Get("MsgNoAdapter"),
                LocalizationManager.Get("AppTitle"), true);
            StatusText = LocalizationManager.Get("StatusNoAdapter");
            return;
        }

        var saved = adapters.FirstOrDefault(a => a.Id == _settings.LastAdapterId);
        if (saved == null)
        {
            // Let the window paint before the modal picker appears.
            _ = _ui.BeginInvoke(new Action(PickAdapter));
            return;
        }

        IsBusy = true;
        BusyText = LocalizationManager.Get("StatusScanning");
        try
        {
            bool ok = await Task.Run(() => _orch.OpenAdapter(saved));
            if (!ok)
                MessageRequested?.Invoke(LocalizationManager.Get("MsgDriverError", "open failed"),
                    LocalizationManager.Get("AppTitle"), true);
        }
        finally
        {
            IsBusy = false;
            RefreshState();
        }
    }

    /// <summary>True while Windows is logging off or shutting down — the shell must not
    /// block that with a confirmation dialog.</summary>
    public bool IsSessionEnding { get; private set; }

    /// <summary>
    /// Windows-initiated logoff/shutdown: the process is about to be torn down, so heal
    /// synchronously with a hard time bound (last chance to restore the network), then
    /// release the engine. Called from the WPF SessionEnding event on the UI thread.
    /// </summary>
    public void HealSynchronouslyForShutdown(int timeoutMs = 3000)
    {
        IsSessionEnding = true;
        if (_orch == null) return;
        try
        {
            // The whole teardown runs off the UI thread and is bounded as a whole,
            // not just the first heal call.
            var teardown = Task.Run(() =>
            {
                _orch.StopAndHealAsync().GetAwaiter().GetResult();
                _orch.Dispose(); // retries any outstanding heal claims
            });
            teardown.Wait(timeoutMs); // Windows may terminate us after the logoff deadline
        }
        catch { /* best effort during shutdown */ }
    }

    /// <summary>Heals the LAN (if shaping) and disposes the engine before the window closes.</summary>
    public async Task PrepareShutdownAsync()
    {
        if (_orch == null) return;
        if (IsShaping)
        {
            IsBusy = true;
            BusyText = LocalizationManager.Get("MsgRestoringArp");
            try { await _orch.StopAndHealAsync(); }
            finally { IsBusy = false; }
        }
        _orch.Dispose();
    }

    /// <summary>Idempotent teardown for the Closed path.</summary>
    public void Shutdown()
    {
        _fastTimer.Stop();
        // SessionEnding owns a bounded background teardown; do not block logoff
        // with a second, unbounded CloseCore on the UI thread.
        if (IsSessionEnding) return;
        _orch?.Dispose();
    }

    /// <summary>Sets every device's download/upload limit back to 0 (unlimited).</summary>
    private void ResetLimits()
    {
        if (_orch == null) return;
        _orch.SetCapsBulk(_orch.Catalog.Devices, 0, 0);
        foreach (var vm in Devices) vm.RefreshCaps();
    }

    // ---------------- plumbing ----------------

    private async Task RunBusyAsync(string busyText, Func<Task> work)
    {
        IsBusy = true;
        BusyText = busyText;
        try { await work(); }
        finally { IsBusy = false; }
    }

    private void Dispatch(Action action)
    {
        if (_ui.CheckAccess()) action();
        else _ui.BeginInvoke(action);
    }
}
