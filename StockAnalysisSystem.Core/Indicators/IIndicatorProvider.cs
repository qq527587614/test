using StockAnalysisSystem.Core.Entities;

namespace StockAnalysisSystem.Core.Indicators;

public interface IIndicatorProvider
{
    /// <summary>
    /// 获取指定股票在区间内的指标；当库中缺失时，会基于日线数据现场计算并可选落库。
    /// </summary>
    Task<List<StockDailyIndicator>> GetOrComputeAsync(
        string stockId,
        DateTime startDate,
        DateTime endDate,
        IndicatorProviderOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量获取多个股票在区间内的指标；当库中缺失时，会基于日线数据现场计算并可选落库。
    /// </summary>
    Task<Dictionary<string, List<StockDailyIndicator>>> GetOrComputeBatchAsync(
        IReadOnlyCollection<string> stockIds,
        DateTime startDate,
        DateTime endDate,
        IndicatorProviderOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 已有日线数据时的快速路径：避免重复加载日线。
    /// </summary>
    Task<List<StockDailyIndicator>> GetOrComputeFromDailyDataAsync(
        string stockId,
        List<StockDailyData> dailyData,
        DateTime startDate,
        DateTime endDate,
        IndicatorProviderOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed class IndicatorProviderOptions
{
    /// <summary>
    /// 计算指标所需的回溯天数（自然日）。例如 MA120/量能MA120，通常至少需要 200+ 天数据更稳。
    /// </summary>
    public int LookbackDays { get; set; } = 220;

    /// <summary>
    /// 是否将“缺失日期”的计算结果写回指标表（只插入缺失，不做 upsert）。
    /// </summary>
    public bool PersistComputedIndicators { get; set; } = false;
}

