// Windows 11 visual treatment: Mica backdrop + rounded corners (via DWM),
// Segoe UI Variable typography, light WinUI-style grid/toolbar/status colors.
// Everything is best-effort: older Windows simply keeps the classic look.

using System.Runtime.InteropServices;

namespace SelfishNetV2.Test.UI;

internal static class Win11Theme
{
    // DWMWA_WINDOW_CORNER_PREFERENCE / DWMWA_SYSTEMBACKDROP_TYPE
    private const int DwmCornerPreference = 33;
    private const int DwmSystemBackdrop = 38;
    private const int CornerRound = 2;   // DWMWCP_ROUND
    private const int BackdropMica = 2; // DWMSBT_MAINWINDOW

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static readonly Color Accent = Color.FromArgb(0, 120, 212);
    public static readonly Color GridLine = Color.FromArgb(0xED, 0xEB, 0xE9);
    public static readonly Color Selection = Color.FromArgb(0xCC, 0xE8, 0xFF);
    public static readonly Color SurfaceAlt = Color.FromArgb(0xF3, 0xF3, 0xF3);

    public static void Apply(Form form)
    {
        TryMica(form);
        form.Font = PreferredFont(9f);
        form.BackColor = Color.White;
        foreach (var c in AllControls(form))
        {
            switch (c)
            {
                case DataGridView g: StyleGrid(g); break;
                case StatusStrip: c.BackColor = SurfaceAlt; break;
                case ToolStrip t: StyleToolbar(t); break;
            }
        }
        if (form is not AdapterDialog)
            form.ForeColor = Color.FromArgb(0x1B, 0x1B, 0x1B);
    }

    private static void TryMica(Form form)
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
            IntPtr h = form.Handle;
            int corner = CornerRound, backdrop = BackdropMica;
            DwmSetWindowAttribute(h, DwmCornerPreference, ref corner, sizeof(int));
            DwmSetWindowAttribute(h, DwmSystemBackdrop, ref backdrop, sizeof(int));
        }
        catch { /* pre-Win11 or headless: ignore */ }
    }

    public static Font PreferredFont(float size)
    {
        foreach (string name in new[] { "Segoe UI Variable Text", "Segoe UI Variable", "Segoe UI" })
        {
            try { return new Font(name, size); }
            catch { /* try next */ }
        }
        return SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont ?? Control.DefaultFont;
    }

    public static void StyleGrid(DataGridView g)
    {
        g.EnableHeadersVisualStyles = false;
        g.BackgroundColor = Color.White;
        g.BorderStyle = BorderStyle.None;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.GridColor = GridLine;
        g.RowTemplate.Height = 30;
        g.ColumnHeadersHeight = 34;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.FromArgb(0x1B, 0x1B, 0x1B),
            Font = new Font(g.Font ?? PreferredFont(9f), FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
        };
        g.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.FromArgb(0x1B, 0x1B, 0x1B),
            SelectionBackColor = Selection,
            SelectionForeColor = Color.Black,
            Padding = new Padding(8, 0, 0, 0),
        };
    }

    public static void StyleToolbar(ToolStrip t)
    {
        t.BackColor = Color.White;
        t.GripStyle = ToolStripGripStyle.Hidden;
        t.Padding = new Padding(6, 4, 6, 4);
        foreach (ToolStripItem item in t.Items)
            item.Margin = new Padding(2);
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var sub in AllControls(c))
                yield return sub;
        }
    }
}
