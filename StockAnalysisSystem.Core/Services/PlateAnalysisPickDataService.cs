using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.RealtimeData;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.Core.Services;

/// <summary>
/// 与「选股 → 板块分析」页一致的数据准备：财联社 cls 同步指定日涨停、按涨停映射 + 当日实时/日线聚合板块并排序。
/// </summary>
public sealed class PlateAnalysisPickDataService
{
    private readonly AppDbContext _db;
    private readonly TencentRealtimeService _tencent;

    public PlateAnalysisPickDataService(AppDbContext db, TencentRealtimeService tencent)
    {
        _db = db;
        _tencent = tencent;
    }

    /// <summary>
    /// 从财联社接口同步指定日涨停到 <see cref="StockLimitUpAnalysis"/>（与板块分析页「同步涨停数据」一致）。
    /// </summary>
    /// <returns>写入条数；失败返回 -1。</returns>
    public async Task<int> SyncClsLimitUpForDateAsync(DateTime date, CancellationToken ct = default)
    {
        var dateStr = date.ToString("yyyyMMdd");
        var url = $"https://x-quote.cls.cn/v2/quote/a/plate/up_down_analysis?date={dateStr}";

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            var json = await http.GetStringAsync(new Uri(url), ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return -1;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var root = JsonSerializer.Deserialize<ClsLimitUpApiResponse>(json, options);
            if (root?.data?.plate_stock == null || root.data.plate_stock.Length == 0)
                return 0;

            var targetDate = DateTime.ParseExact(dateStr, "yyyyMMdd", CultureInfo.InvariantCulture);
            var existing = await _db.StockLimitUpAnalysis
                .Where(s => s.analysis_date.Date == targetDate.Date)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (existing.Count > 0)
            {
                _db.StockLimitUpAnalysis.RemoveRange(existing);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            var stockList = new List<StockLimitUpAnalysis>();
            foreach (var plate in root.data.plate_stock)
            {
                if (plate.stock_list == null) continue;
                foreach (var stock in plate.stock_list)
                {
                    if (stock.time == "--") continue;

                    stockList.Add(new StockLimitUpAnalysis
                    {
                        code = stock.secu_code ?? "",
                        name = stock.secu_name ?? "",
                        close = stock.last_px,
                        pct_chg = stock.change,
                        turn = stock.cmc,
                        plate_code = plate.secu_code,
                        plate_name = plate.secu_name,
                        first_limit_up_time = stock.time,
                        last_limit_up_time = stock.time,
                        analysis_date = date.Date,
                        created_time = DateTime.Now,
                        updated_time = DateTime.Now
                    });
                }
            }

            if (stockList.Count > 0)
            {
                await _db.StockLimitUpAnalysis.AddRangeAsync(stockList, ct).ConfigureAwait(false);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            _db.ChangeTracker.Clear();
            return stockList.Count;
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, nameof(PlateAnalysisPickDataService), $"SyncClsLimitUp {dateStr}");
            return -1;
        }
    }

    /// <summary>
    /// 按板块分析页逻辑：全表涨停映射 → 分析日实时或日线 → 分板块平均涨幅排序，取前若干板块名（家数≥阈值，剔除归类/ST 等）。
    /// </summary>
    public async Task<IReadOnlyList<string>> GetHotPlatesByPlateAnalysisAsync(
        DateTime sessionDate,
        int maxPlates,
        int minStocksPerPlate,
        CancellationToken ct = default)
    {
        var tradeDate = sessionDate.Date;
        var isToday = tradeDate == DateTime.Today;

        var limitUpStocks = await _db.StockLimitUpAnalysis.AsNoTracking()
            .Select(s => new { s.code, s.plate_name })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var stockToPlate = limitUpStocks
            .Where(s => !string.IsNullOrEmpty(s.code) && !string.IsNullOrEmpty(s.plate_name))
            .Select(s => new { code = ToCode6Digits(s.code), plate_name = s.plate_name })
            .Where(x => x.code.Length > 0)
            .GroupBy(s => s.code, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => string.Join(";", g.Select(x => x.plate_name).Distinct(StringComparer.Ordinal)),
                StringComparer.Ordinal);

        if (stockToPlate.Count == 0)
            return Array.Empty<string>();

        List<(string Code6, string Name, decimal Chg, decimal Amount, decimal? Turn)> rows;

        if (isToday)
        {
            var codes = stockToPlate.Keys.ToList();
            var stockInfoDict = await _db.StockInfos.AsNoTracking()
                .Where(s => codes.Contains(s.StockCode))
                .ToDictionaryAsync(s => s.StockCode, s => s.StockName, StringComparer.Ordinal, ct)
                .ConfigureAwait(false);

            var prefixCodes = codes.Select(ToTencentPrefix).ToList();
            const int batchSize = 100;
            var allRt = new List<RealtimeStockData>();
            for (var i = 0; i < prefixCodes.Count; i += batchSize)
            {
                ct.ThrowIfCancellationRequested();
                var batch = prefixCodes.Skip(i).Take(batchSize).ToList();
                var batchResults = await _tencent.GetRealtimeDataAsync(batch).ConfigureAwait(false);
                allRt.AddRange(batchResults);
            }

            rows = allRt
                .Where(r => codes.Contains(r.StockCode, StringComparer.Ordinal))
                .Select(r => (
                    Code6: r.StockCode,
                    Name: stockInfoDict.GetValueOrDefault(r.StockCode, r.StockName ?? ""),
                    Chg: r.ChangePercent,
                    Amount: r.Amount,
                    Turn: (decimal?)r.TurnoverRate))
                .ToList();
        }
        else
        {
            var codes = stockToPlate.Keys.ToList();
            var stockInfoDict = await _db.StockInfos.AsNoTracking()
                .Where(s => codes.Contains(s.StockCode))
                .ToDictionaryAsync(s => s.StockCode, s => s.StockName, StringComparer.Ordinal, ct)
                .ConfigureAwait(false);

            var dailyData = await _db.StockDailyData.AsNoTracking()
                .Where(d => d.TradeDate.Date == tradeDate && d.ChangePercent.HasValue && codes.Contains(d.StockCode))
                .Select(d => new
                {
                    d.StockCode,
                    d.ClosePrice,
                    Chg = d.ChangePercent ?? 0m,
                    d.Amount,
                    d.TurnoverRate
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            rows = dailyData.Select(d => (
                Code6: d.StockCode,
                Name: stockInfoDict.GetValueOrDefault(d.StockCode, ""),
                Chg: d.Chg,
                Amount: d.Amount,
                Turn: d.TurnoverRate)).ToList();
        }

        var stocksWithPlates = rows
            .SelectMany(d =>
            {
                var plates = stockToPlate.GetValueOrDefault(d.Code6, "");
                if (string.IsNullOrEmpty(plates))
                    return Enumerable.Empty<(string Plate, (string Code6, string Name, decimal Chg, decimal Amount, decimal? Turn) Row)>();
                return plates.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(p => (Plate: p.Trim(), Row: d));
            })
            .ToList();

        var grouped = stocksWithPlates
            .GroupBy(s => s.Plate, StringComparer.Ordinal)
            .Select(g => new
            {
                PlateName = g.Key,
                StockCount = g.Count(),
                AvgChange = g.Average(x => x.Row.Chg)
            })
            .Where(p => p.StockCount >= minStocksPerPlate && !IsExcludedHotPlateName(p.PlateName))
            .OrderByDescending(p => p.AvgChange)
            .Take(Math.Max(1, maxPlates))
            .Select(p => p.PlateName)
            .ToList();

        return grouped;
    }

    private static bool IsExcludedHotPlateName(string? plateName)
    {
        if (string.IsNullOrWhiteSpace(plateName)) return true;
        var p = plateName.Trim();
        if (string.Equals(p, "未分类", StringComparison.Ordinal)) return true;
        if (string.Equals(p, "其他", StringComparison.Ordinal) || string.Equals(p, "其它", StringComparison.Ordinal))
            return true;
        if (string.Equals(p, "其他板块", StringComparison.Ordinal) || string.Equals(p, "其它板块", StringComparison.Ordinal))
            return true;
        if (string.Equals(p, "ST板块", StringComparison.Ordinal) || string.Equals(p, "ST股", StringComparison.Ordinal))
            return true;
        if (p.Contains("ST板块", StringComparison.Ordinal)) return true;
        if (p.Contains("风险警示", StringComparison.Ordinal)) return true;
        if (p.Contains("退市整理", StringComparison.Ordinal)) return true;
        return false;
    }

    private static string ToCode6Digits(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        return SinaMinuteChartService.NormalizeToCode6(code);
    }

    private static string ToTencentPrefix(string code6)
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

    private sealed class ClsLimitUpApiResponse
    {
        public ClsLimitUpData? data { get; set; }
    }

    private sealed class ClsLimitUpData
    {
        public ClsPlateData[]? plate_stock { get; set; }
    }

    private sealed class ClsPlateData
    {
        public string? secu_code { get; set; }
        public string? secu_name { get; set; }
        public ClsStockInfo[]? stock_list { get; set; }
    }

    private sealed class ClsStockInfo
    {
        public string? secu_code { get; set; }
        public string? secu_name { get; set; }
        public decimal? change { get; set; }
        public string? time { get; set; }
        public decimal? cmc { get; set; }
        public decimal? last_px { get; set; }
    }
}
