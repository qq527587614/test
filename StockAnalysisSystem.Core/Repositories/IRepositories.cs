using StockAnalysisSystem.Core.Entities;
using DailyPickEntity = StockAnalysisSystem.Core.Entities.DailyPick;

namespace StockAnalysisSystem.Core.Repositories;

/// <summary>
/// 股票信息仓储接口
/// </summary>
public interface IStockRepository
{
    Task<List<StockInfo>> GetAllAsync();
    Task<StockInfo?> GetByIdAsync(string id);
    Task<StockInfo?> GetByCodeAsync(string code);
    Task<List<StockInfo>> GetByIndustryAsync(string industry);
    Task<List<StockInfo>> GetBySectorAsync(string sector);
    Task<int> GetCountAsync();
}

/// <summary>
/// 股票日线数据仓储接口
/// </summary>
public interface IStockDailyDataRepository
{
    Task<List<StockDailyData>> GetByStockIdAsync(string stockId, DateTime? startDate = null, DateTime? endDate = null);
    Task<List<StockDailyData>> GetByDateAsync(DateTime date);
    Task<StockDailyData?> GetLatestAsync(string stockId);
    Task<DateTime?> GetLatestTradeDateAsync();
    Task<List<DateTime>> GetTradeDatesAsync(DateTime startDate, DateTime endDate);
    /// <summary>
    /// 批量获取指定日期范围内所有股票的日线数据（用于批量处理优化）
    /// </summary>
    Task<List<StockDailyData>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
}

/// <summary>
/// 技术指标仓储接口
/// </summary>
public interface IIndicatorRepository
{
    Task<List<StockDailyIndicator>> GetByStockIdAsync(string stockId, DateTime? startDate = null, DateTime? endDate = null);
    Task<StockDailyIndicator?> GetByStockAndDateAsync(string stockId, DateTime date);
    Task<List<StockDailyIndicator>> GetByDateAsync(DateTime date);
    Task BulkInsertAsync(List<StockDailyIndicator> indicators);
    Task<int> GetCountAsync();
    /// <summary>
    /// 批量获取指定日期范围内所有股票的技术指标（用于批量处理优化）
    /// </summary>
    Task<List<StockDailyIndicator>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
}

/// <summary>
/// 策略仓储接口
/// </summary>
public interface IStrategyRepository
{
    Task<List<Strategy>> GetAllAsync();
    Task<Strategy?> GetByIdAsync(int id);
    Task<List<Strategy>> GetActiveAsync();
    Task<Strategy> AddAsync(Strategy strategy);
    Task UpdateAsync(Strategy strategy);
    Task DeleteAsync(int id);
}

/// <summary>
/// 回测任务仓储接口
/// </summary>
public interface IBacktestTaskRepository
{
    Task<BacktestTask?> GetByIdAsync(int id);
    Task<List<BacktestTask>> GetByStrategyIdAsync(int strategyId);
    Task<List<BacktestTask>> GetRecentAsync(int count = 10);
    Task<BacktestTask> AddAsync(BacktestTask task);
    Task UpdateAsync(BacktestTask task);
    Task UpdateStatusAsync(int id, string status, string? result = null);
}

/// <summary>
/// 优化任务仓储接口
/// </summary>
public interface IOptimizationTaskRepository
{
    Task<OptimizationTask?> GetByIdAsync(int id);
    Task<List<OptimizationTask>> GetRecentAsync(int count = 10);
    Task<OptimizationTask> AddAsync(OptimizationTask task);
    Task UpdateAsync(OptimizationTask task);
    Task UpdateStatusAsync(int id, string status, string? bestParameters = null, string? bestResult = null);
}

/// <summary>
/// 每日选股结果仓储接口
/// </summary>
public interface IDailyPickRepository
{
    Task<List<DailyPickEntity>> GetByDateAsync(DateTime date);
    Task<List<DailyPickEntity>> GetByDateAndStrategyAsync(DateTime date, int strategyId);
    Task<List<DailyPickEntity>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<DailyPickEntity?> GetByDateStockStrategyAsync(DateTime date, string stockId, int strategyId);
    Task BulkInsertAsync(List<DailyPickEntity> picks);
    Task DeleteByDateAsync(DateTime date);
}

/// <summary>
/// DeepSeek日志仓储接口
/// </summary>
public interface IDeepSeekLogRepository
{
    Task<DeepSeekLog> AddAsync(DeepSeekLog log);
    Task<List<DeepSeekLog>> GetRecentAsync(int count = 100);
}
