using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TaskbarTickerNative;

/// İkonun üstünde açılan, buzlu cam zeminli panel. WPF sürümündeki PanelWindow'un yerleşimini
/// (ölçüler, yazı boyutları, renkler) birebir izler; her şey GDI+ ile tek bir yüzeye çizilir.
internal static unsafe class Panel
{
    public const string ClassName = "TaskbarTickerNativePanel";

    // Yerleşim ölçüleri (96 DPI'da birim; çizimde DPI ölçeğiyle çarpılır) — PanelWindow.xaml ile aynı
    private const float PanelWidth = 380, Gap = 12;
    private const float HeaderHeight = 16 + 16 * Gdi.LineHeight + 10; // üst boşluk + başlık satırı + alt boşluk
    private const float RowHeight = 62, BottomMargin = 8;

    private static Gdi.Surface? s_surface;
    private static uint[]? s_backdrop;
    private static int s_w, s_h, s_hover = -1;
    private static float s_scale = 1;
    private static int[] s_rowTop = Array.Empty<int>();
    private static bool s_tracking;

    private static Config? s_cfg;
    private static IReadOnlyList<Quote> s_quotes = Array.Empty<Quote>();
    private static DateTime? s_updated;

    public static nint Handle { get; private set; }
    public static bool Visible { get; private set; }

    /// Panelin odağı kaybedip kendiliğinden kapandığı an (aynı tıklamayla yeniden açılmasın diye).
    public static DateTime ClosedAt { get; private set; } = DateTime.MinValue;

    private static void EnsureWindow()
    {
        if (Handle != 0) return;
        Handle = Win32.CreateWindowExW(Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST, ClassName, "Piyasalar",
            Win32.WS_POPUP, 0, 0, 10, 10, 0, 0, Win32.GetModuleHandleW(0), 0);

        // Windows 11 yuvarlak köşe + gölge
        int round = Win32.DWMWCP_ROUND;
        Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        int dark = Theme.IsLight("AppsUseLightTheme") ? 0 : 1;
        Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    /// Paneli, verilen yatay merkezin (ikon) üstünde ve görev çubuğunun hemen yukarısında gösterir (aygıt pikseli).
    public static void Show(Config cfg, IReadOnlyList<Quote> quotes, DateTime? updated, int anchorCenterX, int taskbarTop, float scale)
    {
        EnsureWindow();
        s_cfg = cfg;
        s_quotes = quotes;
        s_updated = updated;
        s_scale = scale;
        s_hover = -1;

        ComputeSize();

        Win32.SystemParametersInfoW(Win32.SPI_GETWORKAREA, 0, out var area, 0);
        int gap = (int)(Gap * scale);
        int left = Math.Max(area.Left + gap, Math.Min(anchorCenterX - s_w / 2, area.Right - gap - s_w));
        int top = Math.Max(area.Top + gap, taskbarTop - gap - s_h);

        // Cam zemin: panel görünmeden önce arkasında kalan ekran bölgesi yakalanır.
        s_backdrop = Backdrop.Make(left, top, s_w, s_h, scale, cfg.PanelTintPercent, Theme.IsLight("AppsUseLightTheme"));
        Render();

        Visible = true;
        Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, left, top, s_w, s_h, Win32.SWP_SHOWWINDOW);
        Win32.SetForegroundWindow(Handle);
        App.RenderTaskbar();
    }

    private static void ComputeSize()
    {
        int n = Math.Min(s_cfg!.Items.Count, s_quotes.Count);
        s_w = (int)MathF.Round(PanelWidth * s_scale);
        s_h = (int)MathF.Ceiling((HeaderHeight + n * RowHeight + Math.Max(0, n - 1) + BottomMargin) * s_scale);
    }

    /// Tanılama için: paneli pencere açmadan, yapay bir zemin üstüne çizer.
    public static Gdi.Surface? RenderOffscreen(Config cfg, IReadOnlyList<Quote> quotes, DateTime? updated, float scale, int hover)
    {
        s_cfg = cfg;
        s_quotes = quotes;
        s_updated = updated;
        s_scale = scale;
        s_hover = hover;
        ComputeSize();
        s_backdrop = Backdrop.Synthetic(s_w, s_h, cfg.PanelTintPercent, Theme.IsLight("AppsUseLightTheme"));
        Render();
        return s_surface;
    }

    public static void Update(Config cfg, IReadOnlyList<Quote> quotes, DateTime? updated)
    {
        s_cfg = cfg;
        s_quotes = quotes;
        s_updated = updated;
        if (Visible) Render();
    }

