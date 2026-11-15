using System.Globalization;
using System.Text;

namespace TaskbarTickerNative;

internal sealed class TickerItem
{
    public string Label = "";
    public string Name = "";       // panelde etiketin altında görünen açıklama
    public string Symbol = "";     // Yahoo Finance sembolü
    public string Prefix = "";
    public int PanelDecimals = 2;  // panelde ondalık basamak
    public int StripDecimals = 2;  // yatay şeritte ondalık basamak
    public string? MultiplyBy;     // isteğe bağlı: fiyat bu sembolün fiyatıyla çarpılır
    public double DivideBy = 1;    // isteğe bağlı: sonuç buna bölünür
}

/// Ayarlar. Dosya biçimi WPF sürümüyle aynıdır; config.json iki sürüm arasında kopyalanabilir.
internal sealed class Config
{
    public const string ViewPanel = "panel";
    public const string ViewStrip = "strip";

    public string View = ViewPanel;              // "panel" (ikon + panel) ya da "strip" (yatay şerit)
    public int PanelOpenRefreshSeconds = 15;     // panel açıkken
    public int PanelClosedRefreshSeconds = 300;  // ikon görünümünde, panel kapalıyken
    public int StripRefreshSeconds = 60;         // yatay şerit görünümünde
    public bool StripShowPercent;                // şeritte okun yanında değişim yüzdesi
    public int PanelTintPercent = 45;            // cam zemin üstündeki tonun yoğunluğu (0 = tamamen cam, 100 = düz)
    public int? OffsetX;                         // null = otomatik yerleş
    public List<TickerItem> Items = new();

    public bool IsStrip => string.Equals(View, ViewStrip, StringComparison.OrdinalIgnoreCase);

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "config.json");

    public static Config Load()
    {
        Config? cfg = null;
        try
        {
            if (File.Exists(FilePath)) cfg = FromJson(File.ReadAllText(FilePath));
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

    private static Config? FromJson(string text)
    {
        var o = Json.Obj(Json.Parse(text));
        if (o == null) return null;

        var d = new Config();
        var cfg = new Config
        {
            View = Json.Str(o, "view", d.View),
            PanelOpenRefreshSeconds = (int)Json.Num(o, "panelOpenRefreshSeconds", d.PanelOpenRefreshSeconds),
            PanelClosedRefreshSeconds = (int)Json.Num(o, "panelClosedRefreshSeconds", d.PanelClosedRefreshSeconds),
            StripRefreshSeconds = (int)Json.Num(o, "stripRefreshSeconds", d.StripRefreshSeconds),
            StripShowPercent = Json.Bool(o, "stripShowPercent", d.StripShowPercent),
            PanelTintPercent = (int)Json.Num(o, "panelTintPercent", d.PanelTintPercent),
            OffsetX = Json.Get(o, "offsetX") is double x ? (int)x : null,
        };

        foreach (var entry in Json.Arr(Json.Get(o, "items")) ?? new List<object?>())
        {
            var it = Json.Obj(entry);
            if (it == null) continue;
            cfg.Items.Add(new TickerItem
            {
                Label = Json.Str(it, "label", ""),
                Name = Json.Str(it, "name", ""),
                Symbol = Json.Str(it, "symbol", ""),
                Prefix = Json.Str(it, "prefix", ""),
                PanelDecimals = Math.Clamp((int)Json.Num(it, "panelDecimals", 2), 0, 10),
                StripDecimals = Math.Clamp((int)Json.Num(it, "stripDecimals", 2), 0, 10),
                MultiplyBy = Json.Get(it, "multiplyBy") as string,
                DivideBy = Json.Num(it, "divideBy", 1),
            });
        }
        return cfg;
    }

    public void Save()
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"view\": ").Append(Json.Quote(View)).Append(",\n");
        sb.Append("  \"panelOpenRefreshSeconds\": ").Append(PanelOpenRefreshSeconds.ToString(inv)).Append(",\n");
        sb.Append("  \"panelClosedRefreshSeconds\": ").Append(PanelClosedRefreshSeconds.ToString(inv)).Append(",\n");
        sb.Append("  \"stripRefreshSeconds\": ").Append(StripRefreshSeconds.ToString(inv)).Append(",\n");
        sb.Append("  \"stripShowPercent\": ").Append(StripShowPercent ? "true" : "false").Append(",\n");
        sb.Append("  \"panelTintPercent\": ").Append(PanelTintPercent.ToString(inv)).Append(",\n");
        sb.Append("  \"offsetX\": ").Append(OffsetX.HasValue ? OffsetX.Value.ToString(inv) : "null").Append(",\n");
        sb.Append("  \"items\": [\n");
        for (int i = 0; i < Items.Count; i++)
        {
            var it = Items[i];
            sb.Append("    {\n");
            sb.Append("      \"label\": ").Append(Json.Quote(it.Label)).Append(",\n");
            sb.Append("      \"name\": ").Append(Json.Quote(it.Name)).Append(",\n");
            sb.Append("      \"symbol\": ").Append(Json.Quote(it.Symbol)).Append(",\n");
            sb.Append("      \"prefix\": ").Append(Json.Quote(it.Prefix)).Append(",\n");
            sb.Append("      \"panelDecimals\": ").Append(it.PanelDecimals.ToString(inv)).Append(",\n");
            sb.Append("      \"stripDecimals\": ").Append(it.StripDecimals.ToString(inv)).Append(",\n");
            sb.Append("      \"multiplyBy\": ").Append(Json.Quote(it.MultiplyBy)).Append(",\n");
            sb.Append("      \"divideBy\": ").Append(Json.Number(it.DivideBy)).Append('\n');
            sb.Append(i < Items.Count - 1 ? "    },\n" : "    }\n");
        }
        sb.Append("  ]\n}\n");
        try { File.WriteAllText(FilePath, sb.ToString()); } catch { }
    }
}
