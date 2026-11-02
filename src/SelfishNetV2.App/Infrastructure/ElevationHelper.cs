using System.Diagnostics;
using System.IO;
using System.Windows;
using SelfishNetV2.Services;

namespace SelfishNetV2.App.Infrastructure;

/// <summary>Restarts the current executable with the "runas" verb (UAC prompt).
/// Used only when the packet driver denies access to standard users.</summary>
public static class ElevationHelper
{
    /// <returns>true when the elevated instance was launched (the caller should exit);
    /// false when the user cancelled the UAC prompt or elevation failed.</returns>
    public static bool RelaunchElevated()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;
            // The elevated process needs to take the single-instance mutex.
            SingleInstanceGuard.Release();
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
            });
            Application.Current.Shutdown();
            return true;
        }
        catch
        {
            SingleInstanceGuard.Reacquire(); // UAC cancelled or shell error: keep this instance protected
            return false;
        }
    }
}