    public static void Hide()
    {
        if (!Visible) return;
        Visible = false;
        Win32.ShowWindow(Handle, Win32.SW_HIDE);
        App.RenderTaskbar();
    }

    public static void Destroy()
    {
        Visible = false;
        if (Handle != 0) Win32.DestroyWindow(Handle);
        Handle = 0;
        s_surface?.Dispose();
        s_surface = null;
    }

    // ------------------------------------------------------------ çizim

    private static void Render()
    {
        var cfg = s_cfg;
        if (cfg == null || s_w <= 0 || s_h <= 0) return;

        float k = s_scale;
        bool light = Theme.IsLight("AppsUseLightTheme");
        // WPF Fluent temasının renkleri
        uint primary = light ? 0xE4000000 : 0xFFFFFFFF;
        uint secondary = light ? 0x9E000000 : 0xC5FFFFFF;
        uint subtle = light ? 0x09000000u : 0x0FFFFFFFu;
        uint divider = light ? 0x0F000000u : 0x15FFFFFFu;
        uint up = light ? Gdi.Argb(255, 0, 140, 80) : Gdi.Argb(255, 61, 214, 140);
        uint down = light ? Gdi.Argb(255, 200, 40, 40) : Gdi.Argb(255, 255, 107, 107);

        var s = new Gdi.Surface(s_w, s_h, 3);
        if (s_backdrop != null && s_backdrop.Length == s_w * s_h) s_backdrop.AsSpan().CopyTo(s.Pixels);
        else s.Pixels.Fill(light ? 0xFFF3F3F3 : 0xFF202024);

        // Camın kenarında ince ışık çizgisi
        s.DrawRounded(0x26FFFFFF, 1, 0.5f, 0.5f, s_w - 1, s_h - 1, 8 * k);

        // Başlık
        s.Text("Piyasalar", FontKind.Bold, 16 * k, primary, 20 * k, 16 * k);
        string status = s_updated.HasValue ? "Güncellendi " + s_updated.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "Yükleniyor…";
        float statusW = Gdi.Width(status, FontKind.Regular, 12 * k);
        s.Text(status, FontKind.Regular, 12 * k, secondary, 360 * k - statusW, (16 + (16 - 12) * Gdi.LineHeight / 2) * k);

        // Satırlar: ikon | ad + etiket | 24 saatlik grafik | fiyat + değişim
        int n = Math.Min(cfg.Items.Count, s_quotes.Count);
        if (s_rowTop.Length != n) s_rowTop = new int[n];
        float y = HeaderHeight;
        Span<PointF> tri = stackalloc PointF[3];
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                s.FillRect(divider, 20 * k, MathF.Round(y * k), 340 * k, MathF.Max(1, MathF.Round(k)));
                y += 1;
            }
            int top = (int)MathF.Round(y * k);
            s_rowTop[i] = top;
            y += RowHeight;

            var item = cfg.Items[i];
            var q = s_quotes[i];
            float op = q.Stale && q.Price.HasValue ? 0.55f : 1f;
            double? change = q.ChangePercent;
            uint trend = Gdi.Fade(q.Stale ? secondary : change is < 0 ? down : up, op);

            if (i == s_hover) s.FillRounded(subtle, 8 * k, top, 364 * k, RowHeight * k, 8 * k);

            float c = top + 11 * k; // satır içeriğinin üstü (40 birim yüksek)

            // Yuvarlak ikon: dış halka + renkli daire + simge
            DrawIcon(s, CoinIcons.For(item), 18 * k, c, k, Gdi.Fade(subtle, op), Gdi.Fade(divider, op), op);

            // Ad + etiket
            string name = string.IsNullOrWhiteSpace(item.Name) ? item.Label : item.Name;
            float nameTop = c + (40 - (14.5f * Gdi.LineHeight + 1 + 12 * Gdi.LineHeight)) / 2 * k;
            s.TextTrimmed(name, FontKind.Bold, 14.5f * k, Gdi.Fade(primary, op), 70 * k, nameTop, 92 * k);
            s.Text(item.Label, FontKind.Regular, 12 * k, Gdi.Fade(secondary, op), 70 * k, nameTop + (14.5f * Gdi.LineHeight + 1) * k);

