using Microsoft.EntityFrameworkCore;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Utils;
using DailyPickEntity = StockAnalysisSystem.Core.Entities.DailyPick;

namespace StockAnalysisSystem.Core.Repositories;

/// <summary>
/// 股票信息仓储实现
/// </summary>
public class StockRepository : IStockRepository
{
    private readonly AppDbContext _context;

    public StockRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<StockInfo>> GetAllAsync()
    {
        return await _context.StockInfos.ToListAsync();
    }

    public async Task<StockInfo?> GetByIdAsync(string id)
    {
        return await _context.StockInfos.FindAsync(id);
    }

    public async Task<StockInfo?> GetByCodeAsync(string code)
    {
        return await _context.StockInfos
            .FirstOrDefaultAsync(s => s.StockCode == code);
    }

    public async Task<List<StockInfo>> GetByIndustryAsync(string industry)
    {
        return await _context.StockInfos
            .Where(s => s.Industry == industry)
            .ToListAsync();
    }

    public async Task<List<StockInfo>> GetBySectorAsync(string sector)
    {
        return await _context.StockInfos
            .Where(s => s.Sector == sector)
            .ToListAsync();
    }

    public async Task<int> GetCountAsync()
    {
        return await _context.StockInfos.CountAsync();
    }
}

/// <summary>
/// 股票日线数据仓储实现
/// </summary>
public class StockDailyDataRepository : IStockDailyDataRepository
{
    private readonly AppDbContext _context;
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;

    public StockDailyDataRepository(AppDbContext context)
    {
        _context = context;
    }

    public StockDailyDataRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    private AppDbContext GetContext()
    {
        return _contextFactory != null ? _contextFactory.CreateDbContext() : _context;
    }

    public async Task<List<StockDailyData>> GetByStockIdAsync(string stockId, DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            var context = GetContext();
            var query = context.StockDailyData
                .Where(d => d.StockID == stockId);

            if (startDate.HasValue)
                query = query.Where(d => d.TradeDate >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(d => d.TradeDate <= endDate.Value);

            return await query.OrderBy(d => d.TradeDate).ToListAsync();
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, 
                $"Method: {nameof(GetByStockIdAsync)} | Repository: StockDailyDataRepository", 
                new { StockId = stockId, StartDate = startDate, EndDate = endDate });
            throw;
        }
    }

    public async Task<List<StockDailyData>> GetByDateAsync(DateTime date)
    {
        var context = GetContext();
        return await context.StockDailyData
            .Where(d => d.TradeDate == date)
            .ToListAsync();
    }

    public async Task<StockDailyData?> GetLatestAsync(string stockId)
    {
        try
        {
            var context = GetContext();
            return await context.StockDailyData
                .Where(d => d.StockID == stockId)
                .OrderByDescending(d => d.TradeDate)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, 
                $"Method: {nameof(GetLatestAsync)} | Repository: StockDailyDataRepository", 
                new { StockId = stockId });
            throw;
        }
    }

    public async Task<DateTime?> GetLatestTradeDateAsync()
    {
        var context = GetContext();
        return await context.StockDailyData
            .MaxAsync(d => (DateTime?)d.TradeDate);
    }

    public async Task<List<DateTime>> GetTradeDatesAsync(DateTime startDate, DateTime endDate)
    {
        var context = GetContext();
        return await context.StockDailyData
            .Where(d => d.TradeDate >= startDate && d.TradeDate <= endDate)
            .Select(d => d.TradeDate)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync();
    }

    public async Task<List<StockDailyData>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            var context = GetContext();
            return await context.StockDailyData
                .Where(d => d.TradeDate >= startDate && d.TradeDate <= endDate)
                .OrderBy(d => d.StockID)
                .ThenBy(d => d.TradeDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex,
                $"Method: {nameof(GetByDateRangeAsync)} | Repository: StockDailyDataRepository",
                new { StartDate = startDate, EndDate = endDate });
            throw;
        }
    }
}

/// <summary>
/// 技术指标仓储实现
/// </summary>
public class IndicatorRepository : IIndicatorRepository
{
    private readonly AppDbContext _context;
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;

    public IndicatorRepository(AppDbContext context)
    {
        _context = context;
    }

    public IndicatorRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    private AppDbContext GetContext()
    {
        return _contextFactory != null ? _contextFactory.CreateDbContext() : _context;
    }

