using Microsoft.EntityFrameworkCore;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.Utils;
using DailyPickEntity = StockAnalysisSystem.Core.Entities.DailyPick;

namespace StockAnalysisSystem.Core.DailyPick;

/// <summary>
/// 股票选股信息
/// </summary>
public class StockPickInfo
{
    public string StockId { get; set; } = string.Empty;
    public string StockCode { get; set; } = string.Empty;
    public string StockName { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public string? Sector { get; set; }
    public decimal? CirculationValue { get; set; }
    public decimal ClosePrice { get; set; }
    public decimal ChangePercent { get; set; }
    public decimal? TurnoverRate { get; set; }
    public decimal? Volume { get; set; }
}

/// <summary>
/// 选股结果
/// </summary>
public class DailyPickResult
{
    public string StockId { get; set; } = string.Empty;
    public string StockCode { get; set; } = string.Empty;
    public string StockName { get; set; } = string.Empty;
    public int StrategyId { get; set; }
    public string StrategyName { get; set; } = string.Empty;
    public string SignalType { get; set; } = "Buy";
    public string Reason { get; set; } = string.Empty;
    public decimal? DeepSeekScore { get; set; }
    public decimal FinalScore { get; set; }
    public decimal? CirculationValue { get; set; }
    public string? Industry { get; set; }

    // 新增：用于精细化评分
    public decimal ChangePercent { get; set; }
    public decimal? TurnoverRate { get; set; }
    public decimal? RSI { get; set; }
    public bool IsGoldenCross { get; set; }  // 是否金叉
    public int SignalCount { get; set; } = 1;  // 触发信号数量
}

/// <summary>
/// 每日选股器
/// </summary>
public class DailyPicker
{
    private readonly IStockRepository _stockRepo;
    private readonly IStockDailyDataRepository _dailyDataRepo;
    private readonly IIndicatorRepository _indicatorRepo;
    private readonly IStrategyRepository _strategyRepo;
    private readonly IDailyPickRepository _dailyPickRepo;
    private readonly DeepSeek.DeepSeekClient? _deepSeekClient;
    private readonly Indicators.IndicatorCalculator _calculator;
    private readonly AppDbContext _dbContext;

    public DailyPicker(
        AppDbContext dbContext,
        IStockRepository stockRepo,
        IStockDailyDataRepository dailyDataRepo,
        IIndicatorRepository indicatorRepo,
        IStrategyRepository strategyRepo,
        IDailyPickRepository dailyPickRepo,
        DeepSeek.DeepSeekClient? deepSeekClient = null)
    {
        _dbContext = dbContext;
        _stockRepo = stockRepo;
        _dailyDataRepo = dailyDataRepo;
        _indicatorRepo = indicatorRepo;
        _strategyRepo = strategyRepo;
        _dailyPickRepo = dailyPickRepo;
        _deepSeekClient = deepSeekClient;
        _calculator = new Indicators.IndicatorCalculator();
    }

