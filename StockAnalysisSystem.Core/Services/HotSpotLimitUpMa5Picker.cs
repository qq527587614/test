using System.Threading;
using Microsoft.EntityFrameworkCore;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.RealtimeData;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.Core.Services;

/// <summary>
/// 热点选股（与界面说明一致）：
/// <list type="bullet">
/// <item>涨停表近 30 个自然日内出现过涨停；</item>
/// <item>自窗口内<strong>首次</strong>涨停日起至评估日前一交易日：用日线 <c>CurrentPrice</c>（无则 <c>ClosePrice</c>）计算 MA5，不破天数占比 ≥ 80%；</item>
/// <item>评估日当天：必须用<strong>实时现价</strong>计算 MA5（前 4 日同口径之和 + 现价）/5，且现价不得跌破该实时 MA5；无有效实时价则剔除。</item>
/// <item>截止评估日（含）最近两个<strong>交易日</strong>内曾在涨停表中出现过的股票剔除（避免连板追高；含评估日涨停）。</item>
/// <item>该区间内收盘价从未低于当日 MA10；</item>
/// <item>评估日涨停价：优先用涨停表当日 close，否则按昨收 × 涨停幅度推算；含涨停假设 MA5 =（前 4 日收盘之和 + 涨停价）/ 5。</item>
/// </list>
/// 实现侧重：少往返、少扫大表、并行拉日线、内存侧 O(n) 均线判定。
/// </summary>
public sealed class HotSpotLimitUpMa5Picker
{
    private readonly AppDbContext _db;
    private readonly IStockDailyDataRepository _dailyRepo;
    private readonly TencentRealtimeService _realtime;

    public HotSpotLimitUpMa5Picker(
        AppDbContext db,
        IStockDailyDataRepository dailyRepo,
        TencentRealtimeService realtime)
    {
        _db = db;
        _dailyRepo = dailyRepo;
        _realtime = realtime;
    }

    /// <summary>腾讯行情接口用的前缀代码（与日线同步规则一致）。</summary>
    private static string ToTencentQuoteCode(string code6)
    {
        if (string.IsNullOrWhiteSpace(code6) || code6.Length < 2)
            return "sz" + code6;
        if (code6.StartsWith("60", StringComparison.Ordinal) || code6.StartsWith("68", StringComparison.Ordinal))
            return "sh" + code6;
        if (code6.StartsWith("00", StringComparison.Ordinal) || code6.StartsWith("30", StringComparison.Ordinal))
            return "sz" + code6;
        if (code6.StartsWith("43", StringComparison.Ordinal) || code6.StartsWith("83", StringComparison.Ordinal) ||
            code6.StartsWith("87", StringComparison.Ordinal) || code6.StartsWith("92", StringComparison.Ordinal))
            return "bj" + code6;
        return "sz" + code6;
    }

    /// <summary>
    /// 涨停表 <c>code</c> → 与 <c>stockdailydata.StockCode</c> / <c>stockinfo.StockCode</c> 对齐。
    /// </summary>
    public static string LimitTableCodeToDailyStockCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var c = code.Trim();

        var dotIdx = c.LastIndexOf('.');
        if (dotIdx > 0 && dotIdx < c.Length - 1)
        {
            var suf = c[(dotIdx + 1)..];
            if (suf.Equals("SH", StringComparison.OrdinalIgnoreCase) ||
                suf.Equals("SZ", StringComparison.OrdinalIgnoreCase) ||
                suf.Equals("BJ", StringComparison.OrdinalIgnoreCase))
                c = c[..dotIdx].Trim();
        }

        if (c.Length > 2 && (c.StartsWith("sh", StringComparison.OrdinalIgnoreCase) ||
                             c.StartsWith("sz", StringComparison.OrdinalIgnoreCase) ||
                             c.StartsWith("bj", StringComparison.OrdinalIgnoreCase)))
            c = c[2..].Trim();

        if (c.Length > 0 && c.All(char.IsDigit))
        {
            if (c.Length < 6)
                return c.PadLeft(6, '0');
            return c;
        }

