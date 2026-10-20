using System.Windows;
using SelfishNetV2.App.Theming;
using SelfishNetV2.Core;
using SelfishNetV2.Services;

namespace SelfishNetV2.App.Views;

public partial class AdapterWindow : Window
{
    public sealed record Row(
        string Name,
        string Details,
        string Ip,
        string Gateway,
        AdapterInfo Adapter);

    public AdapterInfo? Selected { get; private set; }

    public AdapterWindow(List<AdapterInfo> adapters, string? lastAdapterId)
    {
        InitializeComponent();

        var rows = adapters.Select(a => new Row(
            a.Name,
            string.IsNullOrWhiteSpace(a.Description) ? a.Name : a.Description,
            NetKeys.FormatIp(a.LocalIpKey),
            a.GatewayIpKey == 0 ? "—" : NetKeys.FormatIp(a.GatewayIpKey),
            a)).ToList();

        Grid.ItemsSource = rows;
        Grid.SelectedItem = rows.FirstOrDefault(r => r.Adapter.Id == lastAdapterId) ?? rows.FirstOrDefault();
        BtnOk.IsEnabled = Grid.SelectedItem != null;

        Grid.SelectionChanged += (_, _) => BtnOk.IsEnabled = Grid.SelectedItem != null;
        Grid.MouseDoubleClick += (_, _) => Accept();
        BtnOk.Click += (_, _) => Accept();
        BtnCancel.Click += (_, _) => DialogResult = false;

        // This window is created after startup, so apply the immersive dark title
        // bar to it as well (ThemeManager only walks existing windows).
        SourceInitialized += (_, _) => TitleBarHelper.Apply(this, ThemeManager.Current);

        Title = LocalizationManager.Get("AdapterDialogTitle");
        TxtPrompt.Text = LocalizationManager.Get("AdapterSelectPrompt");
        ColAdapter.Header = LocalizationManager.Get("AdapterColName");
        ColIp.Header = LocalizationManager.Get("AdapterColIP");
        ColGateway.Header = LocalizationManager.Get("AdapterColGateway");
        BtnOk.Content = LocalizationManager.Get("BtnOK");
        BtnCancel.Content = LocalizationManager.Get("BtnCancel");

        Loaded += (_, _) => Grid.Focus();
    }

    private void Accept()
    {
        if (Grid.SelectedItem is Row r)
        {
            Selected = r.Adapter;
            DialogResult = true;
        }
    }
}
