using System.Windows;

namespace TaskbarTicker;

// Görev çubuğuna gömülü fiyat göstergesi. İki görünümü vardır:
//  - ikon + panel (varsayılan): tek bir para ikonu; tıklanınca üstünde panel açılır
//  - yatay şerit: tüm kalemler görev çubuğunda yan yana durur
public partial class App : Application
{
    private Mutex? _mutex;
    private TickerController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "TaskbarTicker.SingleInstance", out bool first);
        if (!first)
        {
            Shutdown();
            return;
        }
        _controller = new TickerController();
    }
}