            // Fiyat + değişim (sağa yaslı)
            string price = q.Price.HasValue
                ? item.Prefix + q.Price.Value.ToString("N" + item.PanelDecimals, CultureInfo.CurrentCulture)
                : "…";
            string pct = change.HasValue ? Math.Abs(change.Value).ToString("0.00", CultureInfo.CurrentCulture) + "%" : "";
            float priceW = Gdi.Width(price, FontKind.Bold, 15 * k);
            float pctW = Gdi.Width(pct, FontKind.SemiBold, 12 * k);
            float triW = 8 * k, triGap = 4.5f * k;
            float changeW = pct.Length > 0 ? triW + triGap + pctW : 0;
            float right = 362 * k;
            float col = Math.Max(92 * k, Math.Max(priceW, changeW));
            float priceTop = c + (40 - (15 * Gdi.LineHeight + 2 + 12 * Gdi.LineHeight)) / 2 * k;
            s.Text(price, FontKind.Bold, 15 * k, Gdi.Fade(primary, op), right - priceW, priceTop);
            if (pct.Length > 0)
            {
                float lineTop = priceTop + (15 * Gdi.LineHeight + 2) * k;
                s.Text(pct, FontKind.SemiBold, 12 * k, trend, right - pctW, lineTop);
                float tx = right - pctW - triGap - triW, cy = lineTop + 12 * Gdi.LineHeight / 2 * k + 0.5f * k, half = 3.25f * k;
                bool isUp = change!.Value >= 0;
                tri[0] = new PointF(tx, isUp ? cy + half : cy - half);
                tri[1] = new PointF(tx + triW, isUp ? cy + half : cy - half);
                tri[2] = new PointF(tx + triW / 2, isUp ? cy - half : cy + half);
                s.FillPolygon(trend, tri);
            }

            // 24 saatlik mini grafik
            float sx = (162 + 4) * k, sw = right - col - 14 * k - sx;
            if (sw > 4 * k) DrawSparkline(s, q.Series, sx, c + 5 * k, sw, 30 * k, trend, k);
        }

        var old = s_surface;
        s_surface = s;
        old?.Dispose();
        if (Handle != 0) Win32.InvalidateRect(Handle, 0, 0);
    }

    private static void DrawIcon(Gdi.Surface s, CoinIcon icon, float x, float y, float k, uint ring, uint ringStroke, float op)
    {
        float d = 40 * k;
        s.FillEllipse(ring, x, y, d, d);
        s.DrawEllipse(ringStroke, MathF.Max(1, k), x + 0.5f * k, y + 0.5f * k, d - k, d - k);
        s.FillEllipseGradient(Gdi.Fade(icon.Color1, op), Gdi.Fade(icon.Color2, op), x + 5 * k, y + 5 * k, 30 * k, 30 * k);

        float cx = x + d / 2, cy = y + d / 2;
        uint white = Gdi.Fade(0xFFFFFFFF, op);
        if (icon.IsEth)
        {
            // Ethereum elması: 24'lük kutudaki çizim, 17 birimlik alana sığacak şekilde ortalanır
            float e = 0.85f * k;
            PointF At(float px, float py) => new(cx + (px - 12) * e, cy + (py - 12) * e);
            Span<PointF> p = stackalloc PointF[4];
            p[0] = At(12, 2); p[1] = At(18.5f, 12.3f); p[2] = At(12, 16.1f); p[3] = At(5.5f, 12.3f);
            s.FillPolygon(white, p);
            p[0] = At(12, 17.5f); p[1] = At(18.5f, 13.7f); p[2] = At(12, 22); p[3] = At(5.5f, 13.7f);
            s.FillPolygon(white, p);
        }
        else
        {
            float size = icon.TextSize * k;
            float w = Gdi.Width(icon.Text, FontKind.Bold, size);
            s.Text(icon.Text, FontKind.Bold, size, white, cx - w / 2, cy - size * Gdi.LineHeight / 2);
        }
    }

    /// Bir değer dizisini çizgi + altı hafif dolgulu mini grafik olarak çizer.
    private static void DrawSparkline(Gdi.Surface s, double[] v, float x, float y, float w, float h, uint color, float k)
    {
        if (v.Length < 2 || w <= 0 || h <= 2) return;
        double min = v[0], max = v[0];
        foreach (double d in v)
        {
            if (d < min) min = d;
            if (d > max) max = d;
        }
        double range = max - min;
        if (range <= 0) range = 1;

        var pts = new PointF[v.Length + 2];
        for (int i = 0; i < v.Length; i++)
            pts[i + 1] = new PointF(x + w * i / (v.Length - 1), y + h - k - (v[i] - min) / range * (h - 2 * k));
        pts[0] = new PointF(x, y + h);
        pts[^1] = new PointF(x + w, y + h);

        int a = (int)(color >> 24);
        s.FillPolygonVertical(Gdi.WithAlpha(color, a * 28 / 255), Gdi.WithAlpha(color, 0), y, y + h, pts);
        s.DrawLines(color, 1.6f * k, pts.AsSpan(1, v.Length));
    }

    // ------------------------------------------------------------ pencere

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    public static nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case Win32.WM_ERASEBKGND:
                    return 1;

                case Win32.WM_PAINT:
                {
                    nint dc = Win32.BeginPaint(hWnd, out var ps);
                    if (s_surface is { } surface)
                    {
                        nint mem = Win32.CreateCompatibleDC(dc);
                        nint old = Win32.SelectObject(mem, surface.Dib);
                        Win32.BitBlt(dc, 0, 0, surface.W, surface.H, mem, 0, 0, Win32.SRCCOPY);
                        Win32.SelectObject(mem, old);
                        Win32.DeleteDC(mem);
                    }
                    Win32.EndPaint(hWnd, ref ps);
                    return 0;
                }

                case Win32.WM_ACTIVATE:
                    if (Win32.LoWord(wParam) == 0 && Visible)
                    {
                        // Başka bir yere tıklandı: kapan
                        ClosedAt = DateTime.UtcNow;
                        Hide();
                    }
                    return 0;

                case Win32.WM_KEYDOWN:
                    if ((int)wParam == Win32.VK_ESCAPE) Hide();
                    return 0;

                case Win32.WM_MOUSEMOVE:
                {
                    if (!s_tracking)
                    {
                        s_tracking = true;
                        var tme = new Win32.TRACKMOUSEEVENT { cbSize = sizeof(Win32.TRACKMOUSEEVENT), dwFlags = Win32.TME_LEAVE, hwndTrack = hWnd };
                        Win32.TrackMouseEvent(ref tme);
                    }
                    int mx = Win32.LoWord(lParam), my = Win32.HiWord(lParam), row = -1;
                    if (mx >= 8 * s_scale && mx < 372 * s_scale)
                    {
                        for (int i = 0; i < s_rowTop.Length; i++)
                            if (my >= s_rowTop[i] && my < s_rowTop[i] + RowHeight * s_scale) row = i;
                    }
                    SetHover(row);
                    return 0;
                }

                case Win32.WM_MOUSELEAVE:
                    s_tracking = false;
                    SetHover(-1);
                    return 0;

                case Win32.WM_NCDESTROY:
                    if (hWnd == Handle) Handle = 0;
                    break;
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void SetHover(int row)
    {
        if (row == s_hover) return;
        s_hover = row;
        if (Visible) Render();
    }
}

