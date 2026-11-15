using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Imaging;

namespace TaskbarTicker;

/// Görev çubuğunun (Shell_TrayWnd) içine gömülen saydam, katmanlı alt pencere.
/// İçeriği dışarıdan hazır bir bitmap olarak alır; fare olaylarını olay olarak bildirir.
internal sealed class TaskbarHost
{
    private const string ClassName = "TaskbarTickerHost";

    private static TaskbarHost? s_instance;
    private static readonly Native.WndProc s_wndProc = StaticWndProc; // GC toplamasın diye statik tutulur
    private static bool s_registered;

    private bool _pressed, _dragging;
    private int _dragCursorX, _dragStartX;

    public IntPtr Handle { get; private set; }
    public bool IsCreated => Handle != IntPtr.Zero;
    public float Scale { get; private set; } = 1f;
    public int X { get; private set; }
    public int Y { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int ParentWidth { get; private set; }
    public Native.RECT TrayRect { get; private set; }
    public bool Hover { get; private set; }
    public bool IsDragging => _dragging;

    public event Action? Clicked;
    public event Action? RightClicked;
    public event Action? DragStarted;
    public event Action<int>? Moved;      // sürükleme bitti: yeni X
    public event Action? HoverChanged;

    public TaskbarHost() => s_instance = this;

    /// Görev çubuğu hazırsa pencereyi oluşturur. Yeni oluşturulduysa true döner.
    public bool TryCreate(int? offsetX)
    {
        if (IsCreated) return false;
        IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return false;

        IntPtr instance = Native.GetModuleHandle(null);
        if (!s_registered)
        {
            var wc = new Native.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<Native.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
                hInstance = instance,
                hCursor = Native.LoadCursor(IntPtr.Zero, Native.IDC_ARROW),
                lpszClassName = ClassName,
            };
            if (Native.RegisterClassEx(ref wc) == 0) return false;
            s_registered = true;
        }

        Scale = Math.Max(1f, Native.GetDpiForWindow(tray) / 96f);
        RefreshTrayRect(tray);
        Width = 1; // gerçek genişlik ilk Present çağrısında belli olur
        Height = TrayRect.Bottom - TrayRect.Top - (int)(2 * Scale);
        Y = (int)(1 * Scale);
        X = Math.Clamp(offsetX ?? AutoX(tray), 0, Math.Max(0, ParentWidth - Width));

        Handle = Native.CreateWindowEx(
            Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW,
            ClassName, "TaskbarTicker",
            Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPSIBLINGS,
            X, Y, Width, Height, tray, IntPtr.Zero, instance, IntPtr.Zero);
        return IsCreated;
    }

    public void RefreshTrayRect(IntPtr tray = default)
    {
        if (tray == IntPtr.Zero) tray = Native.FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return;
        Native.GetWindowRect(tray, out var r);
        TrayRect = r;
        ParentWidth = r.Right - r.Left;
    }

    /// Görev çubuğunun soluna gömülü başka araçlar varsa (ör. sistem monitörü) hemen sağına yerleş.
    private int AutoX(IntPtr tray)
    {
        string[] shell =
        {
            "Windows.UI.Composition.DesktopWindowContentBridge", "Windows.UI.Core.CoreWindow", "Start",
            "TrayDummySearchControl", "TrayNotifyWnd", "ReBarWindow32", "MSTaskSwWClass",
        };
        int right = 0;
        IntPtr child = IntPtr.Zero;
        var name = new StringBuilder(256);
        while ((child = Native.FindWindowEx(tray, child, null, null)) != IntPtr.Zero)
        {
            if (!Native.IsWindowVisible(child)) continue;
            name.Clear();
            Native.GetClassName(child, name, name.Capacity);
            if (shell.Contains(name.ToString())) continue;
            Native.GetWindowRect(child, out var r);
            int width = r.Right - r.Left;
            if (width <= 0 || width > ParentWidth / 2 || r.Left - TrayRect.Left > ParentWidth / 3) continue;
            right = Math.Max(right, r.Right - TrayRect.Left);
        }
        return right > 0 ? right + (int)(2 * Scale) : (int)(8 * Scale);
    }

