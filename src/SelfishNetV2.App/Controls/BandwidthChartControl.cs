using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SelfishNetV2.Services;

namespace SelfishNetV2.App.Controls;

/// <summary>
/// Lightweight real-time bandwidth chart. Keeps a rolling 120-sample window per
/// series (2 minutes at the 1 Hz stats cadence), auto-scales to a "nice" ceiling and
/// smoothly scrolls each new sample in over ~0.5 s (CompositionTarget.Rendering).
/// Zero external charting dependencies; colors come from the active theme dictionary.
/// </summary>
public sealed class BandwidthChartControl : FrameworkElement
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(BandwidthChartControl),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    private const int Capacity = 120;
    private readonly double[] _down = new double[Capacity];
    private readonly double[] _up = new double[Capacity];
    private int _count;
    private double _scroll;          // 1 → 0 between samples
    private bool _renderingHooked;
    private UnitMode _unitMode = UnitMode.Bytes;

    /// <summary>Unit system for axis + legend labels (follows the app's Units toggle).</summary>
    public UnitMode UnitMode
    {
        get => _unitMode;
        set
        {
            if (_unitMode == value) return;
            _unitMode = value;
            InvalidateVisual();
        }
    }

    private static readonly Typeface Face = new("Segoe UI");

    public BandwidthChartControl()
    {
        ClipToBounds = true;
        Loaded += (_, _) => HookRendering(true);
        Unloaded += (_, _) => HookRendering(false);
    }

    private void HookRendering(bool on)
    {
        if (on == _renderingHooked) return;
        _renderingHooked = on;
        if (on) CompositionTarget.Rendering += OnFrame;
        else CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_scroll <= 0) return;
        _scroll = Math.Max(0, _scroll - 1.0 / 30.0);
        InvalidateVisual();
    }

    public void PushSample(double downBytesPerSec, double upBytesPerSec)
    {
        if (_count < Capacity)
        {
            _down[_count] = downBytesPerSec;
            _up[_count] = upBytesPerSec;
            _count++;
        }
        else
        {
            Array.Copy(_down, 1, _down, 0, Capacity - 1);
            Array.Copy(_up, 1, _up, 0, Capacity - 1);
            _down[^1] = downBytesPerSec;
            _up[^1] = upBytesPerSec;
        }
        _scroll = 1.0;
        InvalidateVisual();
    }

    public void ClearData()
    {
        _count = 0;
        _scroll = 0;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 40 || h < 40) return;

        Brush textDim = Res("TextDimBrush", Brushes.Gray);
        Brush downBrush = Res("ChartDownBrush", Brushes.DodgerBlue);
        Brush upBrush = Res("ChartUpBrush", Brushes.LimeGreen);

        const double padL = 8, padR = 8, padT = 28, padB = 12;
        double cw = w - padL - padR;
        double ch = h - padT - padB;

        DrawText(dc, Title, 2, 2, 12, textDim, FontWeights.SemiBold);

        // ---- auto-scale ----
        double max = 1;
        for (int i = 0; i < _count; i++)
        {
            if (_down[i] > max) max = _down[i];
            if (_up[i] > max) max = _up[i];
        }
        double scaleMax = NiceCeiling(max);

        // ---- horizontal grid + scale labels ----
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x26, 0x88, 0x88, 0x88)), 1);
        gridPen.Freeze();
        for (int i = 0; i <= 4; i++)
        {
            double y = padT + ch - ch * i / 4.0;
            dc.DrawLine(gridPen, new Point(padL, y), new Point(padL + cw, y));
            if (i > 0)
                DrawText(dc, SpeedFormatter.FormatBytes(scaleMax * i / 4.0, _unitMode),
                    padL + 2, y - 14, 9, textDim, FontWeights.Normal);
        }

        // ---- current values (top-right legend) ----
        double lastDown = _count > 0 ? _down[_count - 1] : 0;
        double lastUp = _count > 0 ? _up[_count - 1] : 0;
        FormattedText legend = MakeText(
            $"\u2193 {SpeedFormatter.FormatBytes(lastDown, _unitMode)}    \u2191 {SpeedFormatter.FormatBytes(lastUp, _unitMode)}",
            11, textDim, FontWeights.SemiBold);
        dc.DrawText(legend, new Point(w - legend.Width - 4, 3));

        if (_count < 2)
        {
            dc.DrawLine(gridPen, new Point(padL, padT + ch), new Point(padL + cw, padT + ch));
            return;
        }

        // ---- series ----
        double step = cw / (Capacity - 1);
        double offset = _scroll * step;
        DrawSeries(dc, _down, downBrush, padL, padT, cw, ch, step, offset);
        DrawSeries(dc, _up, upBrush, padL, padT, cw, ch, step, offset);
    }

    private void DrawSeries(DrawingContext dc, double[] values, Brush brush,
        double padL, double padT, double cw, double ch, double step, double offset)
    {
        int n = _count;
        // Points are right-anchored; the newest sample slides in from one step beyond
        // the right edge (clipped), producing the smooth scroll.
        double firstX = padL + cw - (n - 1) * step + offset;
        double bottom = padT + ch;
        double scaleMax = seriesScaleMax();
        double scaleY(double v) => bottom - (v / scaleMax) * (ch - 2);

        // filled area under the curve
        var fillGeo = new StreamGeometry();
        using (var g = fillGeo.Open())
        {
            g.BeginFigure(new Point(firstX, bottom), true, true);
            for (int i = 0; i < n; i++)
                g.LineTo(new Point(firstX + i * step, scaleY(values[i])), true, false);
            g.LineTo(new Point(firstX + (n - 1) * step, bottom), true, false);
        }
        fillGeo.Freeze();

        Color baseColor = brush is SolidColorBrush sc ? sc.Color : Colors.DodgerBlue;
        var fill = new LinearGradientBrush(
            Color.FromArgb(0x3C, baseColor.R, baseColor.G, baseColor.B),
            Color.FromArgb(0x02, baseColor.R, baseColor.G, baseColor.B),
            new Point(0, 0), new Point(0, 1));
        fill.Freeze();
        dc.DrawGeometry(fill, null, fillGeo);

        // stroked line on top
        var lineGeo = new StreamGeometry();
        using (var g = lineGeo.Open())
        {
            g.BeginFigure(new Point(firstX, scaleY(values[0])), false, false);
            for (int i = 1; i < n; i++)
                g.LineTo(new Point(firstX + i * step, scaleY(values[i])), true, false);
        }
        lineGeo.Freeze();
        var pen = new Pen(brush, 1.7) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, lineGeo);

        double seriesScaleMax()
        {
            double m = 1;
            for (int i = 0; i < n; i++) if (values[i] > m) m = values[i];
            return NiceCeiling(m);
        }
    }

    private static double NiceCeiling(double v)
    {
        if (v <= 1) return 1;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(v)));
        double n = v / magnitude;
        double nice = n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    private void DrawText(DrawingContext dc, string text, double x, double y, double size, Brush brush, FontWeight weight) =>
        dc.DrawText(MakeText(text, size, brush, weight), new Point(x, y));

    private FormattedText MakeText(string text, double size, Brush brush, FontWeight weight) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Face.FontFamily, FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
}
