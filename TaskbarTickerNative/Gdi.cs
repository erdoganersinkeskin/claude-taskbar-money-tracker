using System.Runtime.InteropServices;

namespace TaskbarTickerNative;

[StructLayout(LayoutKind.Sequential)]
internal struct PointF
{
    public float X, Y;
    public PointF(float x, float y) { X = x; Y = y; }
    public PointF(double x, double y) { X = (float)x; Y = (float)y; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct RectF { public float X, Y, W, H; }

internal enum FontKind { Regular, SemiBold, Bold }

/// GDI+ (Windows'un yerleşik 2B çizim kitaplığı) üzerine ince bir katman.
internal static unsafe class Gdi
{
    private const string P = "gdiplus.dll";

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInput { public uint Version; public nint Callback; public int NoThread, NoCodecs; }

    [DllImport(P)] private static extern int GdiplusStartup(out nint token, ref StartupInput input, nint output);
    [DllImport(P)] private static extern int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, nint scan0, out nint bitmap);
    [DllImport(P)] private static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);
    [DllImport(P)] private static extern int GdipDeleteGraphics(nint g);
    [DllImport(P)] private static extern int GdipDisposeImage(nint image);
    [DllImport(P)] private static extern int GdipSetSmoothingMode(nint g, int mode);
    [DllImport(P)] private static extern int GdipSetPixelOffsetMode(nint g, int mode);
    [DllImport(P)] private static extern int GdipSetTextRenderingHint(nint g, int hint);
    [DllImport(P)] private static extern int GdipCreateSolidFill(uint argb, out nint brush);
    [DllImport(P)] private static extern int GdipCreateLineBrush(ref PointF a, ref PointF b, uint c1, uint c2, int wrap, out nint brush);
    [DllImport(P)] private static extern int GdipDeleteBrush(nint brush);
    [DllImport(P)] private static extern int GdipCreatePen1(uint argb, float width, int unit, out nint pen);
    [DllImport(P)] private static extern int GdipSetPenLineJoin(nint pen, int join);
    [DllImport(P)] private static extern int GdipDeletePen(nint pen);
    [DllImport(P)] private static extern int GdipFillEllipse(nint g, nint brush, float x, float y, float w, float h);
    [DllImport(P)] private static extern int GdipDrawEllipse(nint g, nint pen, float x, float y, float w, float h);
    [DllImport(P)] private static extern int GdipFillRectangle(nint g, nint brush, float x, float y, float w, float h);
    [DllImport(P)] private static extern int GdipCreatePath(int fillMode, out nint path);
    [DllImport(P)] private static extern int GdipDeletePath(nint path);
    [DllImport(P)] private static extern int GdipAddPathArc(nint path, float x, float y, float w, float h, float start, float sweep);
    [DllImport(P)] private static extern int GdipClosePathFigure(nint path);
    [DllImport(P)] private static extern int GdipFillPath(nint g, nint brush, nint path);
    [DllImport(P)] private static extern int GdipDrawPath(nint g, nint pen, nint path);
    [DllImport(P)] private static extern int GdipFillPolygon(nint g, nint brush, PointF* points, int count, int fillMode);
    [DllImport(P)] private static extern int GdipDrawLines(nint g, nint pen, PointF* points, int count);
    [DllImport(P, CharSet = CharSet.Unicode)] private static extern int GdipCreateFontFamilyFromName(string name, nint collection, out nint family);
    [DllImport(P)] private static extern int GdipCreateFont(nint family, float size, int style, int unit, out nint font);
    [DllImport(P)] private static extern int GdipStringFormatGetGenericTypographic(out nint format);
    [DllImport(P)] private static extern int GdipCloneStringFormat(nint format, out nint clone);
    [DllImport(P)] private static extern int GdipSetStringFormatFlags(nint format, int flags);
    [DllImport(P)] private static extern int GdipSetStringFormatTrimming(nint format, int trimming);
    [DllImport(P)] private static extern int GdipDrawString(nint g, char* text, int length, nint font, ref RectF layout, nint format, nint brush);
    [DllImport(P)]
    private static extern int GdipMeasureString(nint g, char* text, int length, nint font, ref RectF layout, nint format,
        out RectF bounds, out int chars, out int lines);

    private const int PixelFormat32bppPARGB = 0x000E200B, UnitPixel = 2;

    /// Segoe UI'nin satır yüksekliği (em'e oranı); WPF'teki TextBlock yüksekliğiyle aynıdır.
    public const float LineHeight = 1.33008f;

    private static nint s_format, s_formatEllipsis, s_measure, s_measureBitmap;
    private static readonly Dictionary<(FontKind, int), nint> s_fonts = new();
    private static readonly nint[] s_families = new nint[3];