        return c;
    }

    public static string LimitTableCodeToStockCode(string code) => LimitTableCodeToDailyStockCode(code);

    public static string StockCodeToLimitTableCode(string stockCode)
    {
        if (string.IsNullOrWhiteSpace(stockCode)) return stockCode;
        var s = stockCode.Trim();
        if (s.StartsWith("6", StringComparison.Ordinal)) return "sh" + s;
        return "sz" + s;
    }

    public static decimal GetLimitUpPercent(string stockCode)
    {
        if (stockCode.StartsWith("60", StringComparison.Ordinal) || stockCode.StartsWith("00", StringComparison.Ordinal))
            return 10m;
        if (stockCode.StartsWith("30", StringComparison.Ordinal))
            return 20m;
        if (stockCode.StartsWith("688", StringComparison.Ordinal))
            return 20m;
        return 10m;
    }

    public async Task<List<HotSpotPickRow>> PickAsync(DateTime? asOfDate = null, IProgress<string>? progress = null)
    {
        var evalCal = (asOfDate ?? DateTime.Today).Date;
        var latestTrade = await _dailyRepo.GetLatestTradeDateAsync();
        if (!latestTrade.HasValue)
        {
            progress?.Report("无日线数据，请先同步日线。");
            return new List<HotSpotPickRow>();
        }

        var evalDate = evalCal > latestTrade.Value.Date ? latestTrade.Value.Date : evalCal;
        evalDate = evalDate.Date;
        var windowStart = evalDate.AddDays(-30).Date;
        var windowEndExclusive = evalDate.AddDays(1);

        progress?.Report("加载股票代码表…");
        var codeIndex = await BuildStockCodeIndexAsync();

        progress?.Report("读取近一月涨停记录…");
        var limitRows = await _db.StockLimitUpAnalysis
            .AsNoTracking()
            .Where(s => s.analysis_date >= windowStart && s.analysis_date < windowEndExclusive)
            .Select(s => new LimitRowLite(s.code, s.analysis_date, s.close, s.name))
            .ToListAsync();

        var agg = new Dictionary<string, LimitAgg>(StringComparer.Ordinal);
        foreach (var row in limitRows)
        {
            var key = LimitTableCodeToDailyStockCode(row.Code);
            if (string.IsNullOrEmpty(key)) continue;
            if (!agg.TryGetValue(key, out var a))
            {
                agg[key] = new LimitAgg(row.AnalysisDate, row.Name ?? "");
                continue;
            }

            if (row.AnalysisDate < a.FirstInWindow)
            {
                a.FirstInWindow = row.AnalysisDate;
                if (!string.IsNullOrWhiteSpace(row.Name))
                    a.NameHint = row.Name!;
            }
        }

        if (agg.Count == 0)
        {
            progress?.Report("近一月涨停表无数据。");
            return new List<HotSpotPickRow>();
        }

        progress?.Report($"涨停去重 {agg.Count} 只，匹配股票主数据…");

        var limitCloseOnEval = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var row in limitRows)
        {
            if (row.AnalysisDate.Date != evalDate || row.Close is not { } cx || cx <= 0) continue;
            var k = LimitTableCodeToDailyStockCode(row.Code);
            if (string.IsNullOrEmpty(k)) continue;
            if (!limitCloseOnEval.TryGetValue(k, out var cur) || cx > cur)
                limitCloseOnEval[k] = cx;
        }

        var lastLimitDateByCode6 = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var row in limitRows)
        {
            var k = LimitTableCodeToDailyStockCode(row.Code);
            if (string.IsNullOrEmpty(k)) continue;
            var d = row.AnalysisDate.Date;
            if (!lastLimitDateByCode6.TryGetValue(k, out var mx) || d > mx)
                lastLimitDateByCode6[k] = d;
        }

        var candidates = new List<(string Code6, DateTime First, string NameHint, StockInfo Stock)>(agg.Count);
        var missMap = 0;
        foreach (var (code6, a) in agg)
        {
            if (!TryResolveStock(codeIndex, code6, out var stock))
            {
                missMap++;
                continue;
            }

            candidates.Add((code6, a.FirstInWindow.Date, a.NameHint, stock));
        }

        if (candidates.Count == 0)
        {
            progress?.Report($"无匹配股票（涨停 {agg.Count} 只，代码表未命中 {missMap}）。");
            return new List<HotSpotPickRow>();
        }

        var stockIds = candidates.Select(c => c.Stock.StockID).Distinct(StringComparer.Ordinal).ToList();
        var dataLoadStart = windowStart.AddDays(-80).Date;

        progress?.Report($"加载日线 {stockIds.Count} 只（并行）…");
        var allBars = await LoadDailyBarsParallelAsync(stockIds, dataLoadStart, evalDate, progress);

        var barsById = new Dictionary<string, List<StockDailyData>>(StringComparer.Ordinal);
        foreach (var b in allBars)
        {
            if (!barsById.TryGetValue(b.StockID, out var list))
            {
                list = new List<StockDailyData>(96);
                barsById[b.StockID] = list;
            }

            list.Add(b);
        }

        foreach (var kv in barsById)
        {
            if (kv.Value.Count > 1)
                kv.Value.Sort((x, y) => x.TradeDate.CompareTo(y.TradeDate));
        }

        progress?.Report("获取实时行情（评估日实时 MA5）…");
        Dictionary<string, decimal> realtimePxByCode6;
        try
        {
            var qcodes = candidates
                .Select(c => ToTencentQuoteCode(c.Stock.StockCode))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            realtimePxByCode6 = await FetchRealtimePricesBatchedAsync(qcodes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "HotSpotLimitUpMa5Picker.PickAsync.Realtime", "");
            realtimePxByCode6 = new Dictionary<string, decimal>(StringComparer.Ordinal);
        }

        var tradeDatesForRecent = await _dailyRepo.GetTradeDatesAsync(evalDate.AddDays(-60), evalDate);
        // 含评估日在内：例如评估日为 5/8 则取 5/7、5/8 两个交易日；仅排除「之前」不含当日会漏掉评估日涨停。
        var lastTwoTradingDaysThroughEval = tradeDatesForRecent
            .Select(d => d.Date)
            .Where(d => d <= evalDate)
            .Distinct()
            .OrderBy(d => d)
            .TakeLast(2)
            .ToHashSet();

        var code6LimitOnRecentTwoTradingDays = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in limitRows)
        {
            var k = LimitTableCodeToDailyStockCode(row.Code);
            if (string.IsNullOrEmpty(k)) continue;
            if (lastTwoTradingDaysThroughEval.Contains(row.AnalysisDate.Date))
                code6LimitOnRecentTwoTradingDays.Add(k);
        }

        var results = new List<HotSpotPickRow>();
        var missBars = 0;
        var failTrend = 0;
        var missEval = 0;
        var missRealtime = 0;
        var skipRecentTwoLimit = 0;
        var n = candidates.Count;
        for (var i = 0; i < n; i++)
        {
            if ((i & 127) == 0 && i > 0)
                progress?.Report($"筛选 {i}/{n}…");

            var (code6, firstLimit, nameHint, stock) = candidates[i];
            if (code6LimitOnRecentTwoTradingDays.Contains(code6))
            {
                skipRecentTwoLimit++;
                continue;
            }

            if (!barsById.TryGetValue(stock.StockID, out var bars) || bars.Count < 10)
            {
                missBars++;
                continue;
            }

            var lastIdx = bars.Count - 1;
            if (bars[lastIdx].TradeDate.Date != evalDate)
            {
                missEval++;
                continue;
            }

            var anchorIdx = LowerBoundByDate(bars, firstLimit);
            if (anchorIdx < 0 || anchorIdx > lastIdx)
                continue;

            realtimePxByCode6.TryGetValue(stock.StockCode, out var rtPx);
            if (rtPx <= 0m)
            {
                missRealtime++;
                continue;
            }

            if (!PassTrendFilters(bars, anchorIdx, lastIdx, rtPx))
            {
                failTrend++;
                continue;
            }

            if (lastIdx < 4)
            {
                missBars++;
                continue;
            }

            var prev4CloseSum =
                CloseLike(bars[lastIdx - 1]) +
                CloseLike(bars[lastIdx - 2]) +
                CloseLike(bars[lastIdx - 3]) +
                CloseLike(bars[lastIdx - 4]);
            var prevClose = CloseLike(bars[lastIdx - 1]);
            var limitPx = ResolveTodayLimitPrice(code6, limitCloseOnEval, prevClose);
            var ma5WithLimit = (prev4CloseSum + limitPx) / 5m;
            if (ma5WithLimit <= 0)
                continue;

            var dispName = string.IsNullOrWhiteSpace(stock.StockName) ? nameHint : stock.StockName;
            var recentLimit = lastLimitDateByCode6.TryGetValue(code6, out var rld) ? rld : firstLimit;
            var (periodGainPct, pullbackFromHighPct) = CalcPeriodGainAndPullback(bars, anchorIdx, lastIdx);

            results.Add(new HotSpotPickRow
            {
                StockId = stock.StockID,
                StockCode = stock.StockCode,
                StockName = dispName ?? "",
                FirstLimitUpInWindow = firstLimit,
                RecentLimitUpDate = recentLimit,
                PeriodGainPercent = periodGainPct,
                PullbackFromPeriodHighPercent = pullbackFromHighPct,
                LastTradeDate = bars[lastIdx].TradeDate.Date,
                TodayLimitPrice = limitPx,
                Ma5WithTodayLimit = ma5WithLimit,
                Prev4CloseSum = prev4CloseSum
            });
        }

        progress?.Report(
            $"完成 {results.Count} 只。未匹配代码表={missMap} 无K线={missBars} 非评估日收盘={missEval} 无实时价={missRealtime} 趋势未过={failTrend} 含评估日在内近两交易日曾涨停剔除={skipRecentTwoLimit}");
        return results;
    }

    private async Task<Dictionary<string, decimal>> FetchRealtimePricesBatchedAsync(List<string> qcodes)
    {
        var map = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (qcodes.Count == 0)
            return map;

        const int batchSize = 80;
        for (var i = 0; i < qcodes.Count; i += batchSize)
        {
            var take = Math.Min(batchSize, qcodes.Count - i);
            var batch = qcodes.GetRange(i, take);
            var list = await _realtime.GetRealtimeDataAsync(batch).ConfigureAwait(false);
            foreach (var x in list)
            {
                if (string.IsNullOrWhiteSpace(x.StockCode) || x.CurrentPrice <= 0m)
                    continue;
                map[x.StockCode] = x.CurrentPrice;
            }
        }

        return map;
    }

    private async Task<Dictionary<string, CodeIndexEntry>> BuildStockCodeIndexAsync()
    {
        var rows = await _db.StockInfos
            .AsNoTracking()
            .Select(s => new { s.StockID, s.StockCode, s.StockName })
            .ToListAsync();

        var index = new Dictionary<string, CodeIndexEntry>(StringComparer.Ordinal);
        foreach (var s in rows)
        {
            var raw = (s.StockCode ?? "").Trim();
            if (string.IsNullOrEmpty(raw)) continue;

            var variants = new HashSet<string>(StringComparer.Ordinal);
            variants.Add(raw);
            if (raw.All(char.IsDigit) && raw.Length < 6)
                variants.Add(raw.PadLeft(6, '0'));
            var tr = raw.TrimStart('0');
            if (tr.Length > 0 && tr != raw)
                variants.Add(tr);

            foreach (var v in variants)
            {
                if (!index.ContainsKey(v))
                    index[v] = new CodeIndexEntry(s.StockID, s.StockCode ?? raw, s.StockName ?? "");
            }
        }

        return index;
    }

    private static bool TryResolveStock(
        Dictionary<string, CodeIndexEntry> index,
        string code6,
        out StockInfo stock)
    {
        foreach (var v in ExpandStockCodesForDbLookup(new[] { code6 }))
        {
            if (index.TryGetValue(v, out var e))
            {
                stock = new StockInfo
                {
                    StockID = e.StockId,
                    StockCode = e.StockCode,
                    StockName = e.StockName,
                    Market = "",
                    CreatedTime = default
                };
                return true;
            }
        }

        stock = null!;
        return false;
    }

    private static IEnumerable<string> ExpandStockCodesForDbLookup(IEnumerable<string> canonicalCodes)
    {
        foreach (var canonical in canonicalCodes)
        {
            if (string.IsNullOrEmpty(canonical))
                continue;
            yield return canonical;
            var trimmed = canonical.TrimStart('0');
            if (trimmed.Length > 0 && !string.Equals(trimmed, canonical, StringComparison.Ordinal))
                yield return trimmed;
        }
    }

    private async Task<List<StockDailyData>> LoadDailyBarsParallelAsync(
        IReadOnlyList<string> stockIds,
        DateTime startDate,
        DateTime endDate,
        IProgress<string>? progress)
    {
        if (stockIds.Count == 0)
            return new List<StockDailyData>();

        var idList = stockIds as List<string> ?? stockIds.ToList();

        const int chunkSize = 450;
        const int maxParallel = 8;
        var chunks = new List<List<string>>();
        for (var i = 0; i < idList.Count; i += chunkSize)
            chunks.Add(idList.GetRange(i, Math.Min(chunkSize, idList.Count - i)));

        var gate = new SemaphoreSlim(maxParallel);
        var tasks = chunks.Select(async (batch, idx) =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                progress?.Report($"日线批次 {idx + 1}/{chunks.Count}（{batch.Count} 只）…");
                return await _dailyRepo.GetByStockIdsAndDateRangeAsync(batch, startDate, endDate).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });

        var parts = await Task.WhenAll(tasks).ConfigureAwait(false);
        var merged = new List<StockDailyData>(parts.Sum(p => p.Count));
        foreach (var p in parts)
            merged.AddRange(p);
        return merged;
    }

    private static int LowerBoundByDate(List<StockDailyData> bars, DateTime anchorDate)
    {
        var ad = anchorDate.Date;
        var lo = 0;
        var hi = bars.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (bars[mid].TradeDate.Date < ad)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo < bars.Count ? lo : -1;
    }

    private static decimal ResolveTodayLimitPrice(
        string dailyStockCode,
        IReadOnlyDictionary<string, decimal> limitCloseOnEvalDate,
        decimal prevClose)
    {
        if (limitCloseOnEvalDate.TryGetValue(dailyStockCode, out var fromTable))
            return decimal.Round(fromTable, 2, MidpointRounding.AwayFromZero);

        var pct = GetLimitUpPercent(dailyStockCode);
        var raw = prevClose * (1m + pct / 100m);
        return decimal.Round(raw, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// MA10：区间内用日线口径价不破；
    /// MA5：评估日<strong>之前</strong>的交易日用日线 MA5；评估日必须用 <paramref name="evalDayRealtimePx"/> 计算实时 MA5 并判定不破，且整体 MA5「不破」占比 ≥ 80%。
    /// </summary>
    private static bool PassTrendFilters(List<StockDailyData> bars, int anchorIdx, int lastIdx, decimal evalDayRealtimePx)
    {
        if (lastIdx < 9) return false;

        var start5 = Math.Max(anchorIdx, 4);
        var start10 = Math.Max(anchorIdx, 9);

        for (var i = start10; i <= lastIdx; i++)
        {
            var ma10 = Sum10Close(bars, i) / 10m;
            if (CloseLike(bars[i]) < ma10)
                return false;
        }

        var total5 = 0;
        var ok5 = 0;
        for (var i = start5; i <= lastIdx - 1; i++)
        {
            var ma5 = Sum5Close(bars, i) / 5m;
            total5++;
            if (CloseLike(bars[i]) >= ma5)
                ok5++;
        }

        if (evalDayRealtimePx <= 0m)
            return false;

        var prev4 =
            CloseLike(bars[lastIdx - 1]) +
            CloseLike(bars[lastIdx - 2]) +
            CloseLike(bars[lastIdx - 3]) +
            CloseLike(bars[lastIdx - 4]);
        var rtMa5 = (prev4 + evalDayRealtimePx) / 5m;
        total5++;
        if (evalDayRealtimePx < rtMa5)
            return false;
        ok5++;

        if (total5 <= 0) return false;
        return (decimal)ok5 / total5 >= 0.80m;
    }

    private static decimal CloseLike(StockDailyData bar) => bar.CurrentPrice ?? bar.ClosePrice;

    /// <summary>自窗口内首涨停日（锚点）至评估日：收盘涨幅%、相对周期内最高价回落%。</summary>
    private static (decimal PeriodGainPercent, decimal PullbackFromHighPercent) CalcPeriodGainAndPullback(
        List<StockDailyData> bars,
        int anchorIdx,
        int lastIdx)
    {
        var startPx = CloseLike(bars[anchorIdx]);
        var endPx = CloseLike(bars[lastIdx]);
        var gain = 0m;
        if (startPx > 0m)
            gain = (endPx - startPx) / startPx * 100m;

        var maxHigh = 0m;
        for (var j = anchorIdx; j <= lastIdx; j++)
        {
            if (bars[j].HighPrice > maxHigh)
                maxHigh = bars[j].HighPrice;
        }

        var pullback = 0m;
        if (maxHigh > 0m)
            pullback = (maxHigh - endPx) / maxHigh * 100m;

        return (
            decimal.Round(gain, 2, MidpointRounding.AwayFromZero),
            decimal.Round(Math.Max(0m, pullback), 2, MidpointRounding.AwayFromZero));
    }

    private static decimal Sum5Close(List<StockDailyData> bars, int i) =>
        CloseLike(bars[i]) + CloseLike(bars[i - 1]) + CloseLike(bars[i - 2]) + CloseLike(bars[i - 3]) +
        CloseLike(bars[i - 4]);

    private static decimal Sum10Close(List<StockDailyData> bars, int i) =>
        CloseLike(bars[i]) + CloseLike(bars[i - 1]) + CloseLike(bars[i - 2]) + CloseLike(bars[i - 3]) +
        CloseLike(bars[i - 4]) + CloseLike(bars[i - 5]) + CloseLike(bars[i - 6]) + CloseLike(bars[i - 7]) +
        CloseLike(bars[i - 8]) + CloseLike(bars[i - 9]);

    private readonly record struct LimitRowLite(string? Code, DateTime AnalysisDate, decimal? Close, string? Name);

    private sealed class LimitAgg
    {
        public DateTime FirstInWindow;
        public string NameHint;

        public LimitAgg(DateTime firstInWindow, string nameHint)
        {
            FirstInWindow = firstInWindow;
            NameHint = nameHint;
        }
    }

    private readonly record struct CodeIndexEntry(string StockId, string StockCode, string StockName);
}

public sealed class HotSpotPickRow
{
    public string StockId { get; init; } = "";
    public string StockCode { get; init; } = "";
    public string StockName { get; init; } = "";
    public DateTime FirstLimitUpInWindow { get; init; }
    /// <summary>近一月涨停表内该股票最后一次出现涨停的日期。</summary>
    public DateTime RecentLimitUpDate { get; init; }
    /// <summary>自窗口内首涨停日（日线锚点）至评估日收盘的涨跌幅（%）。</summary>
    public decimal PeriodGainPercent { get; init; }
    /// <summary>评估日收盘相对「首涨停日至评估日」区间内最高价的回落幅度（%，非负）。</summary>
    public decimal PullbackFromPeriodHighPercent { get; init; }
    public DateTime LastTradeDate { get; init; }
    public decimal TodayLimitPrice { get; init; }
    public decimal Ma5WithTodayLimit { get; init; }
    public decimal Prev4CloseSum { get; init; }
}
