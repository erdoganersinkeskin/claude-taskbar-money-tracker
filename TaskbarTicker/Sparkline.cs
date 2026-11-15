using System.Windows;
using System.Windows.Media;

namespace TaskbarTicker;

/// Bir değer dizisini çizgi + altı hafif dolgulu mini grafik olarak çizer.
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var s = Values;
        double w = ActualWidth, h = ActualHeight;
        if (s == null || s.Count < 2 || w <= 0 || h <= 2) return;

        double min = s.Min(), max = s.Max();
        double range = max - min;
        if (range <= 0) range = 1;

        Point At(int i) => new(w * i / (s.Count - 1), h - 1 - (s[i] - min) / range * (h - 2));

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(At(0), false, false);
            for (int i = 1; i < s.Count; i++) g.LineTo(At(i), true, true);
        }
        line.Freeze();

        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(0, h), true, true);
            for (int i = 0; i < s.Count; i++) g.LineTo(At(i), false, false);
            g.LineTo(new Point(w, h), false, false);
        }
        area.Freeze();

        Color color = (Stroke as SolidColorBrush)?.Color ?? Colors.Gray;
        var fill = new LinearGradientBrush(
            Color.FromArgb(28, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90);
        fill.Freeze();
        dc.DrawGeometry(fill, null, area);

        var pen = new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);
    }
}
