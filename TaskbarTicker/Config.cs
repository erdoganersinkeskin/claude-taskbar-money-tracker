using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarTicker;

public sealed class TickerItem
{
    public string Label { get; set; } = "";
    public string Name { get; set; } = "";       // panelde etiketin altında görünen açıklama
    public string Symbol { get; set; } = "";     // Yahoo Finance sembolü
    public string Prefix { get; set; } = "";
    public int PanelDecimals { get; set; } = 2;  // panelde ondalık basamak
    public int StripDecimals { get; set; } = 2;  // yatay şeritte ondalık basamak
    public string? MultiplyBy { get; set; }      // isteğe bağlı: fiyat bu sembolün fiyatıyla çarpılır
    public double DivideBy { get; set; } = 1;    // isteğe bağlı: sonuç buna bölünür
}

public sealed class Config
{
    public const string ViewPanel = "panel";
    public const string ViewStrip = "strip";

    public string View { get; set; } = ViewPanel;              // "panel" (ikon + panel) ya da "strip" (yatay şerit)
    public int PanelOpenRefreshSeconds { get; set; } = 15;     // panel açıkken
    public int PanelClosedRefreshSeconds { get; set; } = 300;  // ikon görünümünde, panel kapalıyken
    public int StripRefreshSeconds { get; set; } = 60;         // yatay şerit görünümünde
    public bool StripShowPercent { get; set; }                 // şeritte okun yanında değişim yüzdesi
    public int PanelTintPercent { get; set; } = 45;            // panelin cam zemini üstündeki tonun yoğunluğu (0 = tamamen cam, 100 = düz)
    public int? OffsetX { get; set; }                          // null = otomatik yerleş
    public List<TickerItem> Items { get; set; } = new();

    [JsonIgnore]
    public bool IsStrip => string.Equals(View, ViewStrip, StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "config.json");

    public static Config Load()
    {
        Config? cfg = null;
        try
        {
            if (File.Exists(FilePath))
                cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), Json);
        }
        catch { /* bozuk dosya: varsayılana dön */ }

        if (cfg == null || cfg.Items.Count == 0)
        {
            cfg = new Config
            {
                Items =
                {
                    new TickerItem { Label = "BTC", Name = "Bitcoin", Symbol = "BTC-USD", Prefix = "$", PanelDecimals = 0, StripDecimals = 0 },
                    new TickerItem { Label = "ETH", Name = "Ethereum", Symbol = "ETH-USD", Prefix = "$", PanelDecimals = 0, StripDecimals = 0 },
                    new TickerItem { Label = "USD", Name = "Dolar", Symbol = "USDTRY=X", Prefix = "₺", PanelDecimals = 4, StripDecimals = 2 },
                    new TickerItem { Label = "EUR", Name = "Euro", Symbol = "EURTRY=X", Prefix = "₺", PanelDecimals = 4, StripDecimals = 2 },
                    new TickerItem
                    {
                        Label = "ALTIN", Name = "Gram altın", Symbol = "GC=F", MultiplyBy = "USDTRY=X", DivideBy = 31.1035,
                        Prefix = "₺", PanelDecimals = 0, StripDecimals = 0,
                    },
                }
            };
            cfg.Save();
        }

        cfg.PanelOpenRefreshSeconds = Math.Max(5, cfg.PanelOpenRefreshSeconds);
        cfg.PanelClosedRefreshSeconds = Math.Max(5, cfg.PanelClosedRefreshSeconds);
        cfg.StripRefreshSeconds = Math.Max(5, cfg.StripRefreshSeconds);
        cfg.PanelTintPercent = Math.Clamp(cfg.PanelTintPercent, 0, 100);
        return cfg;
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); } catch { }
    }
}
