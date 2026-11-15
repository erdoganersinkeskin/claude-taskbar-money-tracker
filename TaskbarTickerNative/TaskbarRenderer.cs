using System.Globalization;
using Microsoft.Win32;

namespace TaskbarTickerNative;

/// Açık/koyu temaya göre renkler (0xAARRGGBB).
internal readonly record struct Theme(uint Fg, uint Dim, uint Up, uint Down)
{
    /// Görev çubuğunun teması
    public static Theme Taskbar() => IsLight("SystemUsesLightTheme")
        ? new(Gdi.Argb(255, 20, 20, 20), Gdi.Argb(150, 20, 20, 20), Gdi.Argb(255, 0, 140, 80), Gdi.Argb(255, 200, 40, 40))
        : new(Gdi.Argb(255, 255, 255, 255), Gdi.Argb(150, 255, 255, 255), Gdi.Argb(255, 61, 214, 140), Gdi.Argb(255, 255, 107, 107));

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

/// Görev çubuğundaki ikonu ve yatay şeridi çizer. Tüm ölçüler aygıt pikselidir (DPI ölçeği elle uygulanır).
internal static class TaskbarRenderer
{
    // Neredeyse saydam zemin: tamamen saydam pikseller fare tıklamalarını yakalamaz.
    private const uint Hit = 0x01000000;

    /// İkon görünümü: madeni para simgesi.
    public static Gdi.Surface DrawIcon(int height, float scale, bool hover, bool active)
    {
        var t = Theme.Taskbar();
        int w = (int)(34 * scale);
        var s = new Gdi.Surface(w, height, 3);
        s.Pixels.Fill(Hit);

        // Üzerine gelince / panel açıkken görev çubuğu düğmesi gibi hafif vurgu
        int alpha = active ? 34 : hover ? 22 : 0;
        if (alpha > 0)
            s.FillRounded(Gdi.WithAlpha(t.Fg, alpha), 1 * scale, 4 * scale, w - 2 * scale, height - 8 * scale, 5 * scale);

        uint fg = Gdi.WithAlpha(t.Fg, 235);
        float cx = w / 2f, cy = height / 2f, r = 10 * scale;
        s.DrawEllipse(fg, 1.5f * scale, cx - r, cy - r, 2 * r, 2 * r);

        float size = 12.5f * scale;
        float gw = Gdi.Width("$", FontKind.SemiBold, size), gh = size * Gdi.LineHeight;
        s.Text("$", FontKind.SemiBold, size, fg, cx - gw / 2, cy - gh / 2);
        return s;
    }

    private readonly record struct Cell(string Label, float LabelW, string Price, string Pct, double? Change, uint Trend, uint PriceColor, int Width);

    /// Yatay şerit görünümü: her kalem için üstte soluk etiket + yön oku, altta fiyat.
    public static Gdi.Surface DrawStrip(Config cfg, IReadOnlyList<Quote> quotes, int height, float scale)
    {
        var t = Theme.Taskbar();
        float pad = 8 * scale, gap = 4 * scale, tri = 6 * scale;
        float small = 10.5f * scale, big = 14 * scale;
        int n = Math.Min(cfg.Items.Count, quotes.Count);
        var cells = new Cell[n];
        int total = 0;

        // 1) Ölç: her hücre yalnızca içeriği kadar yer kaplar.
        for (int i = 0; i < n; i++)
        {
            var item = cfg.Items[i];
            var q = quotes[i];
            double? change = q.ChangePercent;
            uint trend = q.Stale ? t.Dim : change is < 0 ? t.Down : t.Up;

            string price = q.Price.HasValue
                ? item.Prefix + q.Price.Value.ToString("N" + item.StripDecimals, CultureInfo.CurrentCulture)
                : "…";
            string pct = cfg.StripShowPercent && change.HasValue
                ? Math.Abs(change.Value).ToString("0.0", CultureInfo.CurrentCulture) + "%"
                : "";

            float labelW = Gdi.Width(item.Label, FontKind.Regular, small);
            float top = labelW + gap + tri + (pct.Length > 0 ? gap + Gdi.Width(pct, FontKind.Regular, small) : 0);
            float bottom = Gdi.Width(price, FontKind.Regular, big);
            int width = (int)MathF.Ceiling(Math.Max(top, bottom) + 2 * pad);
            cells[i] = new Cell(item.Label, labelW, price, pct, change, trend, q.Stale ? t.Dim : t.Fg, width);
            total += width;
        }

        // 2) Çiz
        var s = new Gdi.Surface(Math.Max(1, total), height, 3);
        s.Pixels.Fill(Hit);
        float row1 = 5 * scale, row2 = 20 * scale;
        float x0 = 0;
        Span<PointF> pts = stackalloc PointF[3];
        foreach (var c in cells)
        {
            float x = x0 + pad;
            s.Text(c.Label, FontKind.Regular, small, t.Dim, x, row1);

            if (c.Change.HasValue)
            {
                bool isUp = c.Change.Value >= 0;
                float tx = x + c.LabelW + gap;
                float cy = row1 + small * Gdi.LineHeight / 2 + 0.5f * scale, half = 2.5f * scale;
                pts[0] = new PointF(tx, isUp ? cy + half : cy - half);
                pts[1] = new PointF(tx + tri, isUp ? cy + half : cy - half);
                pts[2] = new PointF(tx + tri / 2, isUp ? cy - half : cy + half);
                s.FillPolygon(c.Trend, pts);
                if (c.Pct.Length > 0) s.Text(c.Pct, FontKind.Regular, small, c.Trend, tx + tri + gap, row1);
            }

            s.Text(c.Price, FontKind.Regular, big, c.PriceColor, x, row2);
            x0 += c.Width;
        }
        return s;
    }
}
