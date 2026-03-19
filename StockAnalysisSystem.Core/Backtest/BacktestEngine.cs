using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StockAnalysisSystem.Core.Entities;
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
    private readonly ILogger<BacktestEngine>? _logger;

    public BacktestEngine(
        IStockRepository stockRepo,
        IStockDailyDataRepository dailyDataRepo,
        IIndicatorRepository indicatorRepo,
        IBacktestTaskRepository backtestTaskRepo,
        ILogger<BacktestEngine>? logger = null)
    {
        _stockRepo = stockRepo;
        _dailyDataRepo = dailyDataRepo;
        _indicatorRepo = indicatorRepo;
        _backtestTaskRepo = backtestTaskRepo;
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

                // 先处理卖出信号
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

                // 再处理买入信号（限制最大持仓数）
                var buySignals = dailySignals
                    .Where(s => s.Type == SignalType.Buy && !portfolio.ContainsKey(s.StockId))
                    .OrderByDescending(s => s.Strength)
                    .Take(settings.MaxPositions - portfolio.Count)
                    .ToList();

                foreach (var signal in buySignals)
                {
                    var stock = stocks.First(s => s.Id == signal.StockId);
                    if (allDailyData.TryGetValue(signal.StockId, out var dailyData))
                    {
                        var todayData = dailyData.FirstOrDefault(d => d.TradeDate == date);
                        if (todayData != null)
                        {
                            var buyPrice = todayData.OpenPrice * (1 + settings.Slippage);
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
}
