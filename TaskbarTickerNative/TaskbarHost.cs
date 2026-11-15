using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TaskbarTickerNative;

/// Görev çubuğunun (Shell_TrayWnd) içine gömülen saydam, katmanlı alt pencere.
/// İçeriği dışarıdan hazır bir yüzey olarak alır; fare olaylarını App'e bildirir.
internal static unsafe class TaskbarHost
{
    public const string ClassName = "TaskbarTickerNativeHost";

    private static bool s_pressed, s_dragging;
    private static int s_dragCursorX, s_dragStartX;

    public static nint Handle { get; private set; }
    public static bool IsCreated => Handle != 0;
    public static float Scale { get; private set; } = 1f;
    public static int X { get; private set; }
    public static int Y { get; private set; }
    public static int Width { get; private set; }
    public static int Height { get; private set; }
    public static int ParentWidth { get; private set; }
    public static Win32.RECT TrayRect { get; private set; }
    public static bool Hover { get; private set; }

    /// Görev çubuğu hazırsa pencereyi oluşturur. Yeni oluşturulduysa true döner.
    public static bool TryCreate(int? offsetX)
    {
        if (IsCreated) return false;
        nint tray = Win32.FindWindowW("Shell_TrayWnd", null);
        if (tray == 0) return false;

        Scale = Math.Max(1f, Win32.GetDpiForWindow(tray) / 96f);
        RefreshTrayRect(tray);
        Width = 1; // gerçek genişlik ilk Present çağrısında belli olur
        Height = TrayRect.Bottom - TrayRect.Top - (int)(2 * Scale);
        Y = (int)(1 * Scale);
        X = Math.Clamp(offsetX ?? AutoX(tray), 0, Math.Max(0, ParentWidth - Width));

        Handle = Win32.CreateWindowExW(
            Win32.WS_EX_LAYERED | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW,
            ClassName, "TaskbarTickerNative",
            Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPSIBLINGS,
            X, Y, Width, Height, tray, 0, Win32.GetModuleHandleW(0), 0);
        return IsCreated;
    }

    public static void RefreshTrayRect(nint tray = 0)
    {
        if (tray == 0) tray = Win32.FindWindowW("Shell_TrayWnd", null);
        if (tray == 0) return;
        Win32.GetWindowRect(tray, out var r);
        TrayRect = r;
        ParentWidth = r.Right - r.Left;
    }

    /// Görev çubuğunun soluna gömülü başka araçlar varsa (ör. sistem monitörü) hemen sağına yerleş.
    private static int AutoX(nint tray)
    {
        string[] shell =
        {
            "Windows.UI.Composition.DesktopWindowContentBridge", "Windows.UI.Core.CoreWindow", "Start",
            "TrayDummySearchControl", "TrayNotifyWnd", "ReBarWindow32", "MSTaskSwWClass",
        };
        int right = 0;
        nint child = 0;
        char* name = stackalloc char[256];
        while ((child = Win32.FindWindowExW(tray, child, null, null)) != 0)
        {
            if (Win32.IsWindowVisible(child) == 0) continue;
            int len = Win32.GetClassNameW(child, name, 256);
            if (Array.IndexOf(shell, new string(name, 0, len)) >= 0) continue;
            Win32.GetWindowRect(child, out var r);
            int width = r.Right - r.Left;
            if (width <= 0 || width > ParentWidth / 2 || r.Left - TrayRect.Left > ParentWidth / 3) continue;
            right = Math.Max(right, r.Right - TrayRect.Left);
        }
        return right > 0 ? right + (int)(2 * Scale) : (int)(8 * Scale);
    }

    /// Görev çubuğunun kendi içeriğinin üstünde kal.
    public static void KeepOnTop()
    {
        if (!IsCreated || s_dragging) return;
        Win32.SetWindowPos(Handle, 0, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    /// Yüzeyi pencereye basar; boyut değiştiyse pencereyi yeniden boyutlandırır.
    public static void Present(Gdi.Surface surface)
    {
        if (!IsCreated) return;
        if (surface.W != Width || surface.H != Height)
        {
            Width = surface.W;
            Height = surface.H;
            X = Math.Clamp(X, 0, Math.Max(0, ParentWidth - Width));
            Win32.SetWindowPos(Handle, 0, X, Y, Width, Height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        }

        nint screen = Win32.GetDC(0);
        nint mem = Win32.CreateCompatibleDC(screen);
        nint old = Win32.SelectObject(mem, surface.Dib);
        var size = new Win32.SIZE { Cx = surface.W, Cy = surface.H };
        var src = new Win32.POINT();
        var blend = new Win32.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        Win32.UpdateLayeredWindow(Handle, screen, 0, ref size, mem, ref src, 0, ref blend, 2);
        Win32.SelectObject(mem, old);
        Win32.DeleteDC(mem);
        Win32.ReleaseDC(0, screen);
    }

    public static void Destroy()
    {
        if (IsCreated) Win32.DestroyWindow(Handle);
        Handle = 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    public static nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (OnMessage(hWnd, msg, out nint result)) return result;
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static bool OnMessage(nint hWnd, uint msg, out nint result)
    {
        result = 0;
        switch (msg)
        {
            case Win32.WM_MOUSEACTIVATE:
                result = Win32.MA_NOACTIVATE;
                return true;

            case Win32.WM_LBUTTONDOWN:
                Win32.GetCursorPos(out var down);
                s_pressed = true;
                s_dragging = false;
                s_dragCursorX = down.X;
                s_dragStartX = X;
                Win32.SetCapture(hWnd);
                return true;

            case Win32.WM_MOUSEMOVE:
                if (!Hover)
                {
                    Hover = true;
                    var tme = new Win32.TRACKMOUSEEVENT { cbSize = sizeof(Win32.TRACKMOUSEEVENT), dwFlags = Win32.TME_LEAVE, hwndTrack = hWnd };
                    Win32.TrackMouseEvent(ref tme);
                    App.RenderTaskbar();
                }
                if (s_pressed)
                {
                    Win32.GetCursorPos(out var cur);
                    int delta = cur.X - s_dragCursorX;
                    if (!s_dragging && Math.Abs(delta) >= (int)(4 * Scale))
                    {
                        s_dragging = true;
                        Panel.Hide();
                    }
                    if (s_dragging)
                    {
                        int nx = Math.Clamp(s_dragStartX + delta, 0, Math.Max(0, ParentWidth - Width));
                        if (nx != X)
                        {
                            X = nx;
                            Win32.SetWindowPos(hWnd, 0, X, Y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                        }
                    }
                }
                return true;

            case Win32.WM_MOUSELEAVE:
                Hover = false;
                App.RenderTaskbar();
                return true;

            case Win32.WM_LBUTTONUP:
                if (s_pressed)
                {
                    s_pressed = false;
                    Win32.ReleaseCapture();
                    if (s_dragging)
                    {
                        s_dragging = false;
                        App.OnMoved(X);
                    }
                    else
                    {
                        App.TogglePanel();
                    }
                }
                return true;

            case Win32.WM_RBUTTONUP:
                App.ShowMenu();
                return true;

            case Win32.WM_NCDESTROY:
                // Explorer yeniden başlarsa görev çubuğuyla birlikte bu pencere de yok edilir.
                if (hWnd == Handle) Handle = 0;
                s_pressed = s_dragging = Hover = false;
                return false;
        }
        return false;
    }
}