    public async Task<List<StockDailyIndicator>> GetByStockIdAsync(string stockId, DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            var context = GetContext();
            var query = context.StockDailyIndicators
                .Where(i => i.StockId == stockId);

            if (startDate.HasValue)
                query = query.Where(i => i.TradeDate >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(i => i.TradeDate <= endDate.Value);

            return await query.OrderBy(i => i.TradeDate).ToListAsync();
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, 
                $"Method: {nameof(GetByStockIdAsync)} | Repository: IndicatorRepository", 
                new { StockId = stockId, StartDate = startDate, EndDate = endDate });
            throw;
        }
    }

    public async Task<StockDailyIndicator?> GetByStockAndDateAsync(string stockId, DateTime date)
    {
        var context = GetContext();
        return await context.StockDailyIndicators
            .FirstOrDefaultAsync(i => i.StockId == stockId && i.TradeDate == date);
    }

    public async Task<List<StockDailyIndicator>> GetByDateAsync(DateTime date)
    {
        var context = GetContext();
        return await context.StockDailyIndicators
            .Where(i => i.TradeDate == date)
            .ToListAsync();
    }

    public async Task BulkInsertAsync(List<StockDailyIndicator> indicators)
    {
        var context = GetContext();
        await context.StockDailyIndicators.AddRangeAsync(indicators);
        await context.SaveChangesAsync();
    }

    public async Task<int> GetCountAsync()
    {
        var context = GetContext();
        return await context.StockDailyIndicators.CountAsync();
    }

    public async Task<List<StockDailyIndicator>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            var context = GetContext();
            return await context.StockDailyIndicators
                .Where(i => i.TradeDate >= startDate && i.TradeDate <= endDate)
                .OrderBy(i => i.StockId)
                .ThenBy(i => i.TradeDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex,
                $"Method: {nameof(GetByDateRangeAsync)} | Repository: IndicatorRepository",
                new { StartDate = startDate, EndDate = endDate });
            throw;
        }
    }
}

/// <summary>
/// 策略仓储实现
/// </summary>
public class StrategyRepository : IStrategyRepository
{
    private readonly AppDbContext _context;

    public StrategyRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<Strategy>> GetAllAsync()
    {
        return await _context.Strategies.ToListAsync();
    }

    public async Task<Strategy?> GetByIdAsync(int id)
    {
        return await _context.Strategies.FindAsync(id);
    }

    public async Task<List<Strategy>> GetByIdsAsync(List<int> ids)
    {
        return await _context.Strategies
            .Where(s => ids.Contains(s.Id))
            .ToListAsync();
    }

    public async Task<List<Strategy>> GetActiveAsync()
    {
        return await _context.Strategies
            .Where(s => s.IsActive)
            .ToListAsync();
    }

    public async Task<Strategy> AddAsync(Strategy strategy)
    {
        _context.Strategies.Add(strategy);
        await _context.SaveChangesAsync();
        return strategy;
    }

    public async Task UpdateAsync(Strategy strategy)
    {
        strategy.UpdatedAt = DateTime.Now;
        _context.Strategies.Update(strategy);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var strategy = await _context.Strategies.FindAsync(id);
        if (strategy != null)
        {
            _context.Strategies.Remove(strategy);
            await _context.SaveChangesAsync();
        }
    }
}

/// <summary>
/// 回测任务仓储实现
/// </summary>
public class BacktestTaskRepository : IBacktestTaskRepository
{
    private readonly AppDbContext _context;