/// Panelin buzlu cam zemini: ekran bölgesini yakalar, bulanıklaştırır ve üstüne ton koyar.
internal static unsafe class Backdrop
{
    /// Sonuç: w×h, opak (0xFFRRGGBB) pikseller. Yakalama başarısız olursa null.
    public static uint[]? Make(int left, int top, int w, int h, float scale, int tintPercent, bool light)
    {
        int pad = (int)MathF.Round(48 * scale); // bulanıklığın kenarda solmaması için taşma payı
        int cw = w + 2 * pad, ch = h + 2 * pad;
        var px = Capture(left - pad, top - pad, cw, ch);
        if (px == null) return null;

        // Üç geçişli kutu bulanıklığı, WPF'teki 36 yarıçaplı Gauss bulanıklığına (σ ≈ 12) denk gelir.
        int radius = Math.Max(1, (int)MathF.Round(12 * scale));
        var tmp = new uint[px.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlur(px, tmp, cw, ch, radius, horizontal: true);
            BoxBlur(tmp, px, cw, ch, radius, horizontal: false);
        }

        // Ortayı kırp ve tonla karıştır
        var result = new uint[w * h];
        for (int y = 0; y < h; y++)
        {
            int src = (y + pad) * cw + pad, dst = y * w;
            for (int x = 0; x < w; x++) result[dst + x] = Tint(px[src + x], tintPercent, light);
        }
        return result;
    }

    /// Tanılama için yapay zemin: gökyüzü mavisinden çimen yeşiline yumuşak bir geçiş + ton.
    public static uint[] Synthetic(int w, int h, int tintPercent, bool light)
    {
        var result = new uint[w * h];
        for (int y = 0; y < h; y++)
        {
            float t = (float)y / Math.Max(1, h - 1);
            int r = (int)(150 + (95 - 150) * t), g = (int)(195 + (140 - 195) * t), b = (int)(235 + (70 - 235) * t);
            uint p = Tint(Gdi.Argb(255, r, g, b), tintPercent, light);
            result.AsSpan(y * w, w).Fill(p);
        }
        return result;
    }

