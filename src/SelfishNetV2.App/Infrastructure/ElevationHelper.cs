using System.Diagnostics;
using System.IO;
using System.Windows;

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
            return false; // UAC cancelled (Win32 1223) or shell error
        }
    }
}
