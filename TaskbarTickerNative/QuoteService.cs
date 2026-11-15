using System.Text;

namespace TaskbarTickerNative;

internal sealed class Quote
{
    public double? Price;
    public double? PrevClose;
    public double[] Series = Array.Empty<double>();
    public bool Stale = true;

    public double? ChangePercent =>
        Price.HasValue && PrevClose is > 0 ? (Price.Value / PrevClose.Value - 1) * 100 : null;
}

/// Bir yenilemenin sonucu; arka plan iş parçacığında hesaplanır, arayüz iş parçacığında Quote'lara uygulanır.
internal readonly record struct QuoteResult(bool Ok, double Price, double? PrevClose, double[] Series);

/// Fiyatları Yahoo Finance'in herkese açık grafik servisinden, Windows'un kendi HTTP istemcisiyle (WinHTTP) çeker.
internal static unsafe class QuoteService
{
    private const string Host = "query1.finance.yahoo.com";

    private readonly record struct Raw(double Price, double? Prev, double[] Series);

    private static nint s_session;
    private static readonly object s_lock = new();

    /// Tüm kalemleri çeker. Engelleyen bir çağrıdır; arka plan iş parçacığından çağrılmalıdır.
    public static QuoteResult[] Fetch(IReadOnlyList<TickerItem> items)
    {
        // Aynı sembol birden çok kalemde geçiyorsa (ör. USDTRY=X) tek istek yeterli.
        var symbols = new List<string>();
        foreach (var item in items)
        {
            if (!symbols.Contains(item.Symbol)) symbols.Add(item.Symbol);
            if (!string.IsNullOrWhiteSpace(item.MultiplyBy) && !symbols.Contains(item.MultiplyBy)) symbols.Add(item.MultiplyBy);
        }

        var raws = new Raw?[symbols.Count];
        var tasks = new Task[symbols.Count];
        for (int i = 0; i < symbols.Count; i++)
        {
            int index = i;
            tasks[i] = Task.Run(() =>
            {
                try { raws[index] = FetchRaw(symbols[index]); }
                catch { raws[index] = null; }
            });
        }
        Task.WaitAll(tasks);

        var results = new QuoteResult[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (raws[symbols.IndexOf(item.Symbol)] is not { } raw) continue;
            double? prev = raw.Prev;

            // Türetilmiş kalemler (ör. gram altın = ons/USD × USD/TRY ÷ 31,1035)
            double factor = 1;
            if (!string.IsNullOrWhiteSpace(item.MultiplyBy))
            {
                if (raws[symbols.IndexOf(item.MultiplyBy)] is not { } mult) continue;
                factor *= mult.Price;
                // Önceki kapanış kendi kuruyla çarpılmalı; aşağıda factor ile çarpılacağı için düzeltiyoruz.
                prev = prev.HasValue && mult.Prev.HasValue ? prev * mult.Prev / mult.Price : null;
            }
            if (item.DivideBy > 0) factor /= item.DivideBy;

            var series = raw.Series;
            if (factor != 1)
            {
                series = new double[raw.Series.Length];
                for (int k = 0; k < series.Length; k++) series[k] = raw.Series[k] * factor;
            }
            results[i] = new QuoteResult(true, raw.Price * factor, prev * factor, series);
        }
        return results;
    }

    private static Raw FetchRaw(string symbol)
    {
        string body = HttpGet("/v8/finance/chart/" + Uri.EscapeDataString(symbol) + "?interval=5m&range=1d");

        var root = Json.Obj(Json.Parse(body)) ?? throw new FormatException();
        var chart = Json.Obj(Json.Get(root, "chart")) ?? throw new FormatException();
        var result = Json.Obj(Json.Arr(Json.Get(chart, "result"))?[0]) ?? throw new FormatException();
        var meta = Json.Obj(Json.Get(result, "meta")) ?? throw new FormatException();

        if (Json.Get(meta, "regularMarketPrice") is not double price) throw new FormatException();
        double? prev = Json.Get(meta, "chartPreviousClose") as double?;

        var series = new List<double>();
        var indicators = Json.Obj(Json.Get(result, "indicators"));
        var quoteArr = indicators != null ? Json.Arr(Json.Get(indicators, "quote")) : null;
        var first = quoteArr is { Count: > 0 } ? Json.Obj(quoteArr[0]) : null;
        var closes = first != null ? Json.Arr(Json.Get(first, "close")) : null;
        if (closes != null)
        {
            foreach (var c in closes)
                if (c is double v) series.Add(v);
        }
        series.Add(price);
        return new Raw(price, prev, series.ToArray());
    }

    private static string HttpGet(string path)
    {
        lock (s_lock)
        {
            if (s_session == 0)
            {
                // 4 = WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY
                s_session = Win32.WinHttpOpen("Mozilla/5.0 (Windows NT 10.0; Win64; x64)", 4, 0, 0, 0);
                if (s_session == 0) throw new IOException("WinHttpOpen");
                Win32.WinHttpSetTimeouts(s_session, 8000, 8000, 12000, 12000);
            }
        }

        nint connect = 0, request = 0;
        try
        {
            connect = Win32.WinHttpConnect(s_session, Host, 443, 0);
            if (connect == 0) throw new IOException("WinHttpConnect");
            request = Win32.WinHttpOpenRequest(connect, "GET", path, 0, 0, 0, 0x00800000 /* WINHTTP_FLAG_SECURE */);
            if (request == 0) throw new IOException("WinHttpOpenRequest");
            if (Win32.WinHttpSendRequest(request, 0, 0, 0, 0, 0, 0) == 0) throw new IOException("WinHttpSendRequest");
            if (Win32.WinHttpReceiveResponse(request, 0) == 0) throw new IOException("WinHttpReceiveResponse");

            // 19 = WINHTTP_QUERY_STATUS_CODE, 0x20000000 = WINHTTP_QUERY_FLAG_NUMBER
            int size = sizeof(int);
            if (Win32.WinHttpQueryHeaders(request, 19 | 0x20000000, 0, out int status, ref size, 0) == 0 || status != 200)
                throw new IOException("HTTP " + status);

            using var ms = new MemoryStream();
            byte* buffer = stackalloc byte[8192];
            while (true)
            {
                if (Win32.WinHttpReadData(request, buffer, 8192, out int read) == 0) throw new IOException("WinHttpReadData");
                if (read == 0) break;
                ms.Write(new ReadOnlySpan<byte>(buffer, read));
            }
            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }
        finally
        {
            if (request != 0) Win32.WinHttpCloseHandle(request);
            if (connect != 0) Win32.WinHttpCloseHandle(connect);
        }
    }
}
