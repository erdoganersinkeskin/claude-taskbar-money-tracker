using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace TaskbarTicker;

/// Ekranın bir bölgesini (aygıt pikseli) WPF görüntüsü olarak yakalar.
public static class ScreenCapture
{
    private const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

    public static BitmapSource? Grab(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        IntPtr screen = Native.GetDC(IntPtr.Zero);
        IntPtr mem = Native.CreateCompatibleDC(screen);
        IntPtr bmp = Native.CreateCompatibleBitmap(screen, width, height);
        IntPtr old = Native.SelectObject(mem, bmp);
        try
        {
            if (!Native.BitBlt(mem, 0, 0, width, height, screen, x, y, SRCCOPY | CAPTUREBLT)) return null;
            Native.SelectObject(mem, old);
            old = IntPtr.Zero;

            var source = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (old != IntPtr.Zero) Native.SelectObject(mem, old);
            Native.DeleteObject(bmp);
            Native.DeleteDC(mem);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