    public static void Startup()
    {
        var input = new StartupInput { Version = 1 };
        GdiplusStartup(out _, ref input, 0);

        // Sıkı ölçüm (kenar boşluğu yok) + tek satır
        GdipStringFormatGetGenericTypographic(out nint generic);
        GdipCloneStringFormat(generic, out s_format);
        GdipSetStringFormatFlags(s_format, 0x7004); // NoFitBlackBox | NoWrap | LineLimit | NoClip
        GdipCloneStringFormat(s_format, out s_formatEllipsis);
        GdipSetStringFormatFlags(s_formatEllipsis, 0x1004); // NoFitBlackBox | NoWrap (kırpma açık)
        GdipSetStringFormatTrimming(s_formatEllipsis, 3); // EllipsisCharacter

        GdipCreateBitmapFromScan0(1, 1, 0, PixelFormat32bppPARGB, 0, out s_measureBitmap);
        GdipGetImageGraphicsContext(s_measureBitmap, out s_measure);
        GdipSetTextRenderingHint(s_measure, 4);

        GdipCreateFontFamilyFromName("Segoe UI", 0, out s_families[0]);
        GdipCreateFontFamilyFromName("Segoe UI Semibold", 0, out s_families[1]);
        if (s_families[1] == 0) s_families[1] = s_families[0];
        s_families[2] = s_families[0];
    }

    private static nint Font(FontKind kind, float sizePx)
    {
        var key = (kind, (int)MathF.Round(sizePx * 16));
        if (!s_fonts.TryGetValue(key, out nint font))
        {
            GdipCreateFont(s_families[(int)kind], sizePx, kind == FontKind.Bold ? 1 : 0, UnitPixel, out font);
            s_fonts[key] = font;
        }
        return font;
    }

    public static float Width(string text, FontKind kind, float sizePx)
    {
        if (text.Length == 0) return 0;
        var layout = new RectF { W = 10000, H = 1000 };
        RectF box;
        fixed (char* p = text)
            GdipMeasureString(s_measure, p, text.Length, Font(kind, sizePx), ref layout, s_format, out box, out _, out _);
        return box.W;
    }

    public static uint Argb(int a, int r, int g, int b) => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;

    /// Rengin alfasını değiştirir (0..255).
    public static uint WithAlpha(uint rgb, int a) => (rgb & 0x00FFFFFF) | ((uint)Math.Clamp(a, 0, 255) << 24);

    /// Rengin mevcut alfasını bir opaklık çarpanıyla ölçekler.
    public static uint Fade(uint argb, float opacity) =>
        opacity >= 1 ? argb : WithAlpha(argb, (int)MathF.Round((argb >> 24) * opacity));

    /// 32 bit, yukarıdan aşağıya, ön çarpımlı alfa içeren bir çizim yüzeyi (DIB) ve onun GDI+ bağlamı.
    public sealed class Surface : IDisposable
    {
        public readonly int W, H;
        public readonly nint Dib;
        public readonly uint* Bits;
        private nint _bitmap, _g;

        /// textHint: 3 = kenar yumuşatma + piksel ızgarasına oturtma (küçük yazı), 4 = yalnızca kenar yumuşatma
        public Surface(int w, int h, int textHint)
        {
            W = Math.Max(1, w);
            H = Math.Max(1, h);
            var bmi = new Win32.BITMAPINFOHEADER { biSize = 40, biWidth = W, biHeight = -H, biPlanes = 1, biBitCount = 32 };
            Dib = Win32.CreateDIBSection(0, ref bmi, 0, out nint bits, 0, 0);
            Bits = (uint*)bits;
            if (Dib == 0 || bits == 0) throw new InvalidOperationException("CreateDIBSection");
            new Span<uint>(Bits, W * H).Clear();

            GdipCreateBitmapFromScan0(W, H, W * 4, PixelFormat32bppPARGB, bits, out _bitmap);
            GdipGetImageGraphicsContext(_bitmap, out _g);
            GdipSetSmoothingMode(_g, 4);   // AntiAlias
            GdipSetPixelOffsetMode(_g, 4); // Half: koordinatlar piksel kenarına göre (WPF ile aynı)
            GdipSetTextRenderingHint(_g, textHint);
        }

        public Span<uint> Pixels => new(Bits, W * H);

        public void FillRect(uint color, float x, float y, float w, float h)
        {
            GdipCreateSolidFill(color, out nint b);
            GdipFillRectangle(_g, b, x, y, w, h);
            GdipDeleteBrush(b);
        }

        public void FillEllipse(uint color, float x, float y, float w, float h)
        {
            GdipCreateSolidFill(color, out nint b);
            GdipFillEllipse(_g, b, x, y, w, h);
            GdipDeleteBrush(b);
        }

        /// Sol üstten sağ alta doğrusal geçişli daire.
        public void FillEllipseGradient(uint c1, uint c2, float x, float y, float w, float h)
        {
            var a = new PointF(x, y);
            var z = new PointF(x + w, y + h);
            GdipCreateLineBrush(ref a, ref z, c1, c2, 3, out nint b);
            GdipFillEllipse(_g, b, x, y, w, h);
            GdipDeleteBrush(b);
        }

        public void DrawEllipse(uint color, float width, float x, float y, float w, float h)
        {
            GdipCreatePen1(color, width, UnitPixel, out nint pen);
            GdipDrawEllipse(_g, pen, x, y, w, h);
            GdipDeletePen(pen);
        }

