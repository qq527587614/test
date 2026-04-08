using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Indicators;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.Strategies;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.Core.Backtest;

/// <summary>
/// 回测引擎
/// </summary>
public class BacktestEngine
{
    private readonly IStockRepository _stockRepo;
    private readonly IStockDailyDataRepository _dailyDataRepo;
    private readonly IIndicatorRepository _indicatorRepo;
    private readonly IBacktestTaskRepository _backtestTaskRepo;
    private readonly IDailyPickRepository _dailyPickRepo;
    private readonly IStrategyRepository _strategyRepo;
    private readonly ILogger<BacktestEngine>? _logger;

    public BacktestEngine(
        IStockRepository stockRepo,
        IStockDailyDataRepository dailyDataRepo,
        IIndicatorRepository indicatorRepo,
        IBacktestTaskRepository backtestTaskRepo,
        IDailyPickRepository dailyPickRepo,
        IStrategyRepository strategyRepo,
        ILogger<BacktestEngine>? logger = null)
    {
        _stockRepo = stockRepo;
        _dailyDataRepo = dailyDataRepo;
        _indicatorRepo = indicatorRepo;
        _backtestTaskRepo = backtestTaskRepo;
        _dailyPickRepo = dailyPickRepo;
        _strategyRepo = strategyRepo;
        _logger = logger;
    }

