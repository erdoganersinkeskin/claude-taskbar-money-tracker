using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TaskbarTickerNative;

// Görev çubuğuna gömülü fiyat göstergesi — WPF'siz, yerel (Native AOT) derlenen sürüm.
// Görünüm ve davranış WPF sürümüyle aynıdır:
//  - ikon + panel (varsayılan): tek bir para ikonu; tıklanınca üstünde panel açılır
//  - yatay şerit: tüm kalemler görev çubuğunda yan yana durur
internal static unsafe class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Tanılama: çizimleri ekrana dokunmadan .bmp dosyalarına yazar (TaskbarTickerNative.exe --dump <klasör>)
        if (args.Length == 2 && args[0] == "--dump")
        {
            Win32.SetProcessDpiAwarenessContext(-4);
            Gdi.Startup();
            App.Dump(args[1]);
            return;
        }

        using var mutex = new Mutex(true, "TaskbarTickerNative.SingleInstance", out bool first);
        if (!first) return;

        Win32.SetProcessDpiAwarenessContext(-4); // PER_MONITOR_AWARE_V2
        try
        {
            Win32.SetPreferredAppMode(1); // AllowDark: sağ tık menüsü sistem temasını izlesin
            Win32.FlushMenuThemes();
        }
        catch { /* eski Windows sürümü: menü açık temada kalır */ }

        Gdi.Startup();
        App.Run();
    }
}

/// Uygulamanın akışı: zamanlayıcı, veri yenileme, görünüm seçimi, panel ve sağ tık menüsü.
internal static unsafe class App
{
    private const string MainClass = "TaskbarTickerNativeMain";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "TaskbarTickerNative";
    private const uint WM_FETCHED = Win32.WM_APP + 1;

    private static nint s_main; // görünmez pencere: zamanlayıcı, arka plan bildirimleri ve menünün sahibi
    private static Config s_cfg = new();
    private static Quote[] s_quotes = Array.Empty<Quote>();
    private static DateTime s_lastFetch = DateTime.MinValue;
    private static DateTime? s_lastUpdated;
    private static bool s_fetching;
    private static int s_generation;
    private static QuoteResult[]? s_pending;
    private static readonly object s_pendingLock = new();

    public static void Run()
    {
        nint instance = Win32.GetModuleHandleW(0);
        Register(MainClass, (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&MainProc, instance);
        Register(TaskbarHost.ClassName, (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&TaskbarHost.WndProc, instance);
        Register(Panel.ClassName, (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&Panel.WndProc, instance);

        s_cfg = Config.Load();
        s_quotes = NewQuotes();

        s_main = Win32.CreateWindowExW(Win32.WS_EX_TOOLWINDOW, MainClass, "TaskbarTickerNative", Win32.WS_POPUP,
            -32000, -32000, 1, 1, 0, 0, instance, 0);
        Win32.SetTimer(s_main, 1, 1000, 0);
        Tick();

        while (Win32.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessageW(ref msg);
        }
    }

    private static void Register(string name, nint proc, nint instance)
    {
        fixed (char* p = name)
        {
            var wc = new Win32.WNDCLASSEXW
            {
                cbSize = sizeof(Win32.WNDCLASSEXW),
                lpfnWndProc = proc,
                hInstance = instance,
                hCursor = Win32.LoadCursorW(0, Win32.IDC_ARROW),
                lpszClassName = p,
            };
            Win32.RegisterClassExW(ref wc);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint MainProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (msg == Win32.WM_TIMER)
            {
                Tick();
                return 0;
            }
            if (msg == WM_FETCHED)
            {
                OnFetched((int)wParam);
                return 0;
            }
        }
        catch (Exception ex)
        {
            Log(ex);
        }
        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public static void Log(Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "error.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }

    /// Tanılama çıktısı: ikon, şerit ve panel çizimlerini verilen klasöre .bmp olarak yazar.
    public static void Dump(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            s_cfg = Config.Load();
            s_quotes = NewQuotes();
            var results = QuoteService.Fetch(s_cfg.Items);
            for (int i = 0; i < s_quotes.Length; i++)
            {
                if (!results[i].Ok) continue;
                s_quotes[i].Price = results[i].Price;
                s_quotes[i].PrevClose = results[i].PrevClose;
                s_quotes[i].Series = results[i].Series;
                s_quotes[i].Stale = false;
            }

            const uint taskbar = 0xFF1C1C1C; // saydam çizimler koyu görev çubuğu rengi üstüne bindirilir
            using (var icon = TaskbarRenderer.DrawIcon(46, 1, hover: false, active: false)) icon.SaveBmp(Path.Combine(folder, "icon.bmp"), taskbar);
            using (var icon = TaskbarRenderer.DrawIcon(46, 1, hover: true, active: true)) icon.SaveBmp(Path.Combine(folder, "icon-active.bmp"), taskbar);
            using (var strip = TaskbarRenderer.DrawStrip(s_cfg, s_quotes, 46, 1)) strip.SaveBmp(Path.Combine(folder, "strip.bmp"), taskbar);
            Panel.RenderOffscreen(s_cfg, s_quotes, DateTime.Now, 1, hover: 1)?.SaveBmp(Path.Combine(folder, "panel.bmp"), 0xFF000000);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private static Quote[] NewQuotes()
    {
        var quotes = new Quote[s_cfg.Items.Count];
        for (int i = 0; i < quotes.Length; i++) quotes[i] = new Quote();
        return quotes;
    }

    private static void Tick()
    {
        if (!TaskbarHost.IsCreated)
        {
            // İlk açılış ya da explorer.exe yeniden başladı: görev çubuğunu bekle, yeniden gömül.
            if (TaskbarHost.TryCreate(s_cfg.OffsetX)) RenderTaskbar();
        }
        else
        {
            TaskbarHost.KeepOnTop();
        }

        int interval = Panel.Visible ? s_cfg.PanelOpenRefreshSeconds
            : s_cfg.IsStrip ? s_cfg.StripRefreshSeconds
            : s_cfg.PanelClosedRefreshSeconds;
        if (!s_fetching && (DateTime.UtcNow - s_lastFetch).TotalSeconds >= interval) StartRefresh();
    }

    // ------------------------------------------------------------ veri

    private static void StartRefresh()
    {
        s_fetching = true;
        s_lastFetch = DateTime.UtcNow;
        int generation = s_generation;
        var items = s_cfg.Items;
        nint main = s_main;
        Task.Run(() =>
        {
            QuoteResult[]? results = null;
            try { results = QuoteService.Fetch(items); }
            catch { /* hepsi "eski" olarak işaretlenir */ }
            lock (s_pendingLock) s_pending = results;
            Win32.PostMessageW(main, WM_FETCHED, generation, 0);
        });
    }

    private static void OnFetched(int generation)
    {
        QuoteResult[]? results;
        lock (s_pendingLock)
        {
            results = s_pending;
            s_pending = null;
        }
        if (generation != s_generation) return; // bu sırada ayarlar yeniden yüklendi
        s_fetching = false;

        bool any = false;
        for (int i = 0; i < s_quotes.Length; i++)
        {
            var q = s_quotes[i];
            if (results != null && i < results.Length && results[i].Ok)
            {
                q.Price = results[i].Price;
                q.PrevClose = results[i].PrevClose;
                q.Series = results[i].Series;
                q.Stale = false;
                any = true;
            }
            else
            {
                q.Stale = true; // son bilinen değer soluk gösterilir
            }
        }
        if (any) s_lastUpdated = DateTime.Now;
        RenderTaskbar();
        if (Panel.Visible) Panel.Update(s_cfg, s_quotes, s_lastUpdated);
    }

    public static void RenderTaskbar()
    {
        if (!TaskbarHost.IsCreated) return;
        using var surface = s_cfg.IsStrip
            ? TaskbarRenderer.DrawStrip(s_cfg, s_quotes, TaskbarHost.Height, TaskbarHost.Scale)
            : TaskbarRenderer.DrawIcon(TaskbarHost.Height, TaskbarHost.Scale, TaskbarHost.Hover, Panel.Visible);
        TaskbarHost.Present(surface);
    }

    public static void OnMoved(int x)
    {
        s_cfg.OffsetX = x;
        s_cfg.Save();
    }

    // ------------------------------------------------------------ panel

    public static void TogglePanel()
    {
        if (Panel.Visible)
        {
            Panel.Hide();
            return;
        }
        // Panel, bu tıklama yüzünden az önce odağı kaybedip kapandıysa aynı tıklamayla yeniden açma.
        if ((DateTime.UtcNow - Panel.ClosedAt).TotalMilliseconds < 250) return;

        TaskbarHost.RefreshTrayRect();
        Panel.Show(s_cfg, s_quotes, s_lastUpdated,
            TaskbarHost.TrayRect.Left + TaskbarHost.X + TaskbarHost.Width / 2, TaskbarHost.TrayRect.Top, TaskbarHost.Scale);

        // Açılırken veri eskiyse hemen tazele.
        if (!s_fetching && (DateTime.UtcNow - s_lastFetch).TotalSeconds >= s_cfg.PanelOpenRefreshSeconds) StartRefresh();
    }

    // ------------------------------------------------------------ sağ tık menüsü

    public static void ShowMenu()
    {
        // Menünün "dışarı tıklayınca kapan" davranışı için sahibi ön planda olmalı.
        Win32.SetForegroundWindow(s_main);
        Panel.Hide();

        nint view = Win32.CreatePopupMenu();
        Win32.AppendMenuW(view, Win32.MF_STRING | (s_cfg.IsStrip ? 0 : Win32.MF_CHECKED), 1, "İkon + panel");
        Win32.AppendMenuW(view, Win32.MF_STRING | (s_cfg.IsStrip ? Win32.MF_CHECKED : 0), 2, "Yatay şerit");

        nint menu = Win32.CreatePopupMenu();
        Win32.AppendMenuW(menu, Win32.MF_POPUP, (nuint)view, "Görünüm");
        Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(menu, Win32.MF_STRING, 3, "Şimdi yenile");
        Win32.AppendMenuW(menu, Win32.MF_STRING, 4, "Ayarları aç (config.json)");
        Win32.AppendMenuW(menu, Win32.MF_STRING, 5, "Ayarları yeniden yükle");
        Win32.AppendMenuW(menu, Win32.MF_STRING, 6, "Konumu sıfırla");
        Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(menu, Win32.MF_STRING | (IsAutoStart() ? Win32.MF_CHECKED : 0), 7, "Windows ile başlat");
        Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(menu, Win32.MF_STRING, 8, "Çıkış");

        // İmlecin olduğu yerde, görev çubuğunun hemen üstünde aç.
        Win32.GetCursorPos(out var cursor);
        TaskbarHost.RefreshTrayRect();
        int cmd = Win32.TrackPopupMenu(menu,
            Win32.TPM_RETURNCMD | Win32.TPM_NONOTIFY | Win32.TPM_RIGHTBUTTON | Win32.TPM_BOTTOMALIGN,
            cursor.X, TaskbarHost.TrayRect.Top - 2, 0, s_main, 0);
        Win32.DestroyMenu(menu); // alt menüyü de yok eder
        Win32.PostMessageW(s_main, Win32.WM_NULL, 0, 0);

        switch (cmd)
        {
            case 1: SetView(Config.ViewPanel); break;
            case 2: SetView(Config.ViewStrip); break;
            case 3:
                s_lastFetch = DateTime.MinValue;
                Tick();
                break;
            case 4: Win32.ShellExecuteW(0, "open", "notepad.exe", "\"" + Config.FilePath + "\"", null, 1); break;
            case 5: Reload(resetPosition: false); break;
            case 6: Reload(resetPosition: true); break;
            case 7: SetAutoStart(!IsAutoStart()); break;
            case 8: Quit(); break;
        }
    }

    private static void SetView(string view)
    {
        if (string.Equals(s_cfg.View, view, StringComparison.OrdinalIgnoreCase)) return;
        Panel.Hide();
        s_cfg.View = view;
        s_cfg.Save();
        RenderTaskbar();
    }

    private static void Reload(bool resetPosition)
    {
        Panel.Hide();
        s_cfg = Config.Load();
        if (resetPosition)
        {
            s_cfg.OffsetX = null;
            s_cfg.Save();
        }
        s_generation++; // süren bir yenileme varsa sonucu yok sayılır
        s_fetching = false;
        s_quotes = NewQuotes();
        s_lastUpdated = null;
        s_lastFetch = DateTime.MinValue;
        TaskbarHost.Destroy(); // Tick yeni ayarlarla yeniden oluşturur
        Tick();
    }

    private static bool IsAutoStart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunName) != null;
    }

    private static void SetAutoStart(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (k == null) return;
        if (on) k.SetValue(RunName, "\"" + Environment.ProcessPath + "\"");
        else k.DeleteValue(RunName, throwOnMissingValue: false);
    }

    private static void Quit()
    {
        Win32.KillTimer(s_main, 1);
        Panel.Destroy();
        TaskbarHost.Destroy();
        Win32.DestroyWindow(s_main);
        Win32.PostQuitMessage(0);
    }
}
