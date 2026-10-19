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

/// <summary>Immersive dark title bar (Windows 10 1809+ / 11) via DWM.</summary>
public static class TitleBarHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void Apply(Window window, AppTheme theme)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int useDark = theme == AppTheme.Dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }
        catch { /* older Windows: keep default chrome */ }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