    /// <summary>
    /// 执行每日选股
    /// </summary>
    public async Task<List<DailyPickResult>> PickAsync(
        DateTime tradeDate,
        List<int>? strategyIds = null,
        bool useDeepSeek = false,
        IProgress<string>? progress = null)
    {
        var results = new List<DailyPickResult>();
        var now = DateTime.Now;

        // 检查选股日期是否是今天且在开盘前
        var isTodayBeforeOpen = tradeDate.Date == now.Date && now.TimeOfDay < new TimeSpan(9, 30, 0);
        if (isTodayBeforeOpen)
        {
            progress?.Report("提示：当前为开盘前，当日无行情数据，建议选择昨日或更早日期");
        }

        progress?.Report($"开始选股，日期: {tradeDate:yyyy-MM-dd}");

        // 如果选择的是今天，自动改为上一个交易日
        var actualTradeDate = tradeDate;
        if (tradeDate.Date == now.Date)
        {
            // 查找上一个交易日
            var lastTradeDate = await GetLastTradeDateAsync(now);
            if (lastTradeDate.HasValue && lastTradeDate.Value < now.Date)
            {
                actualTradeDate = lastTradeDate.Value;
                progress?.Report($"自动调整为上一个交易日: {actualTradeDate:yyyy-MM-dd}");
            }
        }

        // 在外部定义变量，使得catch块可以访问
        int stockCount = 0;
        int strategyCount = 0;
        List<DailyPickResult> topResults = new();

        // 确保数据库连接打开
        var connection = _dbContext.Database.GetDbConnection();
        bool wasOpen = connection.State == System.Data.ConnectionState.Open;
        
        try
        {
            if (!wasOpen)
            {
                await connection.OpenAsync();
            }

            // 1. 获取启用的策略
            var strategies = await _strategyRepo.GetActiveAsync();
            if (strategyIds != null && strategyIds.Count > 0)
            {
                strategies = strategies.Where(s => strategyIds.Contains(s.Id)).ToList();
            }

            strategyCount = strategies.Count;
            if (strategies.Count == 0)
            {
                progress?.Report("没有可用的策略");
                return results;
            }

            progress?.Report($"加载了 {strategies.Count} 个策略");

            // 2. 获取所有股票和当日数据
            var stocks = await _stockRepo.GetAllAsync();
            stockCount = stocks.Count;
            var dailyDataByStock = new Dictionary<string, List<Entities.StockDailyData>>();
            var indicatorsByStock = new Dictionary<string, List<StockDailyIndicator>>();

            progress?.Report($"加载 {stocks.Count} 只股票的数据...");

            // 优化：批量查询日线数据（2次查询替代2N次查询）
            // 注意：根据策略类型决定加载天数
            // - 单独使用首板后回落策略：只需要30天
            // - 使用其他策略或组合策略：计算120日均线需要至少120天数据，为了安全起见，获取200天数据
            var isOnlyFirstBoardPullback = strategies.Count == 1 && strategies.First().StrategyType == "FirstBoardPullback";
            var startDate = isOnlyFirstBoardPullback
                ? actualTradeDate.AddDays(-30)
                : actualTradeDate.AddDays(-200);

            var loadingDays = isOnlyFirstBoardPullback ? 30 : 200;
            progress?.Report($"加载最近 {loadingDays} 天的数据...");

            // 批量获取所有股票的日线数据
            var allDailyData = await _dailyDataRepo.GetByDateRangeAsync(startDate, actualTradeDate);
            
            // 优化：如果是首板回落策略，先过滤掉不以60和00开头的股票
            List<string> filteredStockIds = stocks.Select(s => s.Id).ToList();
            if (isOnlyFirstBoardPullback)
            {
                filteredStockIds = filteredStockIds.Where(id => id.Length >= 2 && (id.StartsWith("00") || id.StartsWith("60"))).ToList();
                progress?.Report($"首板回落策略：过滤后保留 {filteredStockIds.Count}/{stocks.Count} 只股票");
            }
            
            var stockIdSet = new HashSet<string>(filteredStockIds);
            
            // 批量获取所有股票的技术指标
            var allIndicators = await _indicatorRepo.GetByDateRangeAsync(startDate, actualTradeDate);
            
            // 在内存中按股票ID分组
            var dailyDataByStockLookup = allDailyData.ToLookup(d => d.StockID);
            var indicatorsByStockLookup = allIndicators.ToLookup(i => i.StockId);
            
            // 构建结果字典
            foreach (var stock in stocks)
            {
                // 如果是首板回落策略且股票代码不符合条件，跳过
                if (isOnlyFirstBoardPullback)
                {
                    if (stock.Id.Length < 2 || (stock.Id.Substring(0, 2) != "00" && stock.Id.Substring(0, 2) != "60"))
                    {
                        continue;
                    }
                }

                try
                {
                    // 从内存中获取该股票的数据
                    var dailyData = dailyDataByStockLookup[stock.Id].ToList();
                    
                    if (dailyData.Count == 0)
                        continue;

                    dailyDataByStock[stock.Id] = dailyData;

                    // 从内存中获取该股票的指标
                    var indicators = indicatorsByStockLookup[stock.Id].ToList();

                    if (indicators.Count == 0)
                    {
                        // 没有预计算的指标，现场计算
                        indicators = Indicators.IndicatorCalculator.CalculateAll(stock.Id, dailyData);
                    }

                    indicatorsByStock[stock.Id] = indicators;
                }
                catch (Exception ex)
                {
                    ErrorLogger.Log(ex, 
                        $"Method: PickAsync | Data Loading | Stock: {stock.Id} {stock.StockCode} | TradeDate: {tradeDate:yyyy-MM-dd}", 
                        new { StockId = stock.Id, StockCode = stock.StockCode, StockName = stock.StockName, TradeDate = tradeDate });
                    // 继续处理其他股票
                }
            }

            progress?.Report($"完成数据加载，共 {dailyDataByStock.Count} 只股票");

            // 3. 对每只股票应用每个策略
            var pickDict = new Dictionary<(string, int), DailyPickResult>();

            foreach (var strategy in strategies)
            {
                progress?.Report($"应用策略: {strategy.Name}");

                var strategyInstance = Strategies.StrategyFactory.CreateFromJson(
                    strategy.StrategyType, strategy.Parameters);

                if (strategyInstance == null)
                    continue;

                foreach (var stockId in dailyDataByStock.Keys)
                {
                    var dailyData = dailyDataByStock[stockId];
                    var indicators = indicatorsByStock[stockId];
                    var stock = stocks.First(s => s.Id == stockId);

                    try
                    {
                        var signals = strategyInstance.GenerateSignals(stockId, dailyData, indicators);
                        var todaySignal = signals.FirstOrDefault(s => s.Date.Date == actualTradeDate.Date);

                        if (todaySignal != null && todaySignal.Type == Strategies.SignalType.Buy)
                        {
                            var key = (stockId, strategy.Id);
                            var latestData = dailyData.OrderByDescending(d => d.TradeDate).First();
                            var latestIndicator = indicators.OrderByDescending(i => i.TradeDate).FirstOrDefault();

                            if (!pickDict.ContainsKey(key))
                            {
                                pickDict[key] = new DailyPickResult
                                {
                                    StockId = stockId,
                                    StockCode = stock.StockCode,
                                    StockName = stock.StockName,
                                    StrategyId = strategy.Id,
                                    StrategyName = strategy.Name,
                                    SignalType = "Buy",
                                    CirculationValue = stock.CirculationValue,
                                    Industry = stock.Industry,
                                    ChangePercent = latestData.ChangePercent ?? 0m,
                                    TurnoverRate = latestData.TurnoverRate,
                                    RSI = latestIndicator?.RSI6,
                                    IsGoldenCross = todaySignal.Reason.Contains("金叉") || todaySignal.Reason.Contains("cross") || todaySignal.Reason.Contains("上穿")
                                };
                            }
                            else
                            {
                                // 记录信号数量
                                pickDict[key].SignalCount++;
                                // 检查是否有金叉信号
                                if (todaySignal.Reason.Contains("金叉") || todaySignal.Reason.Contains("cross") || todaySignal.Reason.Contains("上穿"))
                                {
                                    pickDict[key].IsGoldenCross = true;
                                }
                            }

                            pickDict[key].Reason += $"[{strategy.Name}] {todaySignal.Reason}; ";
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorLogger.Log(ex,
                            $"Method: PickAsync | Strategy Application | Stock: {stockId} {stock.StockCode} | Strategy: {strategy.Name} | TradeDate: {tradeDate:yyyy-MM-dd}",
                            new { StockId = stockId, StockCode = stock.StockCode, StockName = stock.StockName, StrategyId = strategy.Id, StrategyName = strategy.Name, TradeDate = tradeDate });
                        // 继续处理其他股票
                    }
                }
            }

            results = pickDict.Values.ToList();
            progress?.Report($"初步选出 {results.Count} 只股票");

            // 4. 可选：DeepSeek评分（限制最多100只股票避免超时）
            if (useDeepSeek && _deepSeekClient != null && results.Count > 0)
            {
                // 优先按市值筛选大市值股票，最多100只
                var topStocks = results
                    .Where(r => r.CirculationValue.HasValue)
                    .OrderByDescending(r => r.CirculationValue)
                    .Take(100)
                    .ToList();

                // 如果筛选后不足100只，补充其他股票
                if (topStocks.Count < 100)
                {
                    var remaining = results
                        .Where(r => !r.CirculationValue.HasValue || r.CirculationValue < topStocks.LastOrDefault()?.CirculationValue)
                        .Take(100 - topStocks.Count);
                    topStocks.AddRange(remaining);
                }

                progress?.Report($"正在调用DeepSeek进行评分（共{topStocks.Count}只股票）...");

                var pickInfos = topStocks.Select(r => new StockPickInfo
                {
                    StockId = r.StockId,
                    StockCode = r.StockCode,
                    StockName = r.StockName,
                    Industry = r.Industry,
                    CirculationValue = r.CirculationValue
                }).ToList();

                try
                {
                    var scores = await _deepSeekClient.ScoreStocksAsync(pickInfos);
                    foreach (var score in scores)
                    {
                        var result = results.FirstOrDefault(r => r.StockCode == score.Key);
                        if (result != null)
                        {
                            result.DeepSeekScore = score.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorLogger.Log(ex,
                        $"Method: PickAsync | DeepSeek Scoring | TradeDate: {tradeDate:yyyy-MM-dd}",
                        new { TradeDate = tradeDate, StockCount = pickInfos.Count });
                    progress?.Report($"DeepSeek评分失败: {ex.Message}");
                }
            }

            // 5. 计算最终得分并排序（精细化评分系统）
            foreach (var result in results)
            {
                // DeepSeek评分 (权重40%) - 如果没有DeepSeek评分，默认70分
                var deepSeekScore = (result.DeepSeekScore ?? 70m) * 0.4m;

                // 技术指标得分 (权重30%)
                var technicalScore = 0m;
                // RSI: 30-60最佳 (30-60得分80-100，其他40-70)
                if (result.RSI.HasValue)
                {
                    if (result.RSI >= 30m && result.RSI <= 60m)
                    {
                        technicalScore += 0.6m * 30m;  // RSI得分
                    }
                    else if (result.RSI > 60m && result.RSI < 80m)
                    {
                        technicalScore += 0.5m * 30m;  // RSI稍高
                    }
                    else
                    {
                        technicalScore += 0.4m * 30m;  // RSI不佳
                    }
                }
                else
                {
                    technicalScore += 0.5m * 30m;  // 无RSI数据默认中等
                }
                // 金叉信号加分
                if (result.IsGoldenCross)
                {
                    technicalScore += 0.4m * 30m;
                }

                // 市场表现得分 (权重20%)
                var marketScore = 0m;
                // 涨跌幅: 1%-5%适中最佳
                if (result.ChangePercent >= 1m && result.ChangePercent <= 5m)
                {
                    marketScore += 0.8m * 20m;  // 适中涨幅
                }
                else if (result.ChangePercent >= 0m && result.ChangePercent < 1m)
                {
                    marketScore += 0.6m * 20m;  // 小幅上涨
                }
                else if (result.ChangePercent > 5m && result.ChangePercent <= 8m)
                {
                    marketScore += 0.5m * 20m;  // 涨幅较大
                }
                else if (result.ChangePercent < 0m && result.ChangePercent >= -3m)
                {
                    marketScore += 0.4m * 20m;  // 小幅下跌可能是洗盘
                }
                else
                {
                    marketScore += 0.3m * 20m;  // 其他情况
                }

                // 换手率: 2%-10%活跃度好
                if (result.TurnoverRate.HasValue)
                {
                    if (result.TurnoverRate >= 2m && result.TurnoverRate <= 10m)
                    {
                        marketScore += 0.2m * 20m;
                    }
                    else if (result.TurnoverRate > 10m)
                    {
                        marketScore += 0.1m * 20m;  // 换手率过高可能是炒作
                    }
                    else
                    {
                        marketScore += 0.15m * 20m;
                    }
                }

                // 策略信号数量得分 (权重10%)
                var signalScore = Math.Min(result.SignalCount, 5) * 2m * 10m;  // 最多5个信号

                // 最终得分
                result.FinalScore = deepSeekScore + technicalScore + marketScore + signalScore;
            }

            results = results.OrderByDescending(r => r.FinalScore).ToList();

            // 6. 按策略筛选，只保留每种策略评分前5的股票
            topResults = results
                //.GroupBy(r => r.StrategyId)
                //.SelectMany(g => g.Take(5))
                //.OrderByDescending(r => r.FinalScore)
                .ToList();

            progress?.Report($"筛选后保留 {topResults.Count} 只股票（每种策略前5）");

            // 7. 保存到数据库
            progress?.Report("保存选股结果...");
            var dailyPicks = topResults.Select(r => new DailyPickEntity
            {
                TradeDate = actualTradeDate,
                StockId = r.StockId,
                StockCode = r.StockCode,
                StockName = r.StockName,
                StrategyId = r.StrategyId,
                SignalType = r.SignalType,
                Reason = r.Reason,
                DeepSeekScore = r.DeepSeekScore,
                FinalScore = r.FinalScore
            }).ToList();

            // 先删除当日已有结果，再插入新结果（同一事务）
            await _dailyPickRepo.ReplaceByDateAsync(actualTradeDate, dailyPicks);

            progress?.Report($"选股完成，共选出 {topResults.Count} 只股票");
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, 
                $"Method: PickAsync | Overall Error | TradeDate: {tradeDate:yyyy-MM-dd}", 
                new { TradeDate = tradeDate, StockCount = stockCount, StrategyCount = strategyCount });
            progress?.Report($"选股失败: {ex.Message}");
        }
        finally
        {
            // 如果原来连接是关闭的，操作完成后关闭
            if (!wasOpen && connection.State == System.Data.ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }

        return topResults;
    }

    /// <summary>
    /// 获取历史选股结果
    /// </summary>
    public async Task<List<DailyPickResult>> GetHistoryAsync(DateTime date)
    {
        var picks = await _dailyPickRepo.GetByDateAsync(date);

        return picks.Select(p => new DailyPickResult
        {
            StockId = p.StockId,
            StockCode = p.StockCode,
            StockName = p.StockName,
            StrategyId = p.StrategyId,
            StrategyName = p.Strategy?.Name ?? "",
            SignalType = p.SignalType,
            Reason = p.Reason ?? "",
            DeepSeekScore = p.DeepSeekScore,
            FinalScore = p.FinalScore ?? 0,
            CirculationValue = null,  // 历史数据不保存市值等信息
            Industry = null,
            ChangePercent = 0,
            TurnoverRate = null,
            RSI = null,
            IsGoldenCross = false,
            SignalCount = 1
        }).ToList();
    }

    /// <summary>
    /// 获取指定日期之前的最后一个交易日
    /// </summary>
    private async Task<DateTime?> GetLastTradeDateAsync(DateTime date)
    {
        // 获取数据库中最近一个有数据的交易日
        var latestDate = await _dailyDataRepo.GetLatestTradeDateAsync();

        // 如果找到的日期在今天之前，返回它
        if (latestDate.HasValue && latestDate.Value < date.Date)
        {
            return latestDate.Value;
        }

        return null;
    }
}
