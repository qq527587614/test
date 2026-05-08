using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Repositories;

namespace StockAnalysisSystem.Core.Indicators;

public sealed class IndicatorProvider : IIndicatorProvider
{
    private readonly IStockDailyDataRepository _dailyRepo;
    private readonly IIndicatorRepository _indicatorRepo;

    public IndicatorProvider(
        IStockDailyDataRepository dailyRepo,
        IIndicatorRepository indicatorRepo)
    {
        _dailyRepo = dailyRepo;
        _indicatorRepo = indicatorRepo;
    }

    public async Task<List<StockDailyIndicator>> GetOrComputeAsync(
        string stockId,
        DateTime startDate,
        DateTime endDate,
        IndicatorProviderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new IndicatorProviderOptions();

        // 先读库（最常见路径）
        var existing = await _indicatorRepo.GetByStockIdAsync(stockId, startDate, endDate);
        if (existing.Count > 0 && !options.PersistComputedIndicators)
            return existing;

        // 缺失时：加载日线并现场算
        var lookbackStart = startDate.Date.AddDays(-Math.Max(1, options.LookbackDays));
        var dailyData = await _dailyRepo.GetByStockIdAsync(stockId, lookbackStart, endDate.Date);
        return await GetOrComputeFromDailyDataAsync(stockId, dailyData, startDate, endDate, options, cancellationToken);
    }

    public async Task<Dictionary<string, List<StockDailyIndicator>>> GetOrComputeBatchAsync(
        IReadOnlyCollection<string> stockIds,
        DateTime startDate,
        DateTime endDate,
        IndicatorProviderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new IndicatorProviderOptions();

        // 批量加载：先拉一把全量区间指标与日线，再按股票分组
        var lookbackStart = startDate.Date.AddDays(-Math.Max(1, options.LookbackDays));

        var allDaily = await _dailyRepo.GetByDateRangeAsync(lookbackStart, endDate.Date);
        var dailyByStock = allDaily
            .Where(d => stockIds.Contains(d.StockID))
            .ToLookup(d => d.StockID);

        var allIndicators = await _indicatorRepo.GetByDateRangeAsync(startDate.Date, endDate.Date);
        var indByStock = allIndicators
            .Where(i => stockIds.Contains(i.StockId))
            .ToLookup(i => i.StockId);

        var result = new Dictionary<string, List<StockDailyIndicator>>(StringComparer.Ordinal);

        foreach (var sid in stockIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = indByStock[sid].OrderBy(x => x.TradeDate).ToList();
            if (existing.Count > 0 && !options.PersistComputedIndicators)
            {
                result[sid] = existing;
                continue;
            }

            var daily = dailyByStock[sid].OrderBy(x => x.TradeDate).ToList();
            if (daily.Count == 0)
            {
                result[sid] = existing;
                continue;
            }

            var computed = await GetOrComputeFromDailyDataAsync(sid, daily, startDate, endDate, options, cancellationToken);
            result[sid] = computed;
        }

        return result;
    }

    public async Task<List<StockDailyIndicator>> GetOrComputeFromDailyDataAsync(
        string stockId,
        List<StockDailyData> dailyData,
        DateTime startDate,
        DateTime endDate,
        IndicatorProviderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new IndicatorProviderOptions();
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await _indicatorRepo.GetByStockIdAsync(stockId, startDate.Date, endDate.Date);
        var existingDateSet = new HashSet<DateTime>(existing.Select(x => x.TradeDate.Date));

        // 计算全量指标（当前实现一次性把常用指标都算出来）
        var allComputed = IndicatorCalculator.CalculateAll(stockId, dailyData);
        var computedInRange = allComputed
            .Where(i => i.TradeDate.Date >= startDate.Date && i.TradeDate.Date <= endDate.Date)
            .OrderBy(i => i.TradeDate)
            .ToList();

        if (options.PersistComputedIndicators)
        {
            var toInsert = computedInRange
                .Where(i => !existingDateSet.Contains(i.TradeDate.Date))
                .ToList();

            if (toInsert.Count > 0)
            {
                // 仅插入缺失日期，避免违反 (StockId, TradeDate) 唯一索引
                await _indicatorRepo.BulkInsertAsync(toInsert);
                existing.AddRange(toInsert);
                existing = existing.OrderBy(x => x.TradeDate).ToList();
            }
        }

        // 返回：优先用库里的（可能更完整/历史一致），否则用现算结果兜底
        if (existing.Count > 0)
            return existing.OrderBy(x => x.TradeDate).ToList();

        return computedInRange;
    }
}