    private static uint Tint(uint p, int tintPercent, bool light)
    {
        int a = tintPercent * 255 / 100, ia = 255 - a;
        int tr = light ? 0xF7 : 0x16, tg = light ? 0xF7 : 0x16, tb = light ? 0xF9 : 0x1C;
        int r = (int)((p >> 16) & 255), g = (int)((p >> 8) & 255), b = (int)(p & 255);
        r = (r * ia + tr * a + 127) / 255;
        g = (g * ia + tg * a + 127) / 255;
        b = (b * ia + tb * a + 127) / 255;
        return 0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
    }

    private static uint[]? Capture(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0) return null;
        var bmi = new Win32.BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        nint screen = Win32.GetDC(0);
        nint mem = Win32.CreateCompatibleDC(screen);
        nint dib = Win32.CreateDIBSection(screen, ref bmi, 0, out nint bits, 0, 0);
        try
        {
            if (dib == 0 || bits == 0) return null;
            nint old = Win32.SelectObject(mem, dib);
            int ok = Win32.BitBlt(mem, 0, 0, w, h, screen, x, y, Win32.SRCCOPY | Win32.CAPTUREBLT);
            Win32.SelectObject(mem, old);
            if (ok == 0) return null;
            var result = new uint[w * h];
            new ReadOnlySpan<uint>((void*)bits, w * h).CopyTo(result);
            return result;
        }
        finally
        {
            if (dib != 0) Win32.DeleteObject(dib);
            Win32.DeleteDC(mem);
            Win32.ReleaseDC(0, screen);
        }
    }

    /// Tek yönde kutu bulanıklığı (kayan toplam); kenarlarda en yakın piksel tekrarlanır.
    private static void BoxBlur(uint[] src, uint[] dst, int w, int h, int r, bool horizontal)
    {
        int len = horizontal ? w : h, lines = horizontal ? h : w, step = horizontal ? 1 : w;
        int div = 2 * r + 1;
        for (int line = 0; line < lines; line++)
        {
            int start = horizontal ? line * w : line;
            int sr = 0, sg = 0, sb = 0;
            for (int i = -r; i <= r; i++)
            {
                uint p = src[start + Math.Clamp(i, 0, len - 1) * step];
                sr += (int)((p >> 16) & 255);
                sg += (int)((p >> 8) & 255);
                sb += (int)(p & 255);
            }
            for (int i = 0; i < len; i++)
            {
                dst[start + i * step] = 0xFF000000 | ((uint)(sr / div) << 16) | ((uint)(sg / div) << 8) | (uint)(sb / div);
                uint add = src[start + Math.Min(i + r + 1, len - 1) * step];
                uint sub = src[start + Math.Max(i - r, 0) * step];
                sr += (int)((add >> 16) & 255) - (int)((sub >> 16) & 255);
                sg += (int)((add >> 8) & 255) - (int)((sub >> 8) & 255);
                sb += (int)(add & 255) - (int)(sub & 255);
            }
        }
    }
}

/// Paneldeki yuvarlak kalem ikonu: renkli daire + simge. Logo dosyası kullanılmaz; hepsi kodla çizilir.
internal readonly record struct CoinIcon(uint Color1, uint Color2, string Text, float TextSize, bool IsEth);

internal static class CoinIcons
{
    public static CoinIcon For(TickerItem item)
    {
        string key = (item.Label + " " + item.Symbol).ToUpperInvariant();

        if (key.Contains("BTC")) return new(0xFFF7931A, 0xFFF7931A, "₿", 17, false);
        if (key.Contains("ETH")) return new(0xFF8AA0F5, 0xFF5A6FD8, "", 0, true);
        if (key.Contains("GC=F") || key.Contains("ALTIN") || key.Contains("GOLD") || key.Contains("XAU"))
            return new(0xFFF6D365, 0xFFC8921E, "Au", 12.5f, false);
        if (key.Contains("EUR")) return new(0xFF4A7BF0, 0xFF2A4FC0, "€", 16, false);
        if (key.Contains("USD")) return new(0xFF3CC07A, 0xFF1E8C55, "$", 16, false);
        if (key.Contains("GBP")) return new(0xFFA25BE8, 0xFF6E34B8, "£", 16, false);

        // Bilinmeyen kalem: etiketten türetilen sabit bir renk + baş harf
        string label = item.Label.Trim();
        string text = label.Length == 0 ? "?" : label[..1].ToUpperInvariant();
        uint hash = 2166136261;
        foreach (char c in label) hash = (hash ^ c) * 16777619;
        uint color = FromHsl(hash % 360, 0.55, 0.5);
        return new(color, color, text, 15, false);
    }

    private static uint FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Gdi.Argb(255, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
