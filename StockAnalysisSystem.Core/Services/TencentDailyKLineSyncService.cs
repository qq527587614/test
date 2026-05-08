using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.Core.Services;

/// <summary>
/// 使用腾讯财经前复权日线接口同步历史 K 线，并可按日期区间先清理再回填。
/// 接口形态参考：<c>proxy.finance.qq.com/.../newfqkline/get</c>，与常见 <c>qfqday</c>/<c>day</c> 字段一致。
/// </summary>
public sealed class TencentDailyKLineSyncService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IStockRepository _stockRepo;
    private readonly HttpClient _http;

    public TencentDailyKLineSyncService(IDbContextFactory<AppDbContext> dbContextFactory, IStockRepository stockRepo)
    {
        _dbContextFactory = dbContextFactory;
        _stockRepo = stockRepo;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://finance.qq.com/");
    }

    public async Task<DailyKLineSyncResult> SyncFromDateAsync(
        DateTime startDate,
        DateTime? endDate = null,
        bool deleteExisting = true,
        int maxConcurrency = 6,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        startDate = startDate.Date;
        var end = (endDate ?? DateTime.Today).Date;
        if (end < startDate) end = startDate;

        using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (deleteExisting)
        {
            progress?.Report($"删除日线数据：{startDate:yyyy-MM-dd}..{end:yyyy-MM-dd} …");
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM stockdailydata WHERE TradeDate >= {0} AND TradeDate <= {1}",
                startDate, end);
        }

        progress?.Report("加载股票列表…");
        var stocks = await _stockRepo.GetAllAsync();
        if (stocks.Count == 0)
            return new DailyKLineSyncResult { StartDate = startDate, EndDate = end, TotalStocks = 0 };

        progress?.Report($"开始同步日线（腾讯前复权）：{stocks.Count} 只，{startDate:yyyy-MM-dd}..{end:yyyy-MM-dd}");

        var inserted = 0;
        var okStocks = 0;
        var failedStocks = 0;
        var emptyStocks = 0;

        using var gate = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var tasks = new List<Task>();
        var batch = new List<StockDailyData>(capacity: 8000);
        var batchLock = new object();
        const int flushThreshold = 6000;

        async Task FlushAsync()
        {
            List<StockDailyData> toSave;
            lock (batchLock)
            {
                if (batch.Count == 0) return;
                toSave = new List<StockDailyData>(batch);
                batch.Clear();
            }

            using var db2 = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            await db2.StockDailyData.AddRangeAsync(toSave, cancellationToken);
            await db2.SaveChangesAsync(cancellationToken);
            Interlocked.Add(ref inserted, toSave.Count);
        }

        var processed = 0;
        foreach (var s in stocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await gate.WaitAsync(cancellationToken);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var idx = Interlocked.Increment(ref processed);
                    if (idx % 200 == 0)
                        progress?.Report($"下载中… {idx}/{stocks.Count}");

                    var code6 = Normalize6(s.StockCode);
                    if (string.IsNullOrEmpty(code6))
                    {
                        Interlocked.Increment(ref failedStocks);
                        return;
                    }

                    var qcode = ToTencentQuoteCode(code6);
                    var bars = await FetchQqDailyBarsAsync(qcode, cancellationToken);
                    var filtered = bars
                        .Where(b => b.TradeDate >= startDate && b.TradeDate <= end)
                        .OrderBy(b => b.TradeDate)
                        .ToList();

                    if (filtered.Count == 0)
                    {
                        Interlocked.Increment(ref emptyStocks);
                        return;
                    }

                    var rows = new List<StockDailyData>(filtered.Count);
                    decimal? prevClose = null;
                    foreach (var b in filtered)
                    {
                        decimal? pct = null;
                        if (prevClose.HasValue && prevClose.Value != 0)
                            pct = Math.Round((b.Close - prevClose.Value) / prevClose.Value * 100m, 4, MidpointRounding.AwayFromZero);
                        prevClose = b.Close;

                        rows.Add(new StockDailyData
                        {
                            StockID = s.StockID,
                            StockCode = code6,
                            TradeDate = b.TradeDate,
                            OpenPrice = b.Open,
                            HighPrice = b.High,
                            LowPrice = b.Low,
                            ClosePrice = b.Close,
                            Volume = b.Volume,
                            Amount = b.Amount,
                            ChangePercent = pct,
                            TurnoverRate = b.TurnoverRate,
                            CurrentPrice = b.Close,
                            BeforDate = b.PrevTradeDate,
                            CreatedTime = DateTime.Now
                        });
                    }

                    var shouldFlush = false;
                    lock (batchLock)
                    {
                        batch.AddRange(rows);
                        shouldFlush = batch.Count >= flushThreshold;
                    }

                    if (shouldFlush)
                        await FlushAsync();

                    Interlocked.Increment(ref okStocks);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failedStocks);
                    ErrorLogger.Log(ex, "TencentDailyKLineSyncService.SyncFromDateAsync", new { stock = s.StockCode });
                }
                finally
                {
                    gate.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        await FlushAsync();

        progress?.Report($"同步完成：成功{okStocks}只 / 无数据{emptyStocks}只 / 失败{failedStocks}只，新增{inserted}条日线。");
        return new DailyKLineSyncResult
        {
            StartDate = startDate,
            EndDate = end,
            TotalStocks = stocks.Count,
            OkStocks = okStocks,
            EmptyStocks = emptyStocks,
            FailedStocks = failedStocks,
            InsertedRows = inserted
        };
    }

    /// <summary>与旧代码一致：60→sh；00/30→sz；68（含688）→sh。</summary>
    public static string ToTencentQuoteCode(string code6)
    {
        if (string.IsNullOrEmpty(code6) || code6.Length < 2)
            return code6;
        if (code6.StartsWith("60", StringComparison.Ordinal) || code6.StartsWith("68", StringComparison.Ordinal))
            return "sh" + code6;
        if (code6.StartsWith("00", StringComparison.Ordinal) || code6.StartsWith("30", StringComparison.Ordinal))
            return "sz" + code6;
        // 其他（北交所等）：尝试按常见规则
        if (code6.StartsWith("43", StringComparison.Ordinal) || code6.StartsWith("83", StringComparison.Ordinal) ||
            code6.StartsWith("87", StringComparison.Ordinal) || code6.StartsWith("92", StringComparison.Ordinal))
            return "bj" + code6;
        return "sz" + code6;
    }

    private static string Normalize6(string? stockCode)
    {
        if (string.IsNullOrWhiteSpace(stockCode)) return "";
        var c = stockCode.Trim().ToLowerInvariant().Replace("sh", "").Replace("sz", "").Replace("bj", "");
        if (c.Length == 6) return c;
        if (c.Length < 6) return c.PadLeft(6, '0');
        return c[^6..];
    }

    private async Task<List<QqDailyBar>> FetchQqDailyBarsAsync(string qcode, CancellationToken cancellationToken)
    {
        var r = Random.Shared.NextDouble().ToString("0.################", CultureInfo.InvariantCulture);
        var url =
            "https://proxy.finance.qq.com/ifzqgtimg/appstock/app/newfqkline/get" +
            $"?_var=kline_dayqfq&param={qcode},day,,,2000,qfq&r={r}";

        string raw;
        try
        {
            raw = await _http.GetStringAsync(url, cancellationToken);
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "TencentDailyKLineSyncService.FetchQqDailyBarsAsync", url);
            return new List<QqDailyBar>();
        }

        var json = ExtractJsonObject(raw);
        if (string.IsNullOrEmpty(json))
            return new List<QqDailyBar>();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "TencentDailyKLineSyncService.FetchQqDailyBarsAsync.Parse", json[..Math.Min(200, json.Length)]);
            return new List<QqDailyBar>();
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number && codeEl.GetInt32() != 0)
                return new List<QqDailyBar>();

            if (!root.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object)
                return new List<QqDailyBar>();

            JsonElement? klines = null;
            foreach (var prop in dataEl.EnumerateObject())
            {
                var o = prop.Value;
                if (o.ValueKind != JsonValueKind.Object)
                    continue;
                if (o.TryGetProperty("qfqday", out var qfq) && qfq.ValueKind == JsonValueKind.Array && qfq.GetArrayLength() > 0)
                {
                    klines = qfq;
                    break;
                }
                if (o.TryGetProperty("day", out var day) && day.ValueKind == JsonValueKind.Array && day.GetArrayLength() > 0)
                {
                    klines = day;
                    break;
                }
            }

            if (klines == null || klines.Value.ValueKind != JsonValueKind.Array)
                return new List<QqDailyBar>();

            var parsedBars = ParseQqKlineArray(klines.Value);
            return SortAndLinkPreviousTradeDate(parsedBars);
        }
    }

    private static List<QqDailyBar> SortAndLinkPreviousTradeDate(List<QqDailyBar> bars)
    {
        if (bars.Count <= 1)
            return bars;

        bars.Sort((a, b) => a.TradeDate.CompareTo(b.TradeDate));
        var linked = new List<QqDailyBar>(bars.Count);
        DateTime? prevDate = null;
        foreach (var b in bars)
        {
            linked.Add(new QqDailyBar
            {
                TradeDate = b.TradeDate,
                PrevTradeDate = prevDate,
                Open = b.Open,
                High = b.High,
                Low = b.Low,
                Close = b.Close,
                Volume = b.Volume,
                TurnoverRate = b.TurnoverRate,
                Amount = b.Amount
            });
            prevDate = b.TradeDate;
        }

        return linked;
    }

    private static string ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var i = raw.IndexOf('{');
        if (i < 0) return "";
        return raw[i..];
    }

    /// <summary>
    /// 每行：[0]日期 [1]开 [2]收 [3]高 [4]低 [5]量 [6]? [7]换手 [8]额 —— 与现有旧代码下标一致。
    /// </summary>
    private static List<QqDailyBar> ParseQqKlineArray(JsonElement arr)
    {
        var list = new List<QqDailyBar>(arr.GetArrayLength());

        foreach (var row in arr.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 6)
                continue;

            var d = ParseDate(GetArrayString(row, 0));
            if (!d.HasValue) continue;

            var open = ParseDecimalElement(row[1]);
            var close = ParseDecimalElement(row[2]);
            var high = ParseDecimalElement(row[3]);
            var low = ParseDecimalElement(row[4]);
            var vol = ParseDecimalElement(row[5]);
            var turnover = row.GetArrayLength() > 7 ? ParseDecimalElement(row[7]) : 0m;
            var amount = row.GetArrayLength() > 8 ? ParseDecimalElement(row[8]) : 0m;

            list.Add(new QqDailyBar
            {
                TradeDate = d.Value,
                PrevTradeDate = null,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = vol,
                TurnoverRate = turnover,
                Amount = amount
            });
        }

        return list;
    }

    private static string? GetArrayString(JsonElement row, int index)
    {
        if (index >= row.GetArrayLength()) return null;
        var el = row[index];
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            _ => el.ToString()
        };
    }

    private static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.Date;
        if (s.Length == 8 && int.TryParse(s[..4], out var y) && int.TryParse(s.Substring(4, 2), out var m) &&
            int.TryParse(s.Substring(6, 2), out var d))
            return new DateTime(y, m, d);
        return null;
    }

    private static decimal ParseDecimalElement(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m,
            JsonValueKind.Number => el.TryGetDecimal(out var d) ? d : 0m,
            _ => 0m
        };
    }

    private sealed class QqDailyBar
    {
        public DateTime TradeDate { get; init; }
        public DateTime? PrevTradeDate { get; init; }
        public decimal Open { get; init; }
        public decimal High { get; init; }
        public decimal Low { get; init; }
        public decimal Close { get; init; }
        public decimal Volume { get; init; }
        public decimal TurnoverRate { get; init; }
        public decimal Amount { get; init; }
    }
}

public sealed class DailyKLineSyncResult
{
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public int TotalStocks { get; init; }
    public int OkStocks { get; init; }
    public int EmptyStocks { get; init; }
    public int FailedStocks { get; init; }
    public int InsertedRows { get; init; }
}
