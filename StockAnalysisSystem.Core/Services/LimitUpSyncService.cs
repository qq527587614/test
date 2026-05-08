using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.Core.Services;

/// <summary>
/// 同步涨停数据（来源：财联社 cls）。
/// </summary>
public sealed class LimitUpSyncService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public LimitUpSyncService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<LimitUpSyncResult> SyncRecentDaysAsync(
        int days = 30,
        DateTime? endDate = null,
        bool clearExistingInRange = true,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var end = (endDate ?? DateTime.Today).Date;
        if (days < 1) days = 1;
        var start = end.AddDays(-(days - 1)).Date;

        using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (clearExistingInRange)
        {
            progress?.Report($"清理涨停数据：{start:yyyy-MM-dd}..{end:yyyy-MM-dd} …");
            var toDelete = await db.StockLimitUpAnalysis
                .Where(x => x.analysis_date.Date >= start && x.analysis_date.Date <= end)
                .ToListAsync(cancellationToken);
            if (toDelete.Count > 0)
            {
                db.StockLimitUpAnalysis.RemoveRange(toDelete);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        var inserted = 0;
        var okDays = 0;
        var emptyDays = 0;
        var failedDays = 0;

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dateStr = d.ToString("yyyyMMdd");
            progress?.Report($"同步涨停：{dateStr} …");

            try
            {
                var entities = await FetchOneDayAsync(d, cancellationToken);
                if (entities.Count == 0)
                {
                    emptyDays++;
                    continue;
                }

                // 二次保险：如果调用方未清理范围，这里按天替换
                var existing = await db.StockLimitUpAnalysis
                    .Where(x => x.analysis_date.Date == d.Date)
                    .ToListAsync(cancellationToken);
                if (existing.Count > 0)
                    db.StockLimitUpAnalysis.RemoveRange(existing);

                await db.StockLimitUpAnalysis.AddRangeAsync(entities, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                inserted += entities.Count;
                okDays++;
            }
            catch (Exception ex)
            {
                failedDays++;
                ErrorLogger.Log(ex, "LimitUpSyncService.SyncRecentDaysAsync", new { date = d });
            }
        }

        progress?.Report($"完成：成功{okDays}天/无数据{emptyDays}天/失败{failedDays}天，新增{inserted}条。");
        return new LimitUpSyncResult
        {
            StartDate = start,
            EndDate = end,
            OkDays = okDays,
            EmptyDays = emptyDays,
            FailedDays = failedDays,
            InsertedRows = inserted
        };
    }

    /// <summary>
    /// 按指定交易日集合同步涨停数据（用于“按日线日期范围补同步历史涨停”）。
    /// </summary>
    public async Task<LimitUpSyncResult> SyncByTradeDatesAsync(
        IReadOnlyList<DateTime> tradeDates,
        bool clearExistingForDates = true,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var dates = tradeDates?
            .Select(d => d.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList() ?? new List<DateTime>();

        if (dates.Count == 0)
        {
            progress?.Report("未提供交易日，跳过同步。");
            return new LimitUpSyncResult
            {
                StartDate = DateTime.MinValue,
                EndDate = DateTime.MinValue,
                OkDays = 0,
                EmptyDays = 0,
                FailedDays = 0,
                InsertedRows = 0
            };
        }

        var start = dates[0];
        var end = dates[^1];

        using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (clearExistingForDates)
        {
            progress?.Report($"清理涨停数据（按交易日）：{start:yyyy-MM-dd}..{end:yyyy-MM-dd}（共{dates.Count}天）…");
            foreach (var d in dates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var existing = await db.StockLimitUpAnalysis
                    .Where(x => x.analysis_date.Date == d.Date)
                    .ToListAsync(cancellationToken);
                if (existing.Count > 0)
                {
                    db.StockLimitUpAnalysis.RemoveRange(existing);
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        var inserted = 0;
        var okDays = 0;
        var emptyDays = 0;
        var failedDays = 0;

        for (var i = 0; i < dates.Count; i++)
        {
            var d = dates[i];
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"同步涨停（交易日） {i + 1}/{dates.Count}：{d:yyyyMMdd} …");

            try
            {
                var entities = await FetchOneDayAsync(d, cancellationToken);
                if (entities.Count == 0)
                {
                    emptyDays++;
                    continue;
                }

                // 二次保险：按天替换
                var existing = await db.StockLimitUpAnalysis
                    .Where(x => x.analysis_date.Date == d.Date)
                    .ToListAsync(cancellationToken);
                if (existing.Count > 0)
                    db.StockLimitUpAnalysis.RemoveRange(existing);

                await db.StockLimitUpAnalysis.AddRangeAsync(entities, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                inserted += entities.Count;
                okDays++;
            }
            catch (Exception ex)
            {
                failedDays++;
                ErrorLogger.Log(ex, "LimitUpSyncService.SyncByTradeDatesAsync", new { date = d });
            }
        }

        progress?.Report($"完成：成功{okDays}天/无数据{emptyDays}天/失败{failedDays}天，新增{inserted}条。");
        return new LimitUpSyncResult
        {
            StartDate = start,
            EndDate = end,
            OkDays = okDays,
            EmptyDays = emptyDays,
            FailedDays = failedDays,
            InsertedRows = inserted
        };
    }

    private static string BuildUrl(DateTime date)
        => $"https://x-quote.cls.cn/v2/quote/a/plate/up_down_analysis?date={date:yyyyMMdd}";

    private static async Task<List<StockLimitUpAnalysis>> FetchOneDayAsync(DateTime date, CancellationToken cancellationToken)
    {
        var url = BuildUrl(date);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var json = await client.GetStringAsync(url, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
                return new List<StockLimitUpAnalysis>();

            var data = JsonSerializer.Deserialize<LimitUpApiResponse>(json);
            if (data?.data?.plate_stock == null || data.data.plate_stock.Length == 0)
                return new List<StockLimitUpAnalysis>();

            var list = new List<StockLimitUpAnalysis>(capacity: 1024);
            foreach (var plate in data.data.plate_stock)
            {
                if (plate.stock_list == null) continue;
                foreach (var stock in plate.stock_list)
                {
                    if (stock.time == "--") continue;

                    list.Add(new StockLimitUpAnalysis
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

            return list;
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "LimitUpSyncService.FetchOneDayAsync", url);
            return new List<StockLimitUpAnalysis>();
        }
    }

    // API响应模型（保持与现有 PlateAnalysisForm 一致）
    private sealed class LimitUpApiResponse { public LimitUpData? data { get; set; } }
    private sealed class LimitUpData { public PlateData[]? plate_stock { get; set; } }

    private sealed class PlateData
    {
        public string? secu_code { get; set; }
        public string? secu_name { get; set; }
        public StockInfo[]? stock_list { get; set; }
    }

    private sealed class StockInfo
    {
        public string? secu_code { get; set; }
        public string? secu_name { get; set; }
        public decimal? change { get; set; }
        public string? time { get; set; }
        public decimal? cmc { get; set; }
        public decimal? last_px { get; set; }
    }
}

public sealed class LimitUpSyncResult
{
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public int OkDays { get; init; }
    public int EmptyDays { get; init; }
    public int FailedDays { get; init; }
    public int InsertedRows { get; init; }
}

