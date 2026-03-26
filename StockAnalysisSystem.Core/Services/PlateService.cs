using Microsoft.EntityFrameworkCore;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.Core.Services;

/// <summary>
/// 板块数据服务
/// </summary>
public class PlateService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public PlateService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>
    /// 增量同步板块信息和成分股（同步所有有涨停数据的日期）
    /// </summary>
    public async Task<int> SyncPlatesFromLimitUpAsync()
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        // 获取所有有涨停数据的日期
        var datesWithLimitUp = await dbContext.StockLimitUpAnalysis
            .Where(s => !string.IsNullOrEmpty(s.plate_name))
            .Select(s => s.analysis_date.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync();

        if (!datesWithLimitUp.Any())
        {
            ErrorLogger.Log(null, "PlateService.SyncPlatesFromLimitUp", "没有涨停数据");
            return 0;
        }

        // 预加载所有板块
        var allPlates = await dbContext.Plates.ToListAsync();
        var platesByCode = allPlates.ToDictionary(p => p.plate_code);

        // 预加载所有成分股关系（用于快速检查是否已存在）
        var allPlateStocks = await dbContext.PlateStocks.ToListAsync();
        var existingPlateStocksSet = allPlateStocks
            .Select(ps => (ps.plate_id, ps.stock_code))
            .ToHashSet();

        int syncCount = 0;
        var newPlateStocks = new List<PlateStock>();

        foreach (var date in datesWithLimitUp)
        {
            // 获取当天的涨停数据
            var limitUpData = await dbContext.StockLimitUpAnalysis
                .Where(s => s.analysis_date.Date == date && !string.IsNullOrEmpty(s.plate_name))
                .ToListAsync();

            if (!limitUpData.Any()) continue;

            // 按板块分组获取当天有涨停的股票
            var plateGroups = limitUpData
                .GroupBy(s => new { s.plate_code, s.plate_name })
                .ToList();

            foreach (var group in plateGroups)
            {
                var plateCode = group.Key.plate_code;
                var plateName = group.Key.plate_name;

                // 从内存查找或创建板块
                if (!platesByCode.TryGetValue(plateCode, out var plate))
                {
                    plate = new Plate
                    {
                        plate_code = plateCode,
                        plate_name = plateName,
                        created_time = DateTime.Now,
                        updated_time = DateTime.Now
                    };
                    dbContext.Plates.Add(plate);
                    platesByCode[plateCode] = plate;
                }

                // 更新板块名称（可能变化）
                if (plate.plate_name != plateName)
                {
                    plate.plate_name = plateName;
                    plate.updated_time = DateTime.Now;
                }

                // 获取该板块当天涨停的股票代码（去重）
                var stockCodesInPlate = group
                    .Select(s => new { s.code, s.name })
                    .Distinct()
                    .ToList();

                foreach (var stock in stockCodesInPlate)
                {
                    // 从内存检查成分股是否已存在
                    var stockKey = (plate.id, stock.code);
                    if (!existingPlateStocksSet.Contains(stockKey))
                    {
                        // 新增成分股（保存到列表，稍后批量插入）
                        var plateStock = new PlateStock
                        {
                            plate_id = plate.id,
                            stock_code = stock.code,
                            stock_name = stock.name,
                            join_date = date,
                            created_time = DateTime.Now
                        };
                        newPlateStocks.Add(plateStock);
                        existingPlateStocksSet.Add(stockKey); // 标记为已存在，避免重复
                        syncCount++;
                    }
                }
            }

            // 批量保存成分股
            if (newPlateStocks.Any())
            {
                await dbContext.PlateStocks.AddRangeAsync(newPlateStocks);
                await dbContext.SaveChangesAsync();
                newPlateStocks.Clear();
            }
        }

        ErrorLogger.Log(null, "PlateService.SyncPlatesFromLimitUp", $"同步完成: 新增 {syncCount} 个成分股");
        return syncCount;
    }

    /// <summary>
    /// 增量计算板块日线数据
    /// 第一次运行时计算所有日线数据的日期，之后只计算新增的日期
    /// </summary>
    public async Task<int> CalcPlateDailyDataAsync()
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        // 获取所有板块
        var plates = await dbContext.Plates.ToListAsync();

        if (!plates.Any())
        {
            ErrorLogger.Log(null, "PlateService.CalcPlateDailyData", "没有板块数据，请先同步板块");
            return 0;
        }

        // 获取所有已有板块日线数据的日期
        var existingDates = await dbContext.PlateDailyData
            .Select(pd => pd.trade_date.Date)
            .Distinct()
            .ToListAsync();

        // 获取所有有日线数据的日期
        var allDatesWithData = await dbContext.StockDailyData
            .Select(d => d.TradeDate.Date)
            .Distinct()
            .ToListAsync();

        // 如果没有历史数据，计算所有日期；否则只计算还没有数据的日期
        List<DateTime> datesToCalc;
        if (!existingDates.Any())
        {
            // 第一次计算，计算所有有日线数据的日期
            datesToCalc = allDatesWithData;
        }
        else
        {
            // 增量计算，只计算还没有板块日线数据的日期
            datesToCalc = allDatesWithData.Where(d => !existingDates.Contains(d)).ToList();
        }

        if (!datesToCalc.Any())
        {
            ErrorLogger.Log(null, "PlateService.CalcPlateDailyData", "所有日期已计算完成，无需重复计算");
            return 0;
        }

        // 预加载所有板块成分股关系
        var allPlateStocks = await dbContext.PlateStocks
            .Include(ps => ps.Plate)
            .ToListAsync();

        // 按日期分组要计算的股票代码
        var datesToCalcSet = datesToCalc.ToHashSet();
        var datesToCalcList = datesToCalc.OrderBy(d => d).ToList();

        // 批量查询所有相关日期的日线数据
        var relevantDailyData = await dbContext.StockDailyData
            .Where(d => datesToCalcSet.Contains(d.TradeDate.Date))
            .ToListAsync();

        // 按日期和股票代码索引日线数据
        var dailyDataByDateAndCode = relevantDailyData
            .GroupBy(d => d.TradeDate.Date)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(d => d.StockID)
            );

        // 预加载涨停数据（用于计算涨停数量）
        var relevantLimitUpData = await dbContext.StockLimitUpAnalysis
            .Where(s => datesToCalcSet.Contains(s.analysis_date.Date))
            .Select(s => new { s.code, Date = s.analysis_date.Date, s.plate_name })
            .ToListAsync();

        var limitUpByDateAndCode = relevantLimitUpData
            .GroupBy(s => s.Date)
            .ToDictionary(
                g => g.Key,
                g => g.ToLookup(s => s.code)
            );

        // 预计算每个板块的成分股列表（带前缀和不带前缀）
        var plateStockDataByPlateId = allPlateStocks
            .GroupBy(ps => ps.plate_id)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    StockCodes = g.Select(ps => ps.stock_code).ToList(),
                    StockCodesWithPrefix = g.Select(ps =>
                        ps.stock_code.StartsWith("6") ? "sh" + ps.stock_code : "sz" + ps.stock_code
                    ).ToList()
                }
            );

        int calcCount = 0;
        var plateDailyDataList = new List<PlateDailyData>(plates.Count);

        foreach (var date in datesToCalcList)
        {
            var dateValue = date;
            var dailyDataByCode = dailyDataByDateAndCode.TryGetValue(dateValue, out var dataByCode) ? dataByCode : new Dictionary<string, StockDailyData>();
            var limitUpByCode = limitUpByDateAndCode.TryGetValue(dateValue, out var limitUpLookup) ? limitUpLookup : null;

            foreach (var plate in plates)
            {
                // 从预计算的数据获取成分股
                if (!plateStockDataByPlateId.TryGetValue(plate.id, out var plateStockData))
                    continue;

                if (!plateStockData.StockCodes.Any()) continue;

                // 从内存获取成分股当天的日线数据
                var dailyData = new List<StockDailyData>(plateStockData.StockCodesWithPrefix.Count);
                foreach (var code in plateStockData.StockCodesWithPrefix)
                {
                    if (dailyDataByCode.TryGetValue(code, out var data))
                    {
                        dailyData.Add(data);
                    }
                }

                if (dailyData.Count == 0) continue;

                // 计算板块统计数据
                var stockCount = dailyData.Count;
                var avgPctChg = 0m;
                var totalAmount = 0m;
                var avgTurnover = 0m;

                // 使用for循环提高性能
                for (int i = 0; i < stockCount; i++)
                {
                    var d = dailyData[i];
                    avgPctChg += d.ChangePercent ?? 0;
                    totalAmount += d.Amount;
                    avgTurnover += d.TurnoverRate ?? 0;
                }
                avgPctChg /= stockCount;
                avgTurnover /= stockCount;

                // 从内存获取当天涨停数量
                var limitUpCount = 0;
                if (limitUpByCode != null)
                {
                    foreach (var code in plateStockData.StockCodes)
                    {
                        if (limitUpByCode.Contains(code))
                            limitUpCount++;
                    }
                }

                // 新增板块日线数据
                var plateDailyData = new PlateDailyData
                {
                    plate_id = plate.id,
                    trade_date = date,
                    stock_count = stockCount,
                    limit_up_count = limitUpCount,
                    avg_pct_chg = avgPctChg,
                    total_amount = totalAmount,
                    avg_turnover = avgTurnover,
                    created_time = DateTime.Now,
                    updated_time = DateTime.Now
                };
                plateDailyDataList.Add(plateDailyData);
                calcCount++;
            }

            // 每个日期批量保存一次，减少数据库操作次数
            if (plateDailyDataList.Any())
            {
                await dbContext.PlateDailyData.AddRangeAsync(plateDailyDataList);
                await dbContext.SaveChangesAsync();
                plateDailyDataList.Clear();
                ErrorLogger.Log(null, "PlateService.CalcPlateDailyData", $"日期 {date:yyyy-MM-dd} 计算完成: {plateDailyDataList.Count} 个板块");
            }
        }

        ErrorLogger.Log(null, "PlateService.CalcPlateDailyData", $"计算完成: 共计算 {calcCount} 条板块日线数据");
        return calcCount;
    }

    /// <summary>
    /// 获取板块列表（带分页）
    /// </summary>
    public async Task<List<Plate>> GetPlatesAsync(int pageIndex = 0, int pageSize = 50)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        return await dbContext.Plates
            .OrderBy(p => p.plate_name)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    /// <summary>
    /// 获取板块成分股
    /// </summary>
    public async Task<List<PlateStock>> GetPlateStocksAsync(long plateId)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        return await dbContext.PlateStocks
            .Where(ps => ps.plate_id == plateId)
            .OrderBy(ps => ps.stock_code)
            .ToListAsync();
    }

    /// <summary>
    /// 获取板块日线数据
    /// </summary>
    public async Task<List<PlateDailyData>> GetPlateDailyDataAsync(long plateId, DateTime? startDate = null, DateTime? endDate = null)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var query = dbContext.PlateDailyData.Where(pd => pd.plate_id == plateId);

        if (startDate.HasValue)
            query = query.Where(pd => pd.trade_date >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(pd => pd.trade_date <= endDate.Value);

        return await query
            .OrderBy(pd => pd.trade_date)
            .ToListAsync();
    }
}