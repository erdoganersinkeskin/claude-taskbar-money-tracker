using System.Runtime.InteropServices;

namespace TaskbarTickerNative;

/// Kullanılan Win32 çağrıları. Hepsi doğrudan sistem DLL'lerine gider; ara katman yoktur.
internal static unsafe class Win32
{
    public const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_LAYERED = 0x00080000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080,
        WS_EX_TOPMOST = 0x00000008;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public static readonly nint HWND_TOPMOST = -1;

    public const uint WM_NULL = 0, WM_ACTIVATE = 0x0006, WM_PAINT = 0x000F, WM_ERASEBKGND = 0x0014,
        WM_MOUSEACTIVATE = 0x0021, WM_NCDESTROY = 0x0082, WM_KEYDOWN = 0x0100, WM_TIMER = 0x0113,
        WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205,
        WM_MOUSELEAVE = 0x02A3, WM_APP = 0x8000;
    public const nint MA_NOACTIVATE = 3;
    public const int TME_LEAVE = 2, VK_ESCAPE = 0x1B, SW_HIDE = 0, IDC_ARROW = 32512;
    public const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;
    public const uint MF_STRING = 0, MF_CHECKED = 8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800;
    public const uint TPM_RIGHTBUTTON = 2, TPM_BOTTOMALIGN = 0x20, TPM_RETURNCMD = 0x100, TPM_NONOTIFY = 0x80;
    public const uint SPI_GETWORKAREA = 0x30;

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int Cx, Cy; }
    [StructLayout(LayoutKind.Sequential)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] public struct TRACKMOUSEEVENT { public int cbSize, dwFlags; public nint hwndTrack; public int dwHoverTime; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)] public struct PAINTSTRUCT { public nint hdc; public int fErase; public RECT rcPaint; public int fRestore, fIncUpdate; public fixed byte reserved[32]; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public int cbSize, style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    private const string U = "user32.dll", G = "gdi32.dll";

    [DllImport(U, CharSet = CharSet.Unicode)] public static extern nint FindWindowW(string? cls, string? title);
    [DllImport(U, CharSet = CharSet.Unicode)] public static extern nint FindWindowExW(nint parent, nint after, string? cls, string? title);
    [DllImport(U)] public static extern int GetClassNameW(nint h, char* buffer, int max);
    [DllImport(U)] public static extern int GetWindowRect(nint h, out RECT r);
    [DllImport(U)] public static extern int IsWindowVisible(nint h);
    [DllImport(U)] public static extern uint GetDpiForWindow(nint h);
    [DllImport(U)] public static extern int SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport(U)] public static extern int DestroyWindow(nint h);
    [DllImport(U)] public static extern nint DefWindowProcW(nint h, uint msg, nint w, nint l);
    [DllImport(U)] public static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport(U, CharSet = CharSet.Unicode)]
    public static extern nint CreateWindowExW(int ex, string cls, string title, int style, int x, int y, int w, int h,
        nint parent, nint menu, nint instance, nint param);
    [DllImport(U)] public static extern nint LoadCursorW(nint instance, nint name);
    [DllImport(U)] public static extern int GetCursorPos(out POINT p);
    [DllImport(U)] public static extern nint SetCapture(nint h);
    [DllImport(U)] public static extern int ReleaseCapture();
    [DllImport(U)] public static extern int TrackMouseEvent(ref TRACKMOUSEEVENT e);
    [DllImport(U)]
    public static extern int UpdateLayeredWindow(nint h, nint dstDc, nint pptDst, ref SIZE size, nint srcDc, ref POINT src,
        int key, ref BLENDFUNCTION blend, int flags);
    [DllImport(U)] public static extern nint GetDC(nint h);
    [DllImport(U)] public static extern int ReleaseDC(nint h, nint dc);
    [DllImport(U)] public static extern int ShowWindow(nint h, int cmd);
    [DllImport(U)] public static extern int SetForegroundWindow(nint h);
    [DllImport(U)] public static extern int GetMessageW(out MSG m, nint h, uint min, uint max);
    [DllImport(U)] public static extern int TranslateMessage(ref MSG m);
    [DllImport(U)] public static extern nint DispatchMessageW(ref MSG m);
    [DllImport(U)] public static extern void PostQuitMessage(int code);
    [DllImport(U)] public static extern int PostMessageW(nint h, uint msg, nint w, nint l);
    [DllImport(U)] public static extern nuint SetTimer(nint h, nuint id, uint ms, nint proc);
    [DllImport(U)] public static extern int KillTimer(nint h, nuint id);
    [DllImport(U)] public static extern int InvalidateRect(nint h, nint rect, int erase);
    [DllImport(U)] public static extern nint BeginPaint(nint h, out PAINTSTRUCT ps);
    [DllImport(U)] public static extern int EndPaint(nint h, ref PAINTSTRUCT ps);
    [DllImport(U)] public static extern nint CreatePopupMenu();
    [DllImport(U, CharSet = CharSet.Unicode)] public static extern int AppendMenuW(nint menu, uint flags, nuint id, string? text);
    [DllImport(U)] public static extern int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint owner, nint rect);
    [DllImport(U)] public static extern int DestroyMenu(nint menu);
    [DllImport(U)] public static extern int SystemParametersInfoW(uint action, uint param, out RECT r, uint winIni);
    [DllImport(U)] public static extern int SetProcessDpiAwarenessContext(nint context);

    [DllImport(G)] public static extern nint CreateCompatibleDC(nint dc);
    [DllImport(G)] public static extern int DeleteDC(nint dc);
    [DllImport(G)] public static extern nint SelectObject(nint dc, nint obj);
    [DllImport(G)] public static extern int DeleteObject(nint obj);
    [DllImport(G)] public static extern nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport(G)] public static extern int BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, int rop);

    [DllImport("kernel32.dll")] public static extern nint GetModuleHandleW(nint name);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(nint h, int attr, ref int value, int size);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern nint ShellExecuteW(nint h, string verb, string file, string? args, string? dir, int show);

    // Koyu temada sağ tık menüsünün de koyu çizilmesi (belgelenmemiş ama yaygın kullanılan uxtheme çağrıları)
    [DllImport("uxtheme.dll", EntryPoint = "#135")] public static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] public static extern void FlushMenuThemes();

    // ---- WinHTTP
    private const string H = "winhttp.dll";
    [DllImport(H, CharSet = CharSet.Unicode)] public static extern nint WinHttpOpen(string agent, int access, nint proxy, nint bypass, int flags);
    [DllImport(H, CharSet = CharSet.Unicode)] public static extern nint WinHttpConnect(nint session, string host, ushort port, int reserved);
    [DllImport(H, CharSet = CharSet.Unicode)]
    public static extern nint WinHttpOpenRequest(nint connect, string verb, string path, nint version, nint referrer, nint accept, int flags);
    [DllImport(H)] public static extern int WinHttpSetTimeouts(nint h, int resolve, int connect, int send, int receive);
    [DllImport(H)] public static extern int WinHttpSendRequest(nint req, nint headers, int headersLen, nint optional, int optionalLen, int totalLen, nint context);
    [DllImport(H)] public static extern int WinHttpReceiveResponse(nint req, nint reserved);
    [DllImport(H)] public static extern int WinHttpQueryHeaders(nint req, int level, nint name, out int value, ref int size, nint index);
    [DllImport(H)] public static extern int WinHttpReadData(nint req, byte* buffer, int toRead, out int read);
    [DllImport(H)] public static extern int WinHttpCloseHandle(nint h);

    public static int LoWord(nint v) => (short)((long)v & 0xFFFF);
    public static int HiWord(nint v) => (short)(((long)v >> 16) & 0xFFFF);
}
