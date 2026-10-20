using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using SelfishNetV2.App.Theming;
using SelfishNetV2.App.ViewModels;
using SelfishNetV2.Services;

namespace SelfishNetV2.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _closeConfirmed;
    private bool _closing;
    private bool _initialized;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;

        _vm.MessageRequested += OnMessageRequested;
        _vm.ChartTick += OnChartTick;
        _vm.UiStringsChanged += ApplyLocalization;
        _vm.ThemeChanged += OnThemeChanged;
        _vm.ElevationRequested += OnElevationRequested;

        SourceInitialized += (_, _) => TitleBarHelper.Apply(this, ThemeManager.Current);
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        DeviceGrid.SelectionChanged += (_, _) => SelectedChart.ClearData();
        DeviceGrid.PreviewKeyDown += OnGridKeyDown;

        ApplyLocalization();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        await _vm.InitializeAsync();
    }

    // ---------------- localization ----------------

    private static string L(string key) => LocalizationManager.Get(key);

    private void ApplyLocalization()
    {
        Title = L("AppTitle");

        BtnDiscover.Content = L("BtnDiscover");
        BtnDiscover.ToolTip = L("BtnDiscoverTooltip");
        BtnAdapter.Content = L("BtnAdapter");
        BtnAdapter.ToolTip = L("BtnAdapterTooltip");
        BtnResetLimits.Content = L("BtnResetLimits");
        BtnResetLimits.ToolTip = L("BtnResetLimitsTooltip");

        LblEngine.Text = L("EngineGroupLabel");
        TglLegacy.Content = L("EngineLegacy");
        TglModern.Content = L("EngineModern");
        TglLegacy.ToolTip = L("EngineSwitchTooltip");
        TglModern.ToolTip = L("EngineSwitchTooltip");

        BtnUnits.ToolTip = L("BtnUnitsTooltip");
        BtnTheme.ToolTip = L("BtnThemeTooltip");
        BtnLanguage.ToolTip = L("BtnLanguageTooltip");
        BtnAbout.Content = L("BtnAbout");

        TxtDriverBanner.Text = L("MsgDriverNotFound");
        BtnDownloadNpcap.Content = L("BtnDownloadNpcap");
        BtnDownloadNpcap.ToolTip = L("BtnDownloadNpcapTooltip");
        TotalChart.Title = L("ChartTotal");
        SelectedChart.Title = L("ChartSelected");
        TotalChart.UnitMode = _vm.Units;
        SelectedChart.UnitMode = _vm.Units;

        string unit = SpeedFormatter.CapUnit(_vm.Units);
        string capTip = string.Format(L("CapCellTooltip"), unit);
        ColName.Header = Header(L("ColPCName"), L("NameColumnTooltip"));
        ColIp.Header = L("ColPCIP");
        ColMac.Header = L("ColPCMac");
        ColDown.Header = Header2(L("ColDownload"), $"({unit})", null);
        ColUp.Header = Header2(L("ColUpload"), $"({unit})", null);
        ColDownCap.Header = Header2(L("ColDownCap"), $"({unit})", capTip);
        ColUpCap.Header = Header2(L("ColUpCap"), $"({unit})", capTip);
        ColBlock.Header = Header(L("ColBlock"), L("BlockColumnTooltip"));
    }

    /// <summary>Header content that carries a tooltip (DataGridColumn has no HeaderToolTip).</summary>
    private static TextBlock Header(string text, string tooltip) =>
        new() { Text = text, ToolTip = tooltip };

    /// <summary>Two-line header ("Download" / "(KByte/s)") — avoids truncation in narrow columns.</summary>
    private static TextBlock Header2(string first, string second, string? tooltip)
    {
        var tb = new TextBlock();
        tb.Inlines.Add(new Run(first));
        tb.Inlines.Add(new LineBreak());
        tb.Inlines.Add(new Run(second));
        if (tooltip != null) tb.ToolTip = tooltip;
        return tb;
    }

    // ---------------- VM event plumbing ----------------

    private void OnMessageRequested(string text, string title, bool isError) =>
        MessageBox.Show(this, text, title, MessageBoxButton.OK,
            isError ? MessageBoxImage.Error : MessageBoxImage.Information);

    private void OnElevationRequested()
    {
        var result = MessageBox.Show(this, L("MsgElevationRequired"), L("AppTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            Infrastructure.ElevationHelper.RelaunchElevated();
    }

    private void OnChartTick(double totalDown, double totalUp, double selectedDown, double selectedUp)
    {
        TotalChart.PushSample(totalDown, totalUp);
        SelectedChart.PushSample(selectedDown, selectedUp);
    }

    private void OnThemeChanged()
    {
        TotalChart.InvalidateVisual();
        SelectedChart.InvalidateVisual();
    }

    // ---------------- grid interaction ----------------

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        // Commit a half-typed cap/name cell without requiring a second click.
        DeviceGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        DeviceGrid.CommitEdit(DataGridEditingUnit.Row, true);
        e.Handled = true;
    }

    // ---------------- shutdown ----------------

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed) return;

        // Windows logoff/shutdown: the engine already healed synchronously in
        // SessionEnding; never block the OS with a confirmation dialog.
        if (_vm.IsSessionEnding)
        {
            _closeConfirmed = true;
            _vm.Shutdown();
            return;
        }

        if (!_vm.IsShaping)
        {
            _vm.Shutdown();
            return;
        }

        e.Cancel = true;
        if (_closing) return;

        var result = MessageBox.Show(this, L("MsgConfirmQuit"), L("AppTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        _closing = true;
        try
        {
            await _vm.PrepareShutdownAsync();
            _closeConfirmed = true;
            Close();
        }
        finally
        {
            _closing = false;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _vm.MessageRequested -= OnMessageRequested;
        _vm.ChartTick -= OnChartTick;
        _vm.UiStringsChanged -= ApplyLocalization;
        _vm.ThemeChanged -= OnThemeChanged;
        _vm.ElevationRequested -= OnElevationRequested;
        _vm.Shutdown();
    }
}
