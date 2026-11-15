using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace TaskbarTicker;

/// Görev çubuğunun açık/koyu temasına göre renkler.
internal readonly record struct TaskbarTheme(Color Fg, Color Dim, Color Up, Color Down)
{
    public static TaskbarTheme Current() => IsLight("SystemUsesLightTheme")
        ? new(Color.FromRgb(20, 20, 20), Color.FromArgb(150, 20, 20, 20), Color.FromRgb(0, 140, 80), Color.FromRgb(200, 40, 40))
        : new(Colors.White, Color.FromArgb(150, 255, 255, 255), Color.FromRgb(61, 214, 140), Color.FromRgb(255, 107, 107));

    /// "SystemUsesLightTheme" görev çubuğunu, "AppsUseLightTheme" uygulama pencerelerini belirler.
    public static bool IsLight(string valueName)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue(valueName) is int v && v != 0;
        }
        catch { return false; }
    }
}

/// Görev çubuğundaki ikonu ve yatay şeridi WPF ile ekran dışı bir bitmap'e çizer.
/// Tüm ölçüler aygıt pikselidir (DPI ölçeği elle uygulanır).
internal static class TaskbarRenderer
{
    private static readonly Typeface Regular = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface SemiBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    // Neredeyse saydam zemin: tamamen saydam pikseller fare tıklamalarını yakalamaz.
    private static readonly Brush HitBrush = Frozen(Color.FromArgb(1, 0, 0, 0));

    /// İkon görünümü: madeni para simgesi.
    public static BitmapSource DrawIcon(int height, float scale, bool hover, bool active)
    {
        var t = TaskbarTheme.Current();
        int w = (int)(34 * scale);
        return Render(w, height, dc =>
        {
            // Üzerine gelince / panel açıkken görev çubuğu düğmesi gibi hafif vurgu
            byte alpha = active ? (byte)34 : hover ? (byte)22 : (byte)0;
            if (alpha > 0)
            {
                var r = new Rect(1 * scale, 4 * scale, w - 2 * scale, height - 8 * scale);
                dc.DrawRoundedRectangle(Frozen(Color.FromArgb(alpha, t.Fg.R, t.Fg.G, t.Fg.B)), null, r, 5 * scale, 5 * scale);
            }

            var fg = Frozen(Color.FromArgb(235, t.Fg.R, t.Fg.G, t.Fg.B));
            var pen = new Pen(fg, 1.5 * scale);
            pen.Freeze();
            var center = new Point(w / 2.0, height / 2.0);
            dc.DrawEllipse(null, pen, center, 10 * scale, 10 * scale);

            var glyph = Text("$", SemiBold, 12.5 * scale, fg);
            dc.DrawText(glyph, new Point(center.X - glyph.Width / 2, center.Y - glyph.Height / 2));
        });
    }

    /// Yatay şerit görünümü: her kalem için üstte soluk etiket + yön oku, altta fiyat.
    public static BitmapSource DrawStrip(Config cfg, IReadOnlyList<Quote> quotes, int height, float scale)
    {
        var t = TaskbarTheme.Current();
        var fg = Frozen(t.Fg);
        var dim = Frozen(t.Dim);
        var up = Frozen(t.Up);
        var down = Frozen(t.Down);

        double pad = 8 * scale, gap = 4 * scale, tri = 6 * scale;
        int n = Math.Min(cfg.Items.Count, quotes.Count);
        var cells = new List<(FormattedText Label, FormattedText Price, FormattedText? Pct, double? Change, Brush Trend, double Width)>(n);

        // 1) Ölç: her hücre yalnızca içeriği kadar yer kaplar.
        for (int i = 0; i < n; i++)
        {
            var item = cfg.Items[i];
            var q = quotes[i];
            double? change = q.ChangePercent;
            Brush trend = q.Stale ? dim : change is < 0 ? down : up;

            var label = Text(item.Label, Regular, 10.5 * scale, dim);
            string priceText = q.Price.HasValue
                ? item.Prefix + q.Price.Value.ToString("N" + item.StripDecimals, CultureInfo.CurrentCulture)
                : "…";
            var price = Text(priceText, Regular, 14 * scale, q.Stale ? dim : fg);
            FormattedText? pct = cfg.StripShowPercent && change.HasValue
                ? Text(Math.Abs(change.Value).ToString("0.0", CultureInfo.CurrentCulture) + "%", Regular, 10.5 * scale, trend)
                : null;

            double top = label.Width + gap + tri + (pct != null ? gap + pct.Width : 0);
            cells.Add((label, price, pct, change, trend, Math.Ceiling(Math.Max(top, price.Width) + 2 * pad)));
        }

        // 2) Çiz
        int total = Math.Max(1, (int)cells.Sum(c => c.Width));
        double row1 = 5 * scale, row2 = 20 * scale;
        return Render(total, height, dc =>
        {
            double x0 = 0;
            foreach (var c in cells)
            {
                double x = x0 + pad;
                dc.DrawText(c.Label, new Point(x, row1));

                if (c.Change.HasValue)
                {
                    bool isUp = c.Change.Value >= 0;
                    double tx = x + c.Label.Width + gap;
                    double cy = row1 + c.Label.Height / 2 + 0.5 * scale, half = 2.5 * scale;
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(new Point(tx, isUp ? cy + half : cy - half), true, true);
                        g.LineTo(new Point(tx + tri, isUp ? cy + half : cy - half), false, false);
                        g.LineTo(new Point(tx + tri / 2, isUp ? cy - half : cy + half), false, false);
                    }
                    geo.Freeze();
                    dc.DrawGeometry(c.Trend, null, geo);
                    if (c.Pct != null) dc.DrawText(c.Pct, new Point(tx + tri + gap, row1));
                }

                dc.DrawText(c.Price, new Point(x, row2));
                x0 += c.Width;
            }
        });
    }

    private static BitmapSource Render(int width, int height, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(HitBrush, null, new Rect(0, 0, width, height));
            draw(dc);
        }
        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static FormattedText Text(string text, Typeface face, double size, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, null,
            TextFormattingMode.Display, 1.0);

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
