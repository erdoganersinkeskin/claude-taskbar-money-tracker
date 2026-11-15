using System.Windows;
using System.Windows.Media;

namespace TaskbarTicker;

/// Paneldeki yuvarlak kalem ikonu: renkli daire + simge (metin ya da vektör çizim).
/// Dışarıdan logo dosyası kullanılmaz; hepsi burada çizilir.
public sealed record CoinIcon(Brush Fill, string Text, Geometry? Shape, double TextSize);

public static class CoinIcons
{
    // Ethereum elması (üst ve alt yarı), 0..24 kutusunda
    private static readonly Geometry EthShape = Freeze(Geometry.Parse(
        "M12,2 L18.5,12.3 L12,16.1 L5.5,12.3 Z M12,17.5 L18.5,13.7 L12,22 L5.5,13.7 Z"));

    public static CoinIcon For(TickerItem item)
    {
        string key = (item.Label + " " + item.Symbol).ToUpperInvariant();

        if (key.Contains("BTC")) return Text(Solid(0xF7, 0x93, 0x1A), "₿", 17);
        if (key.Contains("ETH")) return new CoinIcon(Gradient(0x8A, 0xA0, 0xF5, 0x5A, 0x6F, 0xD8), "", EthShape, 0);
        if (key.Contains("GC=F") || key.Contains("ALTIN") || key.Contains("GOLD") || key.Contains("XAU"))
            return Text(Gradient(0xF6, 0xD3, 0x65, 0xC8, 0x92, 0x1E), "Au", 12.5);
        if (key.Contains("EUR")) return Text(Gradient(0x4A, 0x7B, 0xF0, 0x2A, 0x4F, 0xC0), "€", 16);
        if (key.Contains("USD")) return Text(Gradient(0x3C, 0xC0, 0x7A, 0x1E, 0x8C, 0x55), "$", 16);
        if (key.Contains("GBP")) return Text(Gradient(0xA2, 0x5B, 0xE8, 0x6E, 0x34, 0xB8), "£", 16);

        // Bilinmeyen kalem: etiketten türetilen sabit bir renk + baş harf
        string label = item.Label.Trim();
        string text = label.Length == 0 ? "?" : label[..1].ToUpperInvariant();
        uint hash = 2166136261;
        foreach (char c in label) hash = (hash ^ c) * 16777619;
        double hue = hash % 360;
        return Text(new SolidColorBrush(FromHsl(hue, 0.55, 0.5)).Frozen(), text, 15);
    }

    private static CoinIcon Text(Brush fill, string text, double size) => new(fill, text, null, size);

    private static Brush Solid(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b)).Frozen();

    private static Brush Gradient(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2) =>
        new LinearGradientBrush(Color.FromRgb(r1, g1, b1), Color.FromRgb(r2, g2, b2), new Point(0, 0), new Point(1, 1)).Frozen();

    private static T Frozen<T>(this T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    private static Geometry Freeze(Geometry g)
    {
        g.Freeze();
        return g;
    }

    private static Color FromHsl(double h, double s, double l)
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
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