    /// <summary>
    /// 执行回测
    /// </summary>
    public async Task<BacktestResult> RunAsync(
        IStrategy strategy,
        DateTime startDate,
        DateTime endDate,
        BacktestSettings? settings = null,
        List<string>? stockIds = null,
        IProgress<BacktestProgress>? progress = null)
    {
        settings ??= new BacktestSettings();
        var result = new BacktestResult
        {
            StartDate = startDate,
            EndDate = endDate,
            InitialCapital = settings.InitialCapital
        };

        try
        {
            // 1. 获取股票列表
            progress?.Report(new BacktestProgress { Message = "加载股票列表...", CurrentStep = 0, TotalSteps = 5 });

            var stocks = await _stockRepo.GetAllAsync();
            if (stockIds != null && stockIds.Count > 0)
            {
                stocks = stocks.Where(s => stockIds.Contains(s.Id)).ToList();
            }

            // 2. 获取交易日历
            progress?.Report(new BacktestProgress { Message = "获取交易日历...", CurrentStep = 1, TotalSteps = 5 });
            var tradeDates = await _dailyDataRepo.GetTradeDatesAsync(startDate, endDate);
            result.TradingDays = tradeDates.Count;

            // 3. 预加载所有股票的日线数据（批量查询避免并发问题）
            progress?.Report(new BacktestProgress { Message = "加载历史数据...", CurrentStep = 2, TotalSteps = 5 });

            var allDailyData = new ConcurrentDictionary<string, List<StockDailyData>>();
            var allIndicators = new ConcurrentDictionary<string, List<StockDailyIndicator>>();

            // 批量加载所有日线数据，然后按股票分组
            var allData = await _dailyDataRepo.GetByDateRangeAsync(startDate.AddDays(-100), endDate);
            var dailyDataByStock = allData.ToLookup(d => d.StockID);

            // 批量加载所有指标数据，然后按股票分组
            var allIndData = await _indicatorRepo.GetByDateRangeAsync(startDate.AddDays(-100), endDate);
            var indicatorsByStock = allIndData.ToLookup(i => i.StockId);

            foreach (var stock in stocks)
            {
                var dailyData = dailyDataByStock[stock.Id].ToList();
                var indicators = indicatorsByStock[stock.Id].ToList();

                if (dailyData.Count > 0)
                {
                    allDailyData[stock.Id] = dailyData;
                    // 如果没有预计算指标，现场计算
                    allIndicators[stock.Id] = indicators.Count > 0
                        ? indicators
                        : Indicators.IndicatorCalculator.CalculateAll(stock.Id, dailyData);
                }
            }

            // 4. 生成信号
            progress?.Report(new BacktestProgress { Message = "生成交易信号...", CurrentStep = 3, TotalSteps = 5 });

            var allSignals = new ConcurrentDictionary<string, List<Signal>>();
            var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
            await Parallel.ForEachAsync(stocks, options, async (stock, ct) =>
            {
                try
                {
                    if (allDailyData.TryGetValue(stock.Id, out var dailyData) &&
                        allIndicators.TryGetValue(stock.Id, out var indicators))
                    {
                        var signals = strategy.GenerateSignals(stock.Id, dailyData, indicators);
                        allSignals[stock.Id] = signals;
                    }
                }
                catch (Exception ex)
                {
                    ErrorLogger.Log(ex, 
                        $"Method: RunAsync | Parallel Signal Generation | Stock: {stock.Id} {stock.StockCode} | Strategy: {strategy.GetType().Name}", 
                        new { StockId = stock.Id, StockCode = stock.StockCode, StockName = stock.StockName, StrategyName = strategy.GetType().Name });
                    // 继续执行其他股票
                }
            });

            // 5. 模拟交易执行
            progress?.Report(new BacktestProgress { Message = "模拟交易执行...", CurrentStep = 4, TotalSteps = 5 });

            var portfolio = new Dictionary<string, Position>();
            var trades = new List<TradeRecord>();
            var equityCurve = new List<EquityPoint>();
            decimal cash = settings.InitialCapital;
            int currentDateIndex = 0;

            foreach (var date in tradeDates)
            {
                currentDateIndex++;
                var dailySignals = new List<Signal>();

                // 收集当日信号
                foreach (var kvp in allSignals)
                {
                    var dateSignals = kvp.Value.Where(s => s.Date == date).ToList();
                    dailySignals.AddRange(dateSignals);
                }

                // ====== 1. 先处理止损（亏损达到设定比例时卖出）======
                var positionsToClose = new List<(string StockId, string StockCode, string StockName, Position Position, decimal SellPrice, decimal Commission, string Reason)>();
                
                foreach (var kvp in portfolio.ToList())
                {
                    var stock = stocks.First(s => s.Id == kvp.Key);
                    if (allDailyData.TryGetValue(kvp.Key, out var dailyData))
                    {
                        var todayData = dailyData.FirstOrDefault(d => d.TradeDate == date);
                        if (todayData != null)
                        {
                            // 计算当前亏损比例
                            var currentPrice = todayData.ClosePrice;
                            var costPrice = kvp.Value.CostPrice;
                            var profitLossPercent = (currentPrice - costPrice) / costPrice * 100;

                            // 如果亏损达到止损线，卖出
                            if (profitLossPercent <= settings.StopLossPercent)
                            {
                                var sellPrice = todayData.OpenPrice * (1 - settings.Slippage);
                                var sellAmount = sellPrice * kvp.Value.Shares;
                                var commission = sellAmount * settings.Commission;
                                
                                positionsToClose.Add((kvp.Key, kvp.Value.StockCode, stock.StockName, kvp.Value, sellPrice, commission, $"止损({profitLossPercent:F1}%)"));
                            }
                        }
                    }
                }

                // 执行止损卖出
                foreach (var closeInfo in positionsToClose)
                {
                    cash += closeInfo.SellPrice * closeInfo.Position.Shares - closeInfo.Commission;

                    var trade = new TradeRecord
                    {
                        StockId = closeInfo.StockId,
                        StockCode = closeInfo.StockCode,
                        StockName = closeInfo.StockName,
                        StrategyName = strategy.Name,
                        BuyDate = closeInfo.Position.BuyDate,
                        BuyPrice = closeInfo.Position.CostPrice,
                        Shares = closeInfo.Position.Shares,
                        SellDate = date,
                        SellPrice = closeInfo.SellPrice,
                        Commission = closeInfo.Commission,
                        SellReason = closeInfo.Reason,
                        HoldingDays = (date - closeInfo.Position.BuyDate).Days
                    };

                    trade.ProfitLoss = (closeInfo.SellPrice - closeInfo.Position.CostPrice) * closeInfo.Position.Shares - closeInfo.Commission;
                    trade.ProfitLossPercent = (closeInfo.SellPrice - closeInfo.Position.CostPrice) / closeInfo.Position.CostPrice * 100;

                    trades.Add(trade);
                    portfolio.Remove(closeInfo.StockId);
                }

                // ====== 2. 再处理策略卖出信号 ======
                foreach (var signal in dailySignals.Where(s => s.Type == SignalType.Sell))
                {
                    if (portfolio.TryGetValue(signal.StockId, out var position))
                    {
                        var stock = stocks.First(s => s.Id == signal.StockId);
                        if (allDailyData.TryGetValue(signal.StockId, out var dailyData))
                        {
                            var todayData = dailyData.FirstOrDefault(d => d.TradeDate == date);
                            if (todayData != null)
                            {
                                var sellPrice = todayData.OpenPrice * (1 - settings.Slippage);
                                var sellAmount = sellPrice * position.Shares;
                                var commission = sellAmount * settings.Commission;

                                cash += sellAmount - commission;

                                var trade = new TradeRecord
                                {
                                    StockId = position.StockId,
                                    StockCode = position.StockCode,
                                    StockName = stock.StockName,
                                    StrategyName = strategy.Name,
                                    BuyDate = position.BuyDate,
                                    BuyPrice = position.CostPrice,
                                    Shares = position.Shares,
                                    SellDate = date,
                                    SellPrice = sellPrice,
                                    Commission = commission,
                                    SellReason = signal.Reason,
                                    HoldingDays = (date - position.BuyDate).Days
                                };

                                trade.ProfitLoss = (sellPrice - position.CostPrice) * position.Shares - commission;
                                trade.ProfitLossPercent = (sellPrice - position.CostPrice) / position.CostPrice * 100;

                                trades.Add(trade);
                                portfolio.Remove(signal.StockId);
                            }
                        }
                    }
                }

                // ====== 3. 处理买入信号 ======
                // 每次买入使用可用资金的10%（而不是总资金的10%）
                // 只有在有可用资金且未达到最大持仓数时才买入
                if (cash > 0 && portfolio.Count < settings.MaxPositions)
                {
                    var buySignals = dailySignals
                        .Where(s => s.Type == SignalType.Buy && !portfolio.ContainsKey(s.StockId))
                        .OrderByDescending(s => s.Strength)
                        .ToList();

                    foreach (var signal in buySignals)
                    {
                        // 检查是否已达到最大持仓数
                        if (portfolio.Count >= settings.MaxPositions)
                            break;

                        var stock = stocks.First(s => s.Id == signal.StockId);
                        if (allDailyData.TryGetValue(signal.StockId, out var dailyData))
                        {
                            var todayData = dailyData.FirstOrDefault(d => d.TradeDate == date);
                            if (todayData != null)
                            {
                                var buyPrice = todayData.OpenPrice * (1 + settings.Slippage);
                                // 使用可用资金的10%
                                var positionSize = cash * settings.PositionSize;
                                var shares = (int)(positionSize / buyPrice / 100) * 100; // 整手买入

                                if (shares > 0)
                                {
                                    var buyAmount = buyPrice * shares;
                                    var commission = buyAmount * settings.Commission;
                                    var totalCost = buyAmount + commission;

                                    if (totalCost <= cash)
                                    {
                                        cash -= totalCost;

                                        portfolio[signal.StockId] = new Position
                                        {
                                            StockId = signal.StockId,
                                            StockCode = stock.StockCode,
                                            Shares = shares,
                                            CostPrice = buyPrice,
                                            BuyDate = date
                                        };
                                    }
                                }
                            }
                        }
                    }
                }

                // 计算当日市值
                decimal positionValue = 0;
                foreach (var kvp in portfolio)
                {
                    if (allDailyData.TryGetValue(kvp.Key, out var dailyData))
                    {
                        var todayData = dailyData.FirstOrDefault(d => d.TradeDate == date);
                        if (todayData != null)
                        {
                            positionValue += todayData.ClosePrice * kvp.Value.Shares;
                        }
                    }
                }

                equityCurve.Add(new EquityPoint
                {
                    Date = date,
                    Cash = cash,
                    PositionValue = positionValue,
                    Equity = cash + positionValue
                });
            }

            // 清算剩余持仓
            foreach (var kvp in portfolio.ToList())
            {
                var stock = stocks.First(s => s.Id == kvp.Key);
                if (allDailyData.TryGetValue(kvp.Key, out var dailyData))
                {
                    var lastData = dailyData.OrderByDescending(d => d.TradeDate).FirstOrDefault();
                    if (lastData != null)
                    {
                        var sellPrice = lastData.ClosePrice;
                        var sellAmount = sellPrice * kvp.Value.Shares;
                        var commission = sellAmount * settings.Commission;
                        cash += sellAmount - commission;

                        var trade = new TradeRecord
                        {
                            StockId = kvp.Key,
                            StockCode = kvp.Value.StockCode,
                            StockName = stock.StockName,
                            StrategyName = strategy.Name,
                            BuyDate = kvp.Value.BuyDate,
                            BuyPrice = kvp.Value.CostPrice,
                            Shares = kvp.Value.Shares,
                            SellDate = lastData.TradeDate,
                            SellPrice = sellPrice,
                            Commission = commission,
                            SellReason = "回测结束清算",
                            HoldingDays = (lastData.TradeDate - kvp.Value.BuyDate).Days
                        };

                        trade.ProfitLoss = (sellPrice - kvp.Value.CostPrice) * kvp.Value.Shares - commission;
                        trade.ProfitLossPercent = (sellPrice - kvp.Value.CostPrice) / kvp.Value.CostPrice * 100;
                        trades.Add(trade);
                    }
                }
            }

            result.Trades = trades;
            result.EquityCurve = equityCurve;
            result.FinalEquity = cash;

            // 计算绩效指标
            CalculatePerformanceMetrics(result, settings.InitialCapital);

            progress?.Report(new BacktestProgress { Message = "回测完成", CurrentStep = 5, TotalSteps = 5 });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "回测执行失败");
            throw;
        }

