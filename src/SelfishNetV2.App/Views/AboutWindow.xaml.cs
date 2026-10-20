using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SelfishNetV2.App.Theming;
using SelfishNetV2.App.ViewModels;
using SelfishNetV2.Engines;
using SelfishNetV2.Services;

namespace SelfishNetV2.App.Views;

public partial class AboutWindow : Window
{
    private readonly AboutInfo _info;
    private readonly List<(string Label, string Value)> _rows = new();

    public AboutWindow(AboutInfo info)
    {
        _info = info;
        InitializeComponent();

        SourceInitialized += (_, _) => TitleBarHelper.Apply(this, ThemeManager.Current);
        BtnClose.Click += (_, _) => Close();
        BtnCopy.Click += (_, _) => CopyDetails();

        BuildContent();
    }

    private static string L(string key, params object[] args) => LocalizationManager.Get(key, args);

    private void BuildContent()
    {
        Title = L("BtnAbout");
        TxtTagline.Text = L("AboutTagline");
        TxtVersion.Text = L("AboutVersionLine", _info.Version, _info.EngineName, _info.DriverBackend);

        TxtSummary.Text = L("AboutSummary");
        TxtSafety.Text = L("AboutSafety");

        AddBullet("AboutBulletDiscovery");
        AddBullet("AboutBulletEngines");
        AddBullet("AboutBulletHealing");
        AddBullet("AboutBulletPrivacy");

        HdrSystem.Text = L("AboutSectionSystem").ToUpperInvariant();
        AddRow(PanelSystem, L("AboutLabelOs"), _info.SystemText);
        AddRow(PanelSystem, L("AboutLabelRuntime"), _info.RuntimeText);
        AddRow(PanelSystem, L("AboutLabelDriver"), _info.DriverBackend);

        HdrControl.Text = L("AboutSectionControl").ToUpperInvariant();
        AddRow(PanelControl, L("AboutLabelAdapter"), _info.AdapterName);
        AddRow(PanelControl, L("AboutLabelLocal"), _info.LocalText, mono: true);
        AddRow(PanelControl, L("AboutLabelGateway"), $"{_info.GatewayText}  ({_info.GatewayMac})", mono: true);
        AddRow(PanelControl, L("AboutLabelState"), StateText());
        AddRow(PanelControl, L("AboutLabelDevices"), _info.Devices.ToString());
        AddRow(PanelControl, L("AboutLabelTargets"), _info.Targets.ToString());
        AddRow(PanelControl, L("AboutLabelForwarded"),
            $"\u2193 {SpeedFormatter.FormatTotal(_info.ForwardedDown)}    \u2191 {SpeedFormatter.FormatTotal(_info.ForwardedUp)}");
        AddRow(PanelControl, L("AboutLabelDropped"), _info.Dropped.ToString("N0"));
        AddRow(PanelControl, L("AboutLabelSendFailures"), _info.SendFailures.ToString("N0"));
        AddRow(PanelControl, L("AboutLabelOsForwarding"),
            _info.OsForwarding ? L("AboutEnabled") : L("AboutDisabled"));

        TxtOsHint.Text = L("AboutOsForwardingHint");
        BtnCopy.Content = L("AboutBtnCopy");
        BtnClose.Content = L("AboutBtnClose");
    }

    private string StateText() => _info.State switch
    {
        EngineRunState.Shaping => L("AboutStateShaping"),
        EngineRunState.Idle => L("AboutStateIdle"),
        _ => L("AboutStateClosed"),
    };

    private void AddBullet(string key)
    {
        PanelBullets.Children.Add(new TextBlock
        {
            Text = "\u2022  " + L(key),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
            Margin = new Thickness(0, 0, 0, 6)
        });
    }

    private void AddRow(Panel panel, string label, string value, bool mono = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 12.5,
            Foreground = (Brush)FindResource("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = mono ? 13 : 14,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (mono) valueBlock.FontFamily = new FontFamily("Consolas");

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        panel.Children.Add(grid);

        _rows.Add((label, value));
    }

    private void CopyDetails()
    {
        var lines = new List<string>
        {
            "SelfishNet v2",
            TxtVersion.Text,
            "",
            L("AboutSectionSystem"),
        };
        lines.AddRange(_rows.Take(3).Select(r => $"{r.Label}: {r.Value}"));
        lines.Add("");
        lines.Add(L("AboutSectionControl"));
        lines.AddRange(_rows.Skip(3).Select(r => $"{r.Label}: {r.Value}"));

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            TxtCopied.Text = L("AboutCopied");
            TxtCopied.Visibility = Visibility.Visible;
        }
        catch
        {
            TxtCopied.Visibility = Visibility.Collapsed;
        }
    }
}