    /// Görev çubuğunun kendi içeriğinin üstünde kal.
    public void KeepOnTop()
    {
        if (!IsCreated || _dragging) return;
        Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// İçeriği (ön çarpımlı BGRA bitmap) pencereye basar; genişlik değiştiyse pencereyi yeniden boyutlandırır.
    public void Present(BitmapSource bmp)
    {
        if (!IsCreated) return;
        int w = bmp.PixelWidth, h = bmp.PixelHeight, stride = w * 4;
        var pixels = new byte[stride * h];
        bmp.CopyPixels(pixels, stride, 0);

        if (w != Width || h != Height)
        {
            Width = w;
            Height = h;
            X = Math.Clamp(X, 0, Math.Max(0, ParentWidth - Width));
            Native.SetWindowPos(Handle, IntPtr.Zero, X, Y, Width, Height,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        var bmi = new Native.BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        IntPtr screen = Native.GetDC(IntPtr.Zero);
        IntPtr mem = Native.CreateCompatibleDC(screen);
        IntPtr dib = Native.CreateDIBSection(screen, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
        try
        {
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return;
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            IntPtr old = Native.SelectObject(mem, dib);
            var size = new Native.SIZE { cx = w, cy = h };
            var src = new Native.POINT();
            var blend = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            Native.UpdateLayeredWindow(Handle, screen, IntPtr.Zero, ref size, mem, ref src, 0, ref blend, 2);
            Native.SelectObject(mem, old);
        }
        finally
        {
            if (dib != IntPtr.Zero) Native.DeleteObject(dib);
            Native.DeleteDC(mem);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public void Destroy()
    {
        if (IsCreated) Native.DestroyWindow(Handle);
        Handle = IntPtr.Zero;
    }

    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var host = s_instance;
        if (host != null)
        {
            try
            {
                if (host.OnMessage(hWnd, msg, out IntPtr result)) return result;
            }
            catch (Exception ex)
            {
                // Yerel koda istisna sızdırma; ama sessizce de yutma: exe'nin yanına yaz.
                try
                {
                    System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "error.log"),
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}{Environment.NewLine}");
                }
                catch { }
            }
        }
        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private bool OnMessage(IntPtr hWnd, uint msg, out IntPtr result)
    {
        result = IntPtr.Zero;
        switch (msg)
        {
            case Native.WM_MOUSEACTIVATE:
                result = Native.MA_NOACTIVATE;
                return true;

            case Native.WM_LBUTTONDOWN:
                Native.GetCursorPos(out var down);
                _pressed = true;
                _dragging = false;
                _dragCursorX = down.x;
                _dragStartX = X;
                Native.SetCapture(hWnd);
                return true;

            case Native.WM_MOUSEMOVE:
                if (!Hover)
                {
                    Hover = true;
                    var tme = new Native.TRACKMOUSEEVENT
                    {
                        cbSize = Marshal.SizeOf<Native.TRACKMOUSEEVENT>(), dwFlags = Native.TME_LEAVE, hwndTrack = hWnd,
                    };
                    Native.TrackMouseEvent(ref tme);
                    HoverChanged?.Invoke();
                }
                if (_pressed)
                {
                    Native.GetCursorPos(out var cur);
                    int delta = cur.x - _dragCursorX;
                    if (!_dragging && Math.Abs(delta) >= (int)(4 * Scale))
                    {
                        _dragging = true;
                        DragStarted?.Invoke();
                    }
                    if (_dragging)
                    {
                        int nx = Math.Clamp(_dragStartX + delta, 0, Math.Max(0, ParentWidth - Width));
                        if (nx != X)
                        {
                            X = nx;
                            Native.SetWindowPos(hWnd, IntPtr.Zero, X, Y, 0, 0,
                                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                        }
                    }
                }
                return true;

            case Native.WM_MOUSELEAVE:
                Hover = false;
                HoverChanged?.Invoke();
                return true;

            case Native.WM_LBUTTONUP:
                if (_pressed)
                {
                    _pressed = false;
                    Native.ReleaseCapture();
                    if (_dragging)
                    {
                        _dragging = false;
                        Moved?.Invoke(X);
                    }
                    else
                    {
                        Clicked?.Invoke();
                    }
                }
                return true;

            case Native.WM_RBUTTONUP:
                RightClicked?.Invoke();
                return true;

            case Native.WM_NCDESTROY:
                // Explorer yeniden başlarsa görev çubuğuyla birlikte bu pencere de yok edilir.
                if (hWnd == Handle) Handle = IntPtr.Zero;
                _pressed = _dragging = Hover = false;
                return false;
        }
        return false;
    }
}