        private static nint RoundedPath(float x, float y, float w, float h, float r)
        {
            float d = Math.Min(r * 2, Math.Min(w, h));
            GdipCreatePath(0, out nint path);
            GdipAddPathArc(path, x, y, d, d, 180, 90);
            GdipAddPathArc(path, x + w - d, y, d, d, 270, 90);
            GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0, 90);
            GdipAddPathArc(path, x, y + h - d, d, d, 90, 90);
            GdipClosePathFigure(path);
            return path;
        }

        public void FillRounded(uint color, float x, float y, float w, float h, float r)
        {
            nint path = RoundedPath(x, y, w, h, r);
            GdipCreateSolidFill(color, out nint b);
            GdipFillPath(_g, b, path);
            GdipDeleteBrush(b);
            GdipDeletePath(path);
        }

        public void DrawRounded(uint color, float width, float x, float y, float w, float h, float r)
        {
            nint path = RoundedPath(x, y, w, h, r);
            GdipCreatePen1(color, width, UnitPixel, out nint pen);
            GdipDrawPath(_g, pen, path);
            GdipDeletePen(pen);
            GdipDeletePath(path);
        }

        public void FillPolygon(uint color, ReadOnlySpan<PointF> points)
        {
            GdipCreateSolidFill(color, out nint b);
            fixed (PointF* p = points) GdipFillPolygon(_g, b, p, points.Length, 0);
            GdipDeleteBrush(b);
        }

        /// Çokgeni yukarıdan aşağıya doğrusal geçişle doldurur.
        public void FillPolygonVertical(uint top, uint bottom, float y0, float y1, ReadOnlySpan<PointF> points)
        {
            var a = new PointF(0, y0 - 1);
            var z = new PointF(0, y1 + 1);
            GdipCreateLineBrush(ref a, ref z, top, bottom, 3, out nint b);
            fixed (PointF* p = points) GdipFillPolygon(_g, b, p, points.Length, 0);
            GdipDeleteBrush(b);
        }

        public void DrawLines(uint color, float width, ReadOnlySpan<PointF> points)
        {
            GdipCreatePen1(color, width, UnitPixel, out nint pen);
            GdipSetPenLineJoin(pen, 2); // Round
            fixed (PointF* p = points) GdipDrawLines(_g, pen, p, points.Length);
            GdipDeletePen(pen);
        }

        public void Text(string text, FontKind kind, float sizePx, uint color, float x, float y)
        {
            if (text.Length == 0) return;
            var layout = new RectF { X = x, Y = y };
            GdipCreateSolidFill(color, out nint b);
            fixed (char* p = text) GdipDrawString(_g, p, text.Length, Font(kind, sizePx), ref layout, s_format, b);
            GdipDeleteBrush(b);
        }

        /// Verilen genişliğe sığmazsa sonuna "…" koyarak yazar.
        public void TextTrimmed(string text, FontKind kind, float sizePx, uint color, float x, float y, float maxWidth)
        {
            if (text.Length == 0) return;
            var layout = new RectF { X = x, Y = y, W = maxWidth, H = sizePx * LineHeight + 2 };
            GdipCreateSolidFill(color, out nint b);
            fixed (char* p = text) GdipDrawString(_g, p, text.Length, Font(kind, sizePx), ref layout, s_formatEllipsis, b);
            GdipDeleteBrush(b);
        }

        /// Tanılama için: yüzeyi verilen opak zemin rengi üstüne bindirip 32 bit .bmp olarak yazar.
        public void SaveBmp(string path, uint background)
        {
            int br = (int)((background >> 16) & 255), bg = (int)((background >> 8) & 255), bb = (int)(background & 255);
            var data = new byte[54 + W * H * 4];
            var span = data.AsSpan();
            span[0] = (byte)66; // "BM"
            span[1] = (byte)77;
            BitConverter.TryWriteBytes(span[2..], data.Length);
            BitConverter.TryWriteBytes(span[10..], 54);
            BitConverter.TryWriteBytes(span[14..], 40);
            BitConverter.TryWriteBytes(span[18..], W);
            BitConverter.TryWriteBytes(span[22..], -H); // yukarıdan aşağıya
            BitConverter.TryWriteBytes(span[26..], (short)1);
            BitConverter.TryWriteBytes(span[28..], (short)32);
            for (int i = 0; i < W * H; i++)
            {
                // Pikseller ön çarpımlı alfa içerir: sonuç = piksel + zemin × (1 − alfa)
                uint p = Bits[i];
                int ia = 255 - (int)(p >> 24);
                int o = 54 + i * 4;
                data[o] = (byte)Math.Min(255, (int)(p & 255) + bb * ia / 255);
                data[o + 1] = (byte)Math.Min(255, (int)((p >> 8) & 255) + bg * ia / 255);
                data[o + 2] = (byte)Math.Min(255, (int)((p >> 16) & 255) + br * ia / 255);
                data[o + 3] = 255;
            }
            File.WriteAllBytes(path, data);
        }

        public void Dispose()
        {
            if (_g != 0) GdipDeleteGraphics(_g);
            if (_bitmap != 0) GdipDisposeImage(_bitmap);
            if (Dib != 0) Win32.DeleteObject(Dib);
            _g = _bitmap = 0;
        }
    }
}
