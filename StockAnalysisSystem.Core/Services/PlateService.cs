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
    public async Task<int> CalcPlateDailyDataAsync(Action<int, int, string>? progressCallback = null)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        progressCallback?.Invoke(1, 100, "正在获取板块信息...");
        // 获取所有板块
        var plates = await dbContext.Plates.ToListAsync();

        if (!plates.Any())
        {
            ErrorLogger.Log(null, "PlateService.CalcPlateDailyData", "没有板块数据，请先同步板块");
            return 0;
        }

        progressCallback?.Invoke(2, 100, "正在获取已有板块日线数据...");
        // 获取所有已有板块日线数据的日期
        var existingDates = await dbContext.PlateDailyData
            .Select(pd => pd.trade_date.Date)
            .Distinct()
            .ToListAsync();

        progressCallback?.Invoke(3, 100, "正在获取股票日线数据日期...");
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

        progressCallback?.Invoke(4, 100, $"共需计算 {datesToCalc.Count} 个日期");
        var datesToCalcList = datesToCalc.OrderBy(d => d).ToList();
        var totalDates = datesToCalcList.Count;
        int calcCount = 0;

        // 查询已存在的板块日线数据（避免重复插入）
        var existingPlateDailyData = await dbContext.PlateDailyData
            .Select(pd => new { pd.plate_id, pd.trade_date.Date })
            .ToListAsync();

        // 用于快速检查是否已存在：按板块ID分组
        var existingPlateDailyDict = existingPlateDailyData
            .GroupBy(pd => pd.plate_id)
            .ToDictionary(g => g.Key, g => g.Select(pd => pd.Date).ToHashSet());

        // 逐天计算
        for (int dayIndex = 0; dayIndex < totalDates; dayIndex++)
        {
            var date = datesToCalcList[dayIndex];
            var dateValue = date.Date;

            progressCallback?.Invoke(dayIndex + 1, totalDates, $"正在计算第 {dayIndex + 1}/{totalDates} 天 ({date:yyyy-MM-dd})...");

            // 查询当天的股票日线数据
            var dailyData = await dbContext.StockDailyData
                .Where(d => d.TradeDate.Date == dateValue)
                .Select(d => new { d.StockID, d.ChangePercent, d.Amount, d.TurnoverRate })
                .ToListAsync();

            // 查询当天的板块成分股关系
            var plateStocks = await dbContext.PlateStocks
                .ToListAsync();

            // 按板块ID分组成分股
            var plateStocksByPlateId = plateStocks.ToLookup(ps => ps.plate_id);

            var plateDailyDataList = new List<PlateDailyData>();

            foreach (var plate in plates)
            {
                // 获取该板块的成分股
                var stockCodesForPlate = plateStocksByPlateId[plate.id];
                if (stockCodesForPlate == null) continue;

                var stockCodes = stockCodesForPlate.Select(ps => ps.stock_code).ToList();

                // 转换股票代码格式：去掉前两位字母（sh/sz），只保留数字
                var stockCodesNumeric = stockCodes
                    .Select(code => code.Length > 2 ? code.Substring(2) : code)
                    .ToHashSet();

                // 加上前缀：6开头是sh，其他是sz
                var stockCodesWithPrefix = stockCodesNumeric
                    .Select(code => code.StartsWith("6") ? "sh" + code : "sz" + code)
                    .ToHashSet();

                // 从日线数据中筛选该板块的股票
                var plateDailyDataListForPlate = dailyData
                    .Where(d => stockCodesWithPrefix.Contains(d.StockID))
                    .Select(d => new { d.StockID, d.ChangePercent, d.Amount, d.TurnoverRate })
                    .ToList();

                if (plateDailyDataListForPlate.Count == 0) continue;

                // 计算统计数据
                var stockCount = plateDailyDataListForPlate.Count;
                decimal avgPctChg = plateDailyDataListForPlate.Average(d => d.ChangePercent ?? 0);
                decimal totalAmount = plateDailyDataListForPlate.Sum(d => d.Amount);
                decimal avgTurnover = plateDailyDataListForPlate.Average(d => d.TurnoverRate ?? 0);

                // 检查是否已存在
                if (existingPlateDailyDict.TryGetValue(plate.id, out var plateExistingDates) && plateExistingDates.Contains(dateValue))
                {
                    // 已存在，跳过（或者可以选择更新）
                    continue;
                }
                else
                {
                    // 新增
                    var plateDailyData = new PlateDailyData
                    {
                        plate_id = plate.id,
                        trade_date = date,
                        stock_count = stockCount,
                        limit_up_count = 0,
                        avg_pct_chg = avgPctChg,
                        total_amount = totalAmount,
                        avg_turnover = avgTurnover,
                        created_time = DateTime.Now,
                        updated_time = DateTime.Now
                    };
                    plateDailyDataList.Add(plateDailyData);
                    calcCount++;
                }
            }

            // 每个日期批量保存一次
            if (plateDailyDataList.Any())
            {
                await dbContext.PlateDailyData.AddRangeAsync(plateDailyDataList);
                await dbContext.SaveChangesAsync();

                progressCallback?.Invoke(dayIndex + 1, totalDates, $"完成第 {dayIndex + 1}/{totalDates} 天 ({date:yyyy-MM-dd}): 新增 {plateDailyDataList.Count} 个板块");
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