using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TaskbarTicker;

/// Uygulamanın akışı: zamanlayıcı, veri yenileme, görünüm seçimi, panel ve sağ tık menüsü.
internal sealed class TickerController
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "TaskbarTicker";

    private readonly QuoteService _service = new();
    private readonly TaskbarHost _host = new();
    private readonly PanelWindow _panel = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    private Config _cfg = Config.Load();
    private Quote[] _quotes;
    private DateTime _lastFetch = DateTime.MinValue;
    private DateTime? _lastUpdated;
    private bool _fetching;
    private Window? _menuAnchor;
    private ContextMenu? _openMenu;

    public TickerController()
    {
        _quotes = NewQuotes();

        _host.Clicked += TogglePanel;
        _host.RightClicked += ShowMenu;
        _host.DragStarted += _panel.Hide;
        _host.HoverChanged += Render;
        _host.Moved += x =>
        {
            _cfg.OffsetX = x;
            _cfg.Save();
        };
        _panel.IsVisibleChanged += (_, _) => Render();

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    private Quote[] NewQuotes() => _cfg.Items.Select(_ => new Quote()).ToArray();

    private void Tick()
    {
        if (!_host.IsCreated)
        {
            // İlk açılış ya da explorer.exe yeniden başladı: görev çubuğunu bekle, yeniden gömül.
            if (_host.TryCreate(_cfg.OffsetX)) Render();
        }
        else
        {
            _host.KeepOnTop();
        }

        int interval = _panel.IsVisible ? _cfg.PanelOpenRefreshSeconds
            : _cfg.IsStrip ? _cfg.StripRefreshSeconds
            : _cfg.PanelClosedRefreshSeconds;
        if (!_fetching && (DateTime.UtcNow - _lastFetch).TotalSeconds >= interval)
            _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _fetching = true;
        _lastFetch = DateTime.UtcNow;
        var quotes = _quotes;
        try
        {
            await _service.RefreshAsync(_cfg.Items, quotes);
        }
        catch { /* tek tek kalemler zaten Stale olarak işaretlenir */ }
        finally
        {
            _fetching = false;
        }
        if (!ReferenceEquals(quotes, _quotes)) return; // bu sırada ayarlar yeniden yüklendi
        if (quotes.Any(q => !q.Stale)) _lastUpdated = DateTime.Now;
        Render();
        if (_panel.IsVisible) _panel.Update(_cfg, _quotes, _lastUpdated);
    }

    private void Render()
    {
        if (!_host.IsCreated) return;
        _host.Present(_cfg.IsStrip
            ? TaskbarRenderer.DrawStrip(_cfg, _quotes, _host.Height, _host.Scale)
            : TaskbarRenderer.DrawIcon(_host.Height, _host.Scale, _host.Hover, _panel.IsVisible));
    }

    // ------------------------------------------------------------ panel

    private void TogglePanel()
    {
        if (_panel.IsVisible)
        {
            _panel.Hide();
            return;
        }
        // Panel, bu tıklama yüzünden az önce odağı kaybedip kapandıysa aynı tıklamayla yeniden açma.
        if ((DateTime.UtcNow - _panel.ClosedAt).TotalMilliseconds < 250) return;

        _host.RefreshTrayRect();
        double s = _host.Scale;
        _panel.Update(_cfg, _quotes, _lastUpdated);
        _panel.ShowAbove((_host.TrayRect.Left + _host.X + _host.Width / 2.0) / s, _host.TrayRect.Top / s);

        // Açılırken veri eskiyse hemen tazele.
        if (!_fetching && (DateTime.UtcNow - _lastFetch).TotalSeconds >= _cfg.PanelOpenRefreshSeconds)
            _ = RefreshAsync();
    }

    // ------------------------------------------------------------ sağ tık menüsü

    /// Menü açıkken odağı tutan görünmez pencere. WPF menüsü, uygulama etkin değilken ya da
    /// etkin penceresi o anda gizlenirken (ör. açık panel) açılırsa hemen kapanır; menüyü
    /// bu pencereye dayandırmak açılışı da "dışarı tıklayınca kapan" davranışını da güvenilir kılar.
    private Window EnsureMenuAnchor()
    {
        if (_menuAnchor != null) return _menuAnchor;

        var anchor = new Window
        {
            Width = 1, Height = 1, Left = -32000, Top = -32000,
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false, ShowActivated = true, Topmost = true,
        };
        anchor.SourceInitialized += (_, _) =>
        {
            // Alt+Tab listesinde görünmesin
            IntPtr hwnd = new WindowInteropHelper(anchor).Handle;
            long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW));
        };
        // Başka bir pencereye geçilirse menüyü kapat.
        anchor.Deactivated += (_, _) =>
        {
            if (_openMenu != null) _openMenu.IsOpen = false;
        };
        return _menuAnchor = anchor;
    }

    private void ShowMenu()
    {
        if (_openMenu != null) _openMenu.IsOpen = false;

        // Önce odağı al, sonra paneli gizle: sıra tersine olursa odak başka uygulamaya kaçar.
        var anchor = EnsureMenuAnchor();
        anchor.Show();
        anchor.Activate();
        _panel.Hide();

        var view = new MenuItem { Header = "Görünüm" };
        view.Items.Add(Item("İkon + panel", () => SetView(Config.ViewPanel), isChecked: !_cfg.IsStrip));
        view.Items.Add(Item("Yatay şerit", () => SetView(Config.ViewStrip), isChecked: _cfg.IsStrip));

        var menu = new ContextMenu();
        menu.Items.Add(view);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Şimdi yenile", () =>
        {
            _lastFetch = DateTime.MinValue;
            Tick();
        }));
        menu.Items.Add(Item("Ayarları aç (config.json)", OpenConfig));
        menu.Items.Add(Item("Ayarları yeniden yükle", () => Reload(resetPosition: false)));
        menu.Items.Add(Item("Konumu sıfırla", () => Reload(resetPosition: true)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Windows ile başlat", () => SetAutoStart(!IsAutoStart()), isChecked: IsAutoStart()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Çıkış", Quit));

        // İmlecin olduğu yerde, görev çubuğunun hemen üstünde aç.
        Native.GetCursorPos(out var cursor);
        _host.RefreshTrayRect();
        double s = _host.Scale;
        menu.Placement = PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = cursor.x / s;
        menu.VerticalOffset = cursor.y / s;
        menu.Opened += (_, _) => menu.VerticalOffset = _host.TrayRect.Top / s - menu.ActualHeight - 2;
        menu.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_openMenu, menu)) return;
            _openMenu = null;
            anchor.Hide();
        };
        _openMenu = menu;
        menu.IsOpen = true;
    }

    private static MenuItem Item(string header, Action action, bool isChecked = false)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked };
        item.Click += (_, _) => action();
        return item;
    }

    private void SetView(string view)
    {
        if (string.Equals(_cfg.View, view, StringComparison.OrdinalIgnoreCase)) return;
        _panel.Hide();
        _cfg.View = view;
        _cfg.Save();
        Render();
    }

    private static void OpenConfig()
    {
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Config.FilePath + "\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void Reload(bool resetPosition)
    {
        _panel.Hide();
        _cfg = Config.Load();
        if (resetPosition)
        {
            _cfg.OffsetX = null;
            _cfg.Save();
        }
        _quotes = NewQuotes();
        _lastUpdated = null;
        _lastFetch = DateTime.MinValue;
        _host.Destroy(); // Tick yeni ayarlarla yeniden oluşturur
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

    private void Quit()
    {
        _timer.Stop();
        _openMenu = null; // menünün Closed olayı kapatılmış pencereye dokunmasın
        _menuAnchor?.Close();
        _panel.Close();
        _host.Destroy();
        Application.Current.Shutdown();
    }
}
