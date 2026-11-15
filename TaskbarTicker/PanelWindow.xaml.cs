using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TaskbarTicker;

/// Panelde tek bir kalemin görünen hâli.
public sealed record PanelRow(
    string Label, string Name, string Price, string Change, Brush Trend, IReadOnlyList<double> Series, double Opacity,
    CoinIcon Icon);

/// İkonun üstünde açılan, buzlu cam zeminli panel.
public partial class PanelWindow : Window
{
    private const double Gap = 12; // görev çubuğu ve ekran kenarıyla arasındaki boşluk

    private static readonly Brush UpDark = Frozen(61, 214, 140), DownDark = Frozen(255, 107, 107);
    private static readonly Brush UpLight = Frozen(0, 140, 80), DownLight = Frozen(200, 40, 40);

    /// Panelin odağı kaybedip kendiliğinden kapandığı an (aynı tıklamayla yeniden açılmasın diye).
    public DateTime ClosedAt { get; private set; } = DateTime.MinValue;

    private int _tintPercent = 45; // cam zemin üstündeki tonun yoğunluğu (config: panelTintPercent)

    public PanelWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;

        // Zemin görüntüsü, bulanıklığın kenarda solmaması için her yönde 48 birim taşar
        Frame.SizeChanged += (_, e) =>
        {
            Backdrop.Width = e.NewSize.Width + 96;
            Backdrop.Height = e.NewSize.Height + 96;
        };
        Deactivated += (_, _) =>
        {
            if (!IsVisible) return;
            ClosedAt = DateTime.UtcNow;
            Hide();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Hide();
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;

        // Alt+Tab listesinde görünmesin
        long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW));

        // Windows 11 yuvarlak köşe + gölge
        int round = Native.DWMWCP_ROUND;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        // Koyu/açık başlık işareti ve Fluent temasının Mica'sını kapat (zemini kendimiz çiziyoruz)
        int dark = TaskbarTheme.IsLight("AppsUseLightTheme") ? 0 : 1;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int none = Native.DWMSBT_NONE;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
    }

    /// Panelin kaplayacağı ekran bölgesini (bulanıklık taşması için kenar payıyla) yakalayıp cam zemin yapar.
    /// Panel görünmeden önce çağrılmalı; yoksa panelin kendisi de görüntüye girer.
    private void CaptureBackdrop(double heightDip)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        const double pad = 48; // XAML'deki Canvas.Left/Top="-48" ile aynı
        int x = (int)Math.Round((Left - pad) * scale), y = (int)Math.Round((Top - pad) * scale);
        int w = (int)Math.Round((Width + 2 * pad) * scale), h = (int)Math.Round((heightDip + 2 * pad) * scale);

        Backdrop.Source = ScreenCapture.Grab(x, y, w, h);

        bool light = TaskbarTheme.IsLight("AppsUseLightTheme");
        byte alpha = (byte)(_tintPercent * 255 / 100);
        Tint.Background = new SolidColorBrush(light
            ? Color.FromArgb(alpha, 0xF7, 0xF7, 0xF9)
            : Color.FromArgb(alpha, 0x16, 0x16, 0x1C));
        // Görüntü alınamazsa düz zemin
        if (Backdrop.Source == null)
            Tint.Background = new SolidColorBrush(light ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x24));
    }

    public void Update(Config cfg, IReadOnlyList<Quote> quotes, DateTime? lastUpdated)
    {
        _tintPercent = cfg.PanelTintPercent;

        bool light = TaskbarTheme.IsLight("AppsUseLightTheme");
        Brush up = light ? UpLight : UpDark, down = light ? DownLight : DownDark;
        Brush neutral = (Brush?)TryFindResource("TextFillColorSecondaryBrush") ?? Brushes.Gray;

        var rows = new List<PanelRow>();
        for (int i = 0; i < cfg.Items.Count && i < quotes.Count; i++)
        {
            var item = cfg.Items[i];
            var q = quotes[i];
            double? change = q.ChangePercent;

            string price = q.Price.HasValue
                ? item.Prefix + q.Price.Value.ToString("N" + item.PanelDecimals, CultureInfo.CurrentCulture)
                : "…";
            string changeText = change.HasValue
                ? (change.Value >= 0 ? "▲ " : "▼ ") + Math.Abs(change.Value).ToString("0.00", CultureInfo.CurrentCulture) + "%"
                : "";
            Brush trend = q.Stale ? neutral : change is < 0 ? down : up;

            string name = string.IsNullOrWhiteSpace(item.Name) ? item.Label : item.Name;
            rows.Add(new PanelRow(item.Label, name, price, changeText, trend, q.Series,
                q.Stale && q.Price.HasValue ? 0.55 : 1, CoinIcons.For(item)));
        }
        Rows.ItemsSource = rows;

        StatusText.Text = lastUpdated.HasValue
            ? "Güncellendi " + lastUpdated.Value.ToString("HH:mm:ss")
            : "Yükleniyor…";
    }

    /// Paneli, verilen yatay merkezin (ikon) üstünde ve görev çubuğunun hemen yukarısında gösterir.
    /// Ölçüler WPF birimidir (aygıt pikseli ÷ DPI ölçeği).
    public void ShowAbove(double anchorCenterX, double taskbarTop)
    {
        var area = SystemParameters.WorkArea;
        Root.Measure(new Size(Width, double.PositiveInfinity));
        Place(anchorCenterX, taskbarTop, Root.DesiredSize.Height, area);
        CaptureBackdrop(Root.DesiredSize.Height);

        Show();
        Activate();

        // Gerçek yükseklik tahminden farklı çıktıysa yerleşimi düzelt.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (IsVisible && ActualHeight > 0) Place(anchorCenterX, taskbarTop, ActualHeight, area);
        });
    }

    private void Place(double anchorCenterX, double taskbarTop, double height, Rect area)
    {
        double left = anchorCenterX - Width / 2;
        Left = Math.Max(area.Left + Gap, Math.Min(left, area.Right - Gap - Width));
        Top = Math.Max(area.Top + Gap, taskbarTop - Gap - height);
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
