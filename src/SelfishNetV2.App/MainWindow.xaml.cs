using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
        _vm.ConfirmRequested += OnConfirmRequested;
        _vm.ChartTick += OnChartTick;
        _vm.UiStringsChanged += ApplyLocalization;
        _vm.ThemeChanged += OnThemeChanged;

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

        LblEngine.Text = L("EngineGroupLabel");
        TglLegacy.Content = L("EngineLegacy");
        TglModern.Content = L("EngineModern");
        TglLegacy.ToolTip = L("EngineSwitchTooltip");
        TglModern.ToolTip = L("EngineSwitchTooltip");

        BtnUnits.ToolTip = L("BtnUnitsTooltip");
        BtnTheme.ToolTip = L("BtnThemeTooltip");
        LblLanguage.Text = L("MenuLanguage");
        BtnAbout.Content = L("BtnAbout");

        TxtDriverBanner.Text = L("MsgDriverNotFound");
        TotalChart.Title = L("ChartTotal");
        SelectedChart.Title = L("ChartSelected");

        string unit = SpeedFormatter.CapUnit(_vm.Units);
        ColName.Header = Header(L("ColPCName"), L("NameColumnTooltip"));
        ColIp.Header = L("ColPCIP");
        ColMac.Header = L("ColPCMac");
        ColDown.Header = $"{L("ColDownload")} ({unit})";
        ColUp.Header = $"{L("ColUpload")} ({unit})";
        ColDownCap.Header = Header($"{L("ColDownCap")} ({unit})", string.Format(L("CapCellTooltip"), unit));
        ColUpCap.Header = Header($"{L("ColUpCap")} ({unit})", string.Format(L("CapCellTooltip"), unit));
        ColBlock.Header = Header(L("ColBlock"), L("BlockColumnTooltip"));
        ColSpoof.Header = Header(L("ColSpoof"), L("SpoofColumnTooltip"));
    }

    /// <summary>Header content that carries a tooltip (DataGridColumn has no HeaderToolTip).</summary>
    private static TextBlock Header(string text, string tooltip) =>
        new() { Text = text, ToolTip = tooltip };

    // ---------------- VM event plumbing ----------------

    private void OnMessageRequested(string text, string title, bool isError) =>
        MessageBox.Show(this, text, title, MessageBoxButton.OK,
            isError ? MessageBoxImage.Error : MessageBoxImage.Information);

    private bool OnConfirmRequested(string text, string title) =>
        MessageBox.Show(this, text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

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
        _vm.ConfirmRequested -= OnConfirmRequested;
        _vm.ChartTick -= OnChartTick;
        _vm.UiStringsChanged -= ApplyLocalization;
        _vm.ThemeChanged -= OnThemeChanged;
        _vm.Shutdown();
    }
}
