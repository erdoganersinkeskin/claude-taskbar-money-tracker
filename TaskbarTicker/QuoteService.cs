using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;

namespace TaskbarTicker;

public sealed class Quote
{
    public double? Price { get; set; }
    public double? PrevClose { get; set; }
    public double[] Series { get; set; } = Array.Empty<double>();
    public bool Stale { get; set; } = true;

    public double? ChangePercent =>
        Price.HasValue && PrevClose is > 0 ? (Price.Value / PrevClose.Value - 1) * 100 : null;
}

/// Fiyatları Yahoo Finance'in herkese açık grafik servisinden çeker.
public sealed class QuoteService
{
    private readonly record struct Raw(double Price, double? Prev, double[] Series);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public QuoteService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
    }

    public async Task RefreshAsync(IReadOnlyList<TickerItem> items, IReadOnlyList<Quote> quotes)
    {
        // Aynı sembol birden çok kalemde geçiyorsa (ör. USDTRY=X) tek istek yeterli.
        var cache = new ConcurrentDictionary<string, Task<Raw>>();
        Task<Raw> Get(string symbol) => cache.GetOrAdd(symbol, FetchRawAsync);

        await Task.WhenAll(items.Select((item, i) => UpdateAsync(item, quotes[i], Get)));
    }

    private static async Task UpdateAsync(TickerItem item, Quote q, Func<string, Task<Raw>> get)
    {
        try
        {
            var raw = await get(item.Symbol);
            double? prev = raw.Prev;

            // Türetilmiş kalemler (ör. gram altın = ons/USD × USD/TRY ÷ 31,1035)
            double factor = 1;
            if (!string.IsNullOrWhiteSpace(item.MultiplyBy))
            {
                var mult = await get(item.MultiplyBy);
                factor *= mult.Price;
                // Önceki kapanış kendi kuruyla çarpılmalı; aşağıda factor ile çarpılacağı için düzeltiyoruz.
                prev = prev.HasValue && mult.Prev.HasValue ? prev * mult.Prev / mult.Price : null;
            }
            if (item.DivideBy > 0) factor /= item.DivideBy;

            q.Price = raw.Price * factor;
            q.PrevClose = prev * factor;
            q.Series = factor == 1 ? raw.Series : raw.Series.Select(v => v * factor).ToArray();
            q.Stale = false;
        }
        catch
        {
            q.Stale = true; // son bilinen değer soluk gösterilir
        }
    }

    private async Task<Raw> FetchRawAsync(string symbol)
    {
        string url = "https://query1.finance.yahoo.com/v8/finance/chart/" +
                     Uri.EscapeDataString(symbol) + "?interval=5m&range=1d";
        using var resp = await _http.GetAsync(url).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
        var meta = result.GetProperty("meta");

        double price = meta.GetProperty("regularMarketPrice").GetDouble();
        double? prev = null;
        if (meta.TryGetProperty("chartPreviousClose", out var pc) && pc.ValueKind == JsonValueKind.Number)
            prev = pc.GetDouble();

        var series = new List<double>();
        if (result.TryGetProperty("indicators", out var ind) &&
            ind.TryGetProperty("quote", out var qa) && qa.ValueKind == JsonValueKind.Array && qa.GetArrayLength() > 0 &&
            qa[0].TryGetProperty("close", out var closes) && closes.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in closes.EnumerateArray())
                if (c.ValueKind == JsonValueKind.Number) series.Add(c.GetDouble());
        }
        series.Add(price);
        return new Raw(price, prev, series.ToArray());
    }
}
