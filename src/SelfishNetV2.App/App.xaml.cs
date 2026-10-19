using System.Windows;
using System.Windows.Threading;
using SelfishNetV2.App.Infrastructure;
using SelfishNetV2.App.Theming;
using SelfishNetV2.App.ViewModels;
using SelfishNetV2.Drivers;
using SelfishNetV2.Engines;
using SelfishNetV2.Services;

namespace SelfishNetV2.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var settings = SettingsStore.Load();
        LocalizationManager.SetLanguage(settings.Language == "English"
            ? LocalizationManager.Language.English
            : LocalizationManager.Language.SpanishLA);
        ThemeManager.Apply(settings.Theme == "Light" ? AppTheme.Light : AppTheme.Dark);

        try { PcapAbiSelfTest.ValidateStructs(); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "SelfishNet v2", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        string? driverProblem = null;
        IPcapDriver? driver = null;
        var probe = DriverLocator.Probe();
        if (probe.Available)
        {
            try { driver = DriverLocator.LoadBest(); }
            catch (Exception ex) { driverProblem = ex.Message; }
        }
        else
        {
            driverProblem = probe.Detail;
        }

        EngineOrchestrator? orchestrator = null;
        if (driver != null)
        {
            var kind = settings.Engine == "Legacy" ? EngineKind.Legacy : EngineKind.Modern;
            orchestrator = new EngineOrchestrator(driver, KnownDevicesStore.Load(), kind);
        }

        // Start unelevated whenever possible. The packet driver is the only thing
        // that may need admin rights, and that is an Npcap install-time option.
        bool driverRestricted = false;
        if (driver != null)
        {
            var access = DriverAccessProbe.Probe(driver);
            if (access == DriverAccess.Denied)
            {
                var answer = MessageBox.Show(LocalizationManager.Get("MsgElevationRequired"),
                    "SelfishNet v2", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer == MessageBoxResult.Yes && ElevationHelper.RelaunchElevated())
                    return; // elevated instance takes over
                driverRestricted = true;
            }
        }

        var vm = new MainViewModel(orchestrator, new AdapterService(driver), settings, driverProblem, driverRestricted);
        var window = new MainWindow(vm);
        MainWindow = window;
        window.Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "SelfishNet v2", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