    public BacktestTaskRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<BacktestTask?> GetByIdAsync(int id)
    {
        return await _context.BacktestTasks
            .Include(b => b.Strategy)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<List<BacktestTask>> GetByStrategyIdAsync(int strategyId)
    {
        return await _context.BacktestTasks
            .Where(b => b.StrategyId == strategyId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<BacktestTask>> GetRecentAsync(int count = 10)
    {
        return await _context.BacktestTasks
            .Include(b => b.Strategy)
            .OrderByDescending(b => b.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<BacktestTask> AddAsync(BacktestTask task)
    {
        _context.BacktestTasks.Add(task);
        await _context.SaveChangesAsync();
        return task;
    }

    public async Task UpdateAsync(BacktestTask task)
    {
        _context.BacktestTasks.Update(task);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateStatusAsync(int id, string status, string? result = null)
    {
        var task = await _context.BacktestTasks.FindAsync(id);
        if (task != null)
        {
            task.Status = status;
            if (result != null)
                task.Result = result;
            if (status == "Completed" || status == "Failed")
                task.CompletedAt = DateTime.Now;
            await _context.SaveChangesAsync();
        }
    }
}

/// <summary>
/// 优化任务仓储实现
/// </summary>
public class OptimizationTaskRepository : IOptimizationTaskRepository
{
    private readonly AppDbContext _context;

    public OptimizationTaskRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<OptimizationTask?> GetByIdAsync(int id)
    {
        return await _context.OptimizationTasks.FindAsync(id);
    }

    public async Task<List<OptimizationTask>> GetRecentAsync(int count = 10)
    {
        return await _context.OptimizationTasks
            .OrderByDescending(o => o.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<OptimizationTask> AddAsync(OptimizationTask task)
    {
        _context.OptimizationTasks.Add(task);
        await _context.SaveChangesAsync();
        return task;
    }

    public async Task UpdateAsync(OptimizationTask task)
    {
        _context.OptimizationTasks.Update(task);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateStatusAsync(int id, string status, string? bestParameters = null, string? bestResult = null)
    {
        var task = await _context.OptimizationTasks.FindAsync(id);
        if (task != null)
        {
            task.Status = status;
            if (bestParameters != null)
                task.BestParameters = bestParameters;
            if (bestResult != null)
                task.BestResult = bestResult;
            if (status == "Completed" || status == "Failed")
                task.CompletedAt = DateTime.Now;
            await _context.SaveChangesAsync();
        }
    }
}

/// <summary>
/// 每日选股结果仓储实现
/// </summary>
public class DailyPickRepository : IDailyPickRepository
{
    private readonly AppDbContext _context;

    public DailyPickRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<DailyPickEntity>> GetByDateAsync(DateTime date)
    {
        return await _context.DailyPicks
            .Include(d => d.Strategy)
            .Where(d => d.TradeDate == date)
            .OrderByDescending(d => d.FinalScore)
            .ToListAsync();
    }

    public async Task<List<DailyPickEntity>> GetByDateAndStrategyAsync(DateTime date, int strategyId)
    {
        return await _context.DailyPicks
            .Where(d => d.TradeDate == date && d.StrategyId == strategyId)
            .OrderByDescending(d => d.FinalScore)
            .ToListAsync();
    }

    public async Task<List<DailyPickEntity>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _context.DailyPicks
            .Include(d => d.Strategy)
            .Where(d => d.TradeDate >= startDate && d.TradeDate <= endDate)
            .OrderBy(d => d.TradeDate)
            .ThenByDescending(d => d.FinalScore)
            .ToListAsync();
    }

    public async Task<DailyPickEntity?> GetByDateStockStrategyAsync(DateTime date, string stockId, int strategyId)
    {
        return await _context.DailyPicks
            .FirstOrDefaultAsync(d => d.TradeDate == date && d.StockId == stockId && d.StrategyId == strategyId);
    }

    public async Task BulkInsertAsync(List<DailyPickEntity> picks)
    {
        await _context.DailyPicks.AddRangeAsync(picks);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteByDateAsync(DateTime date)
    {
        var picks = await _context.DailyPicks
            .Where(d => d.TradeDate == date)
            .ToListAsync();
        _context.DailyPicks.RemoveRange(picks);
        await _context.SaveChangesAsync();
    }
}

/// <summary>
/// DeepSeek日志仓储实现
/// </summary>
public class DeepSeekLogRepository : IDeepSeekLogRepository
{
    private readonly AppDbContext _context;

    public DeepSeekLogRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<DeepSeekLog> AddAsync(DeepSeekLog log)
    {
        _context.DeepSeekLogs.Add(log);
        await _context.SaveChangesAsync();
        return log;
    }

    public async Task<List<DeepSeekLog>> GetRecentAsync(int count = 100)
    {
        return await _context.DeepSeekLogs
            .OrderByDescending(l => l.CreatedAt)
            .Take(count)
            .ToListAsync();
    }
}

/// <summary>
/// 自选股仓储实现
/// </summary>
public class StockFavoriteRepository : IStockFavoriteRepository
{
    private readonly AppDbContext _context;

    public StockFavoriteRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<StockFavorite>> GetAllAsync()
    {
        return await _context.StockFavorites
            .OrderByDescending(f => f.AddedDate)
            .ToListAsync();
    }

    public async Task<StockFavorite?> GetByStockCodeAsync(string stockCode)
    {
        return await _context.StockFavorites
            .FirstOrDefaultAsync(f => f.StockCode == stockCode);
    }

    public async Task<StockFavorite> AddAsync(StockFavorite favorite)
    {
        _context.StockFavorites.Add(favorite);
        await _context.SaveChangesAsync();
        return favorite;
    }

    public async Task DeleteAsync(string stockCode)
    {
        var favorite = await GetByStockCodeAsync(stockCode);
        if (favorite != null)
        {
            _context.StockFavorites.Remove(favorite);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsAsync(string stockCode)
    {
        return await _context.StockFavorites.AnyAsync(f => f.StockCode == stockCode);
    }
}