        return result;
    }

    private void CalculatePerformanceMetrics(BacktestResult result, decimal initialCapital)
    {
        // 计算总收益率（基于最终资金）
        result.TotalReturn = (result.FinalEquity - initialCapital) / initialCapital * 100;
        
        // 年化收益率
        var years = result.TradingDays / 252m;
        if (years > 0)
        {
            result.AnnualReturn = (decimal)Math.Pow((double)(result.FinalEquity / initialCapital), 1.0 / (double)years) - 1;
            result.AnnualReturn *= 100;
        }

        // 最大回撤（基于资金曲线）
        if (result.EquityCurve.Count > 0)
        {
            decimal peak = initialCapital;
            decimal maxDrawdown = 0;
            foreach (var point in result.EquityCurve)
            {
                if (point.Equity > peak)
                    peak = point.Equity;
                var drawdown = (peak - point.Equity) / peak;
                if (drawdown > maxDrawdown)
                    maxDrawdown = drawdown;
            }
            result.MaxDrawdown = maxDrawdown * 100;
        }

        // 夏普比率
        if (result.EquityCurve.Count > 1)
        {
            var returns = new List<decimal>();
            for (int i = 1; i < result.EquityCurve.Count; i++)
            {
                if (result.EquityCurve[i - 1].Equity > 0)
                {
                    var dailyReturn = (result.EquityCurve[i].Equity - result.EquityCurve[i - 1].Equity) 
                        / result.EquityCurve[i - 1].Equity;
                    returns.Add(dailyReturn);
                }
            }
            
            if (returns.Count > 0)
            {
                var avgReturn = returns.Average();
                var stdDev = (decimal)Math.Sqrt(returns.Select(r => (double)((r - avgReturn) * (r - avgReturn))).Average());
                
                if (stdDev > 0)
                    result.SharpeRatio = avgReturn / stdDev * (decimal)Math.Sqrt(252);
            }
        }

        // 交易统计（只有有交易时才计算）
        if (result.Trades.Count > 0)
        {
            result.TradeCount = result.Trades.Count;
            result.WinCount = result.Trades.Count(t => t.ProfitLoss > 0);
            result.LossCount = result.Trades.Count(t => t.ProfitLoss < 0);
            result.WinRate = result.TradeCount > 0 ? (decimal)result.WinCount / result.TradeCount * 100 : 0;

            var profits = result.Trades.Where(t => t.ProfitLoss > 0).Select(t => t.ProfitLoss).ToList();
            var losses = result.Trades.Where(t => t.ProfitLoss < 0).Select(t => Math.Abs(t.ProfitLoss)).ToList();

            result.AverageProfit = profits.Count > 0 ? profits.Average() : 0;
            result.AverageLoss = losses.Count > 0 ? losses.Average() : 0;
            result.MaxProfit = profits.Count > 0 ? profits.Max() : 0;
            result.MaxLoss = losses.Count > 0 ? losses.Max() : 0;

            if (result.AverageLoss > 0)
                result.ProfitFactor = result.AverageProfit / result.AverageLoss;
        }
    }

    /// <summary>
    /// 执行每日选股回测
    /// </summary>
    public async Task<BacktestResult> RunDailyPickBacktestAsync(
        DateTime startDate,
        DateTime endDate,
        DailyPickBacktestSettings? settings = null,
        IProgress<string>? progress = null)
    {
        settings ??= new DailyPickBacktestSettings { StartDate = startDate, EndDate = endDate };

        // 根据是否选择了策略，调用不同的回测方法
        if (settings.StrategyIds != null && settings.StrategyIds.Count > 0)
        {
            // 模式1：组合策略回测
            return await RunStrategyBacktestAsync(startDate, endDate, settings, progress);
        }
        else
        {
            // 模式2：历史选股记录回测
            return await RunHistoryBacktestAsync(startDate, endDate, settings, progress);
        }
    }

    private async Task<BacktestResult> RunStrategyBacktestAsync(
        DateTime startDate,
        DateTime endDate,
        DailyPickBacktestSettings settings,
        IProgress<string>? progress)
    {
        var result = new BacktestResult
        {
            StartDate = startDate,
            EndDate = endDate,
            InitialCapital = 0
        };

        try
        {
            // 1. 加载选中的策略
            progress?.Report($"加载 {settings.StrategyIds!.Count} 个策略...");
            var strategies = await _strategyRepo.GetByIdsAsync(settings.StrategyIds);

            // 2. 加载所有股票信息
            progress?.Report("加载股票信息...");
            var stocks = await _stockRepo.GetAllAsync();

            // 检查是否所有策略都是首板回落策略
            var isOnlyFirstBoardPullback = strategies.Count > 0 &&
                strategies.All(s => s.StrategyType == "FirstBoardPullback");

            // 如果都是首板回落策略，过滤股票（只保留60和00开头的）
            if (isOnlyFirstBoardPullback)
            {
                var originalCount = stocks.Count;
                stocks = stocks.Where(s => s.StockCode.StartsWith("00") || s.StockCode.StartsWith("60")).ToList();
                progress?.Report($"首板回落策略：过滤后保留 {stocks.Count}/{originalCount} 只股票");
            }

            // 3. 加载日线数据
            progress?.Report("加载日线数据...");
            var allDailyData = await _dailyDataRepo.GetByDateRangeAsync(
                startDate.AddDays(-200),
                endDate.AddDays(30));

            var dailyDataByStock = allDailyData.ToLookup(d => d.StockID);

            // 4. 手动计算技术指标（不依赖数据库）
            progress?.Report("计算技术指标...");
            var indicatorsByStock = new Dictionary<string, List<StockDailyIndicator>>();
            var stocksDict = stocks.ToDictionary(s => s.StockID);

            foreach (var stock in stocks)
            {
                var dailyData = dailyDataByStock[stock.StockID].ToList();
                if (dailyData.Count == 0) continue;

                // 使用IndicatorCalculator手动计算
                var indicators = IndicatorCalculator.CalculateAll(stock.StockID, dailyData);
                indicatorsByStock[stock.StockID] = indicators;
            }

            // 5. 创建策略实例
            var strategyInstances = new List<Strategies.IStrategy>();
            foreach (var strategy in strategies)
            {
                var strategyInstance = Strategies.StrategyFactory.CreateFromJson(
                    strategy.StrategyType, strategy.Parameters);
                if (strategyInstance != null)
                {
                    strategyInstances.Add(strategyInstance);
                }
            }

            if (strategyInstances.Count == 0)
            {
                progress?.Report("没有有效的策略实例");
                return result;
            }

            // 6. 获取交易日历
            var tradingDays = dailyDataByStock
                .SelectMany(d => d)
                .Where(d => d.TradeDate >= startDate && d.TradeDate <= endDate)
                .Select(d => d.TradeDate.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            progress?.Report($"回测区间: {startDate:yyyy-MM-dd} 到 {endDate:yyyy-MM-dd}, 共 {tradingDays.Count} 个交易日");
            result.TradingDays = tradingDays.Count;

            progress?.Report("开始回测...");
            var trades = new List<TradeRecord>();
            var stopLossPercent = -5m;

            // 6. 遍历每个交易日
            foreach (var tradeDate in tradingDays)
            {
                progress?.Report($"处理交易日: {tradeDate:yyyy-MM-dd}");

                // 获取该交易日所有股票的买入信号
                var buySignals = new List<Strategies.Signal>();

                foreach (var stock in stocks)
                {
                    if (!dailyDataByStock.Contains(stock.StockID)) continue;

                    var dailyData = dailyDataByStock[stock.StockID].ToList();
                    if (dailyData.Count == 0) continue;

                    if (!indicatorsByStock.ContainsKey(stock.StockID)) continue;
                    var indicators = indicatorsByStock[stock.StockID];

                    // 使用AND逻辑组合所有策略
                    var combinedSignal = await CombineStrategiesAsync(
                        strategyInstances,
                        stock,
                        dailyData,
                        indicators,
                        tradeDate);

                    if (combinedSignal != null && combinedSignal.Type == Strategies.SignalType.Buy)
                    {
                        buySignals.Add(combinedSignal);
                    }
                }

                // 7. 执行买入
                foreach (var signal in buySignals)
                {
                    var stockData = dailyDataByStock[signal.StockId]
                        .FirstOrDefault(d => d.TradeDate == tradeDate);

                    if (stockData == null) continue;

                    var stock = stocksDict.GetValueOrDefault(signal.StockId);
                    if (stock == null) continue;

                    var buyPrice = stockData.ClosePrice * (1 + settings.Slippage);
                    var trade = new TradeRecord
                    {
                        StockId = signal.StockId,
                        StockCode = stock.StockCode,
                        StockName = stock.StockName,
                        StrategyName = string.Join(" + ", strategyInstances.Select(s => s.Name)),
                        BuyDate = tradeDate,
                        BuyPrice = buyPrice,
                        Shares = settings.SharesPerPick
                    };

                    trades.Add(trade);
                }

                // 8. 检查卖出
                await CheckSellSignalsForStrategyBacktestAsync(
                    tradeDate,
                    trades,
                    strategyInstances,
                    settings,
                    dailyDataByStock,
                    indicatorsByStock,
                    stopLossPercent);
            }

            result.Trades = trades;
            progress?.Report($"回测完成，共 {trades.Count} 笔交易");

            // 计算绩效指标
            CalculateDailyPickPerformanceMetrics(result);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "组合策略回测失败");
            progress?.Report($"回测失败: {ex.Message}");
        }

        return result;
    }

    private async Task<BacktestResult> RunHistoryBacktestAsync(
        DateTime startDate,
        DateTime endDate,
        DailyPickBacktestSettings settings,
        IProgress<string>? progress)
    {
        var result = new BacktestResult
        {
            StartDate = startDate,
            EndDate = endDate,
            InitialCapital = 0
        };

        try
        {
            progress?.Report("加载每日选股记录...");
            var dailyPicks = await _dailyPickRepo.GetByDateRangeAsync(startDate, endDate);

            if (dailyPicks.Count == 0)
            {
                progress?.Report("没有找到每日选股记录");
                return result;
            }

            progress?.Report($"加载了 {dailyPicks.Count} 条每日选股记录");

            // 获取所有涉及的策略ID
            var strategyIds = dailyPicks.Select(p => p.StrategyId).Distinct().ToList();
            progress?.Report($"加载 {strategyIds.Count} 个策略...");

            // 加载所有策略
            var strategies = await _strategyRepo.GetByIdsAsync(strategyIds);
            var strategyDict = strategies.ToDictionary(s => s.Id);

            // 获取所有涉及的股票ID
            var stockIds = dailyPicks.Select(p => p.StockId).Distinct().ToList();
            progress?.Report($"涉及 {stockIds.Count} 只股票");

            // 加载所有股票的日线数据
            progress?.Report("加载日线数据...");
            var allDailyData = await _dailyDataRepo.GetByDateRangeAsync(
                startDate.AddDays(-100),
                endDate.AddDays(30));

            var dailyDataByStock = allDailyData.ToLookup(d => d.StockID);

            // 加载所有股票的指标数据
            progress?.Report("加载指标数据...");
            var allIndicators = await _indicatorRepo.GetByDateRangeAsync(
                startDate.AddDays(-100),
                endDate.AddDays(30));
            var indicatorsByStock = allIndicators.ToLookup(i => i.StockId);

            result.TradingDays = dailyDataByStock.First().Count();

            progress?.Report("开始回测...");
            var trades = new List<TradeRecord>();
            var skippedCount = 0;
            var stopLossPercent = -5m;

            foreach (var pick in dailyPicks)
            {
                try
                {
                    // 获取选股当日的开盘价
                    var stockData = dailyDataByStock[pick.StockId]
                        .FirstOrDefault(d => d.TradeDate == pick.TradeDate);

                    if (stockData == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    var buyPrice = stockData.OpenPrice * (1 + settings.Slippage);
                    var buyDate = pick.TradeDate;

                    // 逐日检查卖出条件
                    string sellReason = "";
                    decimal? finalSellPrice = null;
                    DateTime? finalSellDate = null;

                    // 获取该股票的所有日线数据（买入日之后）
                    var stockDailyData = dailyDataByStock[pick.StockId]
                        .Where(d => d.TradeDate > buyDate)
                        .OrderBy(d => d.TradeDate)
                        .ToList();

                    // 获取该股票的所有指标数据
                    var stockIndicators = indicatorsByStock[pick.StockId]
                        .OrderBy(d => d.TradeDate)
                        .ToList();

                    // 获取策略实例
                    if (!strategyDict.TryGetValue(pick.StrategyId, out var strategy))
                    {
                        skippedCount++;
                        continue;
                    }

                    var strategyInstance = Strategies.StrategyFactory.CreateFromJson(
                        strategy.StrategyType, strategy.Parameters);

                    if (strategyInstance == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    // 逐日检查
                    foreach (var dayData in stockDailyData)
                    {
                        // 检查止损
                        var currentPrice = dayData.ClosePrice;
                        var currentProfitLossPercent = (currentPrice - buyPrice) / buyPrice * 100;

                        if (currentProfitLossPercent <= stopLossPercent)
                        {
                            // 触发止损
                            finalSellPrice = dayData.ClosePrice * (1 - settings.Slippage);
                            finalSellDate = dayData.TradeDate;
                            sellReason = $"止损({stopLossPercent:F1}%)";
                            break;
                        }

                        // 检查策略卖出信号
                        var todayIndicators = stockIndicators
                            .Where(i => i.TradeDate.Date == dayData.TradeDate.Date)
                            .ToList();

                        if (todayIndicators.Count > 0)
                        {
                            var signals = strategyInstance.GenerateSignals(
                                pick.StockId,
                                stockDailyData.Where(d => d.TradeDate <= dayData.TradeDate).ToList(),
                                todayIndicators);

                            var sellSignal = signals.FirstOrDefault(s =>
                                s.Type == Strategies.SignalType.Sell &&
                                s.Date.Date == dayData.TradeDate.Date);

                            if (sellSignal != null)
                            {
                                finalSellPrice = dayData.OpenPrice * (1 - settings.Slippage);
                                finalSellDate = dayData.TradeDate;
                                sellReason = $"策略卖出({strategy.Name})";
                                break;
                            }
                        }
                    }

                    // 如果没有触发卖出，使用最后一个交易日收盘价卖出
                    if (!finalSellPrice.HasValue && stockDailyData.Count > 0)
                    {
                        var lastData = stockDailyData.Last();
                        finalSellPrice = lastData.ClosePrice * (1 - settings.Slippage);
                        finalSellDate = lastData.TradeDate;
                        sellReason = "回测结束";
                    }

                    if (!finalSellPrice.HasValue)
                    {
                        skippedCount++;
                        continue;
                    }

                    var sellPrice = finalSellPrice.Value;
                    var sellDate = finalSellDate.Value;

                    // 计算交易盈亏
                    var shares = settings.SharesPerPick;
                    var buyAmount = buyPrice * shares;
                    var sellAmount = sellPrice * shares;
                    var commission = (buyAmount + sellAmount) * settings.Commission;
                    var profitLoss = sellAmount - buyAmount - commission;
                    var profitLossPercent = profitLoss / buyAmount * 100;
                    var holdingDays = (sellDate - buyDate).Days;

                    // 记录交易
                    trades.Add(new TradeRecord
                    {
                        StockId = pick.StockId,
                        StockCode = pick.StockCode,
                        StockName = pick.StockName,
                        StrategyName = strategy.Name,
                        BuyDate = buyDate,
                        BuyPrice = buyPrice,
                        Shares = shares,
                        SellDate = sellDate,
                        SellPrice = sellPrice,
                        ProfitLoss = profitLoss,
                        ProfitLossPercent = profitLossPercent,
                        Commission = commission,
                        HoldingDays = holdingDays,
                        SellReason = sellReason
                    });
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "回测选股记录失败: {StockCode} {Date}", pick.StockCode, pick.TradeDate);
                    skippedCount++;
                }
            }

            result.Trades = trades;
            progress?.Report($"回测完成，共 {trades.Count} 笔交易，跳过 {skippedCount} 笔");

            // 计算绩效指标
            CalculateDailyPickPerformanceMetrics(result);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "历史选股记录回测失败");
            progress?.Report($"回测失败: {ex.Message}");
        }

        return result;
    }

    private void CalculateDailyPickPerformanceMetrics(BacktestResult result)
    {
        if (result.Trades.Count == 0)
        {
            result.TotalReturn = 0;
            result.WinRate = 0;
            result.TradeCount = 0;
            result.WinCount = 0;
            result.LossCount = 0;
            return;
        }

        result.TradeCount = result.Trades.Count;
        result.WinCount = result.Trades.Count(t => t.ProfitLoss > 0);
        result.LossCount = result.Trades.Count(t => t.ProfitLoss < 0);
        result.WinRate = (decimal)result.WinCount / result.TradeCount * 100;

        var profits = result.Trades.Where(t => t.ProfitLoss > 0).Select(t => t.ProfitLoss).ToList();
        var losses = result.Trades.Where(t => t.ProfitLoss < 0).Select(t => Math.Abs(t.ProfitLoss)).ToList();

        result.AverageProfit = profits.Count > 0 ? profits.Average() : 0;
        result.AverageLoss = losses.Count > 0 ? losses.Average() : 0;
        result.MaxProfit = profits.Count > 0 ? profits.Max() : 0;
        result.MaxLoss = losses.Count > 0 ? losses.Max() : 0;

        // 计算总盈亏和总收益率
        var totalProfitLoss = result.Trades.Sum(t => t.ProfitLoss);
        var totalInvested = result.Trades.Sum(t => t.BuyPrice * t.Shares);
        result.TotalReturn = totalInvested > 0 ? totalProfitLoss / totalInvested * 100 : 0;
        result.FinalEquity = totalProfitLoss;

        // 计算盈亏比
        if (result.AverageLoss > 0)
            result.ProfitFactor = result.AverageProfit / result.AverageLoss;

        // 计算年化收益率（简化计算）
        var days = (result.EndDate - result.StartDate).Days;
        if (days > 0)
        {
            var years = days / 365m;
            result.AnnualReturn = (decimal)Math.Pow(
                (double)((totalInvested + totalProfitLoss) / totalInvested),
                1.0 / (double)years) - 1;
            result.AnnualReturn *= 100;
        }
    }

    private async Task<Strategies.Signal?> CombineStrategiesAsync(
        List<Strategies.IStrategy> strategies,
        Entities.StockInfo stock,
        List<StockDailyData> dailyData,
        List<StockDailyIndicator> indicators,
        DateTime tradeDate)
    {
        // AND逻辑：只有当所有策略都发出买入信号时，才返回买入信号
        foreach (var strategy in strategies)
        {
            var signals = strategy.GenerateSignals(
                stock.StockID,
                dailyData.Where(d => d.TradeDate <= tradeDate).ToList(),
                indicators.Where(i => i.TradeDate.Date == tradeDate.Date).ToList());

            var buySignal = signals.FirstOrDefault(s => s.Type == Strategies.SignalType.Buy);

            // 如果某个策略没有买入信号，则组合策略也不买入
            if (buySignal == null)
            {
                return null;
            }
        }

        // 所有策略都发出了买入信号，返回买入信号
        var stockData = dailyData.FirstOrDefault(d => d.TradeDate == tradeDate);
        if (stockData == null) return null;

        return new Strategies.Signal
        {
            StockId = stock.StockID,
            Type = Strategies.SignalType.Buy,
            Date = tradeDate
        };
    }

    private async Task CheckSellSignalsForStrategyBacktestAsync(
        DateTime tradeDate,
        List<TradeRecord> trades,
        List<Strategies.IStrategy> strategyInstances,
        DailyPickBacktestSettings settings,
        ILookup<string, StockDailyData> dailyDataByStock,
        Dictionary<string, List<StockDailyIndicator>> indicatorsByStock,
        decimal stopLossPercent)
    {
        // 获取未平仓的交易
        var openTrades = trades.Where(t => !t.SellDate.HasValue).ToList();

        foreach (var trade in openTrades)
        {
            if (!dailyDataByStock.Contains(trade.StockId)) continue;

            var stockDailyData = dailyDataByStock[trade.StockId]
                .Where(d => d.TradeDate == tradeDate)
                .FirstOrDefault();

            if (stockDailyData == null) continue;

            if (!indicatorsByStock.ContainsKey(trade.StockId)) continue;

            var buyPrice = trade.BuyPrice;
            var currentPrice = stockDailyData.ClosePrice;
            var profitLossPercent = (currentPrice - buyPrice) / buyPrice * 100;

            // 检查止损
            if (profitLossPercent <= stopLossPercent)
            {
                trade.SellDate = tradeDate;
                trade.SellPrice = currentPrice * (1 - settings.Slippage);
                trade.SellReason = $"止损({stopLossPercent:F1}%)";

                var buyAmount = buyPrice * trade.Shares;
                var sellAmount = trade.SellPrice.Value * trade.Shares;
                trade.Commission = (buyAmount + sellAmount) * settings.Commission;
                trade.ProfitLoss = sellAmount - buyAmount - trade.Commission;
                trade.ProfitLossPercent = trade.ProfitLoss / buyAmount * 100;
                trade.HoldingDays = (trade.SellDate.Value - trade.BuyDate).Days;
                continue;
            }

            // 检查策略卖出信号
            var stockIndicators = indicatorsByStock[trade.StockId]
                .Where(i => i.TradeDate.Date == tradeDate.Date)
                .ToList();

            var todayIndicators = stockIndicators;

            if (todayIndicators.Count > 0)
            {
                var allDailyData = dailyDataByStock[trade.StockId]
                    .Where(d => d.TradeDate <= tradeDate)
                    .OrderBy(d => d.TradeDate)
                    .ToList();

                // 检查所有策略的卖出信号
                foreach (var strategy in strategyInstances)
                {
                    var signals = strategy.GenerateSignals(trade.StockId, allDailyData, todayIndicators);

                    var sellSignal = signals.FirstOrDefault(s =>
                        s.Type == Strategies.SignalType.Sell &&
                        s.Date.Date == tradeDate.Date);

                    if (sellSignal != null)
                    {
                        trade.SellDate = tradeDate;
                        trade.SellPrice = stockDailyData.OpenPrice * (1 - settings.Slippage);
                        trade.SellReason = $"策略卖出({strategy.Name})";

                        var buyAmount = buyPrice * trade.Shares;
                        var sellAmount = trade.SellPrice.Value * trade.Shares;
                        trade.Commission = (buyAmount + sellAmount) * settings.Commission;
                        trade.ProfitLoss = sellAmount - buyAmount - trade.Commission;
                        trade.ProfitLossPercent = trade.ProfitLoss / buyAmount * 100;
                        trade.HoldingDays = (trade.SellDate.Value - trade.BuyDate).Days;
                        break;
                    }
                }
            }
        }

        // 检查是否到了回测结束日期
        if (tradeDate == settings.EndDate.Date)
        {
            foreach (var trade in trades.Where(t => !t.SellDate.HasValue))
            {
                var lastData = dailyDataByStock[trade.StockId]
                    .Where(d => d.TradeDate == tradeDate)
                    .FirstOrDefault();

                if (lastData != null)
                {
                    trade.SellDate = tradeDate;
                    trade.SellPrice = lastData.ClosePrice * (1 - settings.Slippage);
                    trade.SellReason = "回测结束";

                    var buyAmount = trade.BuyPrice * trade.Shares;
                    var sellAmount = trade.SellPrice.Value * trade.Shares;
                    trade.Commission = (buyAmount + sellAmount) * settings.Commission;
                    trade.ProfitLoss = sellAmount - buyAmount - trade.Commission;
                    trade.ProfitLossPercent = trade.ProfitLoss / buyAmount * 100;
                    trade.HoldingDays = (trade.SellDate.Value - trade.BuyDate).Days;
                }
            }
        }
    }
}
