// FREE MODEL TEST PROTOTYPE — not the final app. See ../README_FREE_MODEL_TEST.md
// Entry point: DPI-aware WinForms bootstrap, driver/admin pre-checks surface in UI.

using SelfishNetV2.Test.UI;

namespace SelfishNetV2.Test;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // Classic bootstrap (no ApplicationConfiguration.Initialize mixing —
        // SetHighDpiMode must come first).
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var main = new MainForm();
        Application.Run(main);
    }
}
