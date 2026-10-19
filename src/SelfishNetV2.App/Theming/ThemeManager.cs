using System.Windows;
using System.Windows.Interop;

namespace SelfishNetV2.App.Theming;

public enum AppTheme { Dark, Light }

/// <summary>Swaps the active palette ResourceDictionary; every themed brush is a
/// DynamicResource so the whole UI recolors instantly. Also flips the Win32 title bar.</summary>
public static class ThemeManager
{
    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    /// <summary>Component-relative pack URI — resolves inside THIS assembly regardless of
    /// which assembly is the entry point (the application-relative form silently breaks
    /// when the shell is hosted by another exe).</summary>
    private static Uri ThemeUri(AppTheme theme)
    {
        string assembly = typeof(ThemeManager).Assembly.GetName().Name!;
        return new Uri($"pack://application:,,,/{assembly};component/Themes/{theme}.xaml", UriKind.Absolute);
    }

    public static void Apply(AppTheme theme)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        for (int i = 0; i < merged.Count; i++)
        {
            string src = merged[i].Source?.ToString() ?? "";
            if (src.Contains("Themes/Dark.xaml") || src.Contains("Themes/Light.xaml"))
            {
                merged[i] = new ResourceDictionary { Source = ThemeUri(theme) };
                Current = theme;
                foreach (Window w in Application.Current.Windows)
                    TitleBarHelper.Apply(w, theme);
                return;
            }
        }
        // first call: no theme dictionary yet → insert at the front
        merged.Insert(0, new ResourceDictionary { Source = ThemeUri(theme) });
        Current = theme;
    }
}

/// <summary>
/// Immersive dark title bar via DWM. Supported from Windows 10 1809; the
/// attribute number changed with 1903, so both are attempted:
///   1809 (build 17763)        -> 19
///   1903+ (build 18362+) / 11 -> 20
/// On Windows 10 1607–1803 neither exists; the call fails silently and the
/// window simply keeps the standard title bar (nothing breaks).
/// </summary>
public static class TitleBarHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;          // Windows 10 1903+ / Windows 11
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_1809 = 19;     // Windows 10 1809

    public static void Apply(Window window, AppTheme theme)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int useDark = theme == AppTheme.Dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_1809, ref useDark, sizeof(int));
        }
        catch { /* below 1809: keep the default (light) chrome */ }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
