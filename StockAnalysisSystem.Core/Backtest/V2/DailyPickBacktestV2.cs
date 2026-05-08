using StockAnalysisSystem.Core.DailyPick;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Indicators;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.Strategies;

namespace StockAnalysisSystem.Core.Backtest.V2;

public sealed class DailyPickBacktestSettingsV2
{
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }

    public decimal InitialCapital { get; init; } = 1_000_000m;
    public decimal CommissionRate { get; init; } = 0.00025m;
    public decimal SlippageRate { get; init; } = 0.001m;

    public int SharesPerPick { get; init; } = 100;
    public decimal StopLossPercent { get; init; } = -5m;

    /// <summary>当不为空时：每天使用 DailyPicker + 勾选策略选股。</summary>
    public List<int>? StrategyIds { get; init; }
}

public sealed class DailyPickBacktesterV2
{
    private readonly BacktestEngineV2 _engine;
    private readonly DailyPicker _dailyPicker;
    private readonly IStrategyRepository _strategyRepo;
    private readonly IStockDailyDataRepository _dailyRepo;
    private readonly IIndicatorProvider _indicatorProvider;
    private readonly IStockRepository _stockRepo;

    public DailyPickBacktesterV2(
        BacktestEngineV2 engine,
        DailyPicker dailyPicker,
        IStrategyRepository strategyRepo,
        IStockDailyDataRepository dailyRepo,
        IIndicatorProvider indicatorProvider,
        IStockRepository stockRepo)
    {
        _engine = engine;
        _dailyPicker = dailyPicker;
        _strategyRepo = strategyRepo;
        _dailyRepo = dailyRepo;
        _indicatorProvider = indicatorProvider;
        _stockRepo = stockRepo;
    }

    public async Task<BacktestResultV2> RunAsync(
        DailyPickBacktestSettingsV2 settings,
        CancellationToken cancellationToken = default)
    {
        if (settings.StrategyIds == null || settings.StrategyIds.Count == 0)
            throw new InvalidOperationException("DailyPickBacktesterV2: StrategyIds 不能为空（历史选股记录模式将在后续迁移）。");

        var config = new BacktestConfigV2
        {
            StartDate = settings.StartDate.Date,
            EndDate = settings.EndDate.Date,
            InitialCapital = settings.InitialCapital,
            CommissionRate = settings.CommissionRate,
            SlippageRate = settings.SlippageRate,
            PriceBasis = PriceBasis.Close,
            RoundLot = 100
        };

        var exec = new DefaultExecutionModelV2();
        var portfolioModel = new FixedSharesSinglePositionPortfolioModelV2(exec, settings.SharesPerPick);
        var signalSource = new DailyPickSignalSourceV2(
            _dailyPicker,
            _strategyRepo,
            _dailyRepo,
            _indicatorProvider,
            _stockRepo,
            settings.StrategyIds,
            settings.StopLossPercent,
            config.EndDate);

        return await _engine.RunAsync(config, signalSource, portfolioModel, cancellationToken);
    }
}

/// <summary>固定股数、单仓位：更贴近“每日选股回测”的原始语义。</summary>
public sealed class FixedSharesSinglePositionPortfolioModelV2 : IPortfolioModelV2
{
    private readonly IExecutionModelV2 _execution;
    private readonly int _shares;

    public FixedSharesSinglePositionPortfolioModelV2(IExecutionModelV2 execution, int shares)
    {
        _execution = execution;
        _shares = Math.Max(1, shares);
    }

    public void ApplyOrders(
        DateTime tradeDate,
        IReadOnlyList<PlannedOrderV2> plannedOrders,
        BacktestConfigV2 config,
        MarketSnapshotV2 market,
        PortfolioStateV2 portfolio,
        BacktestResultV2 result)
    {
        // 单仓：先卖后买
        foreach (var o in plannedOrders.Where(x => x.Side == OrderSideV2.Sell))
        {
            if (!portfolio.Positions.TryGetValue(o.StockId, out var pos))
                continue;
            if (!market.BarsByStockId.TryGetValue(o.StockId, out var bar))
                continue;

            var fill = _execution.GetSellFillPrice(bar, config);
            if (!fill.HasValue)
                continue;

            var sellAmt = fill.Value * pos.Shares;
            var sellComm = sellAmt * config.CommissionRate;
            portfolio.Cash += sellAmt - sellComm;

            var buyAmt = pos.CostPrice * pos.Shares;
            var buyComm = buyAmt * config.CommissionRate;
            var totalComm = buyComm + sellComm;
            var pnl = sellAmt - buyAmt - totalComm;
            var pnlPct = buyAmt > 0 ? pnl / buyAmt : 0m;

            result.Trades.Add(new TradeRecordV2
            {
                StockId = pos.StockId,
                StockCode = pos.StockCode,
                StockName = pos.StockName,
                StrategyName = string.IsNullOrWhiteSpace(pos.StrategyName) ? o.StrategyName : pos.StrategyName,
                BuyDate = pos.BuyDate.Date,
                BuyPrice = pos.CostPrice,
                Shares = pos.Shares,
                SellDate = tradeDate.Date,
                SellPrice = fill.Value,
                Commission = totalComm,
                SellReason = o.Reason,
                ProfitLoss = pnl,
                ProfitLossPercent = pnlPct,
                HoldingDays = (tradeDate.Date - pos.BuyDate.Date).Days
            });

            portfolio.Positions.Remove(o.StockId);
        }

        // 如果仍有持仓，则不再买
        if (portfolio.Positions.Count > 0)
            return;

        var buy = plannedOrders.FirstOrDefault(x => x.Side == OrderSideV2.Buy);
        if (buy == null)
            return;
        if (!market.BarsByStockId.TryGetValue(buy.StockId, out var buyBar))
            return;
        var fillBuy = _execution.GetBuyFillPrice(buyBar, config);
        if (!fillBuy.HasValue || fillBuy.Value <= 0)
            return;
        if (!market.StockById.TryGetValue(buy.StockId, out var si))
            return;

        var buyAmt2 = fillBuy.Value * _shares;
        var buyComm2 = buyAmt2 * config.CommissionRate;
        if (buyAmt2 + buyComm2 > portfolio.Cash)
            return;

        portfolio.Cash -= buyAmt2 + buyComm2;
        portfolio.Positions[buy.StockId] = new PositionV2
        {
            StockId = buy.StockId,
            StockCode = si.StockCode,
            StockName = si.StockName,
            BuyDate = tradeDate.Date,
            CostPrice = fillBuy.Value,
            Shares = _shares,
            StrategyName = buy.StrategyName
        };
    }
}

public sealed class DailyPickSignalSourceV2 : ISignalSourceV2
{
    private readonly DailyPicker _dailyPicker;
    private readonly IStrategyRepository _strategyRepo;
    private readonly IStockDailyDataRepository _dailyRepo;
    private readonly IIndicatorProvider _indicatorProvider;
    private readonly IStockRepository _stockRepo;
    private readonly List<int> _strategyIds;
    private readonly decimal _stopLossPercent;
    private readonly DateTime _endDate;

    private List<IStrategy>? _strategyInstances;
    private Dictionary<string, List<StockDailyData>> _dailyCache = new(StringComparer.Ordinal);
    private Dictionary<string, List<StockDailyIndicator>> _indicatorCache = new(StringComparer.Ordinal);

    public DailyPickSignalSourceV2(
        DailyPicker dailyPicker,
        IStrategyRepository strategyRepo,
        IStockDailyDataRepository dailyRepo,
        IIndicatorProvider indicatorProvider,
        IStockRepository stockRepo,
        List<int> strategyIds,
        decimal stopLossPercent,
        DateTime endDate)
    {
        _dailyPicker = dailyPicker;
        _strategyRepo = strategyRepo;
        _dailyRepo = dailyRepo;
        _indicatorProvider = indicatorProvider;
        _stockRepo = stockRepo;
        _strategyIds = strategyIds;
        _stopLossPercent = stopLossPercent;
        _endDate = endDate.Date;
    }

    public async Task<IReadOnlyList<PlannedOrderV2>> GetPlannedOrdersAsync(
        DateTime tradeDate,
        PortfolioStateV2 portfolio,
        CancellationToken cancellationToken = default)
    {
        tradeDate = tradeDate.Date;

        // 初始化策略实例（一次）
        if (_strategyInstances == null)
        {
            var metas = await _strategyRepo.GetByIdsAsync(_strategyIds);
            _strategyInstances = metas
                .Select(m => StrategyFactory.CreateFromJson(m.StrategyType, m.Parameters))
                .Where(s => s != null)
                .Cast<IStrategy>()
                .ToList();
        }

        var planned = new List<PlannedOrderV2>();

        // 到期清算：最后一天全部卖出
        if (tradeDate >= _endDate && portfolio.Positions.Count > 0)
        {
            planned.AddRange(portfolio.Positions.Values.Select(p => new PlannedOrderV2
            {
                Side = OrderSideV2.Sell,
                StockId = p.StockId,
                StrategyName = p.StrategyName,
                Reason = "回测区间结束清算"
            }));
            return planned;
        }

        // 有持仓：检查止损/策略卖出
        foreach (var pos in portfolio.Positions.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bar = await GetBarAsync(pos.StockId, tradeDate);
            if (bar == null)
                continue;

            var currentPx = bar.ClosePrice;
            if (pos.CostPrice > 0)
            {
                var pnlPct = (currentPx - pos.CostPrice) / pos.CostPrice * 100m;
                if (pnlPct <= _stopLossPercent)
                {
                    planned.Add(new PlannedOrderV2
                    {
                        Side = OrderSideV2.Sell,
                        StockId = pos.StockId,
                        StrategyName = pos.StrategyName,
                        Reason = $"止损({pnlPct:F1}%)"
                    });
                    continue;
                }
            }

            // 策略卖出：只要任一策略在当日给出 Sell 信号即卖出
            if (_strategyInstances != null && _strategyInstances.Count > 0)
            {
                var daily = await GetDailySeriesAsync(pos.StockId, tradeDate);
                var indicators = await GetIndicatorSeriesAsync(pos.StockId, tradeDate);
                if (daily.Count > 0)
                {
                    foreach (var s in _strategyInstances)
                    {
                        var signals = s.GenerateSignals(
                            pos.StockId,
                            daily.Where(x => x.TradeDate.Date <= tradeDate).ToList(),
                            indicators);

                        var sell = signals.FirstOrDefault(x => x.Type == SignalType.Sell && x.Date.Date == tradeDate.Date);
                        if (sell != null)
                        {
                            planned.Add(new PlannedOrderV2
                            {
                                Side = OrderSideV2.Sell,
                                StockId = pos.StockId,
                                StrategyName = pos.StrategyName,
                                Reason = $"策略卖出({s.Name})"
                            });
                            break;
                        }
                    }
                }
            }
        }

        // 无持仓：选股买入
        if (portfolio.Positions.Count == 0)
        {
            var picks = await _dailyPicker.PickAsync(tradeDate, _strategyIds, useDeepSeek: false, progress: null);
            var top = picks.OrderByDescending(p => p.FinalScore).FirstOrDefault();
            if (top != null)
            {
                planned.Add(new PlannedOrderV2
                {
                    Side = OrderSideV2.Buy,
                    StockId = top.StockId,
                    StrategyName = string.Join(" + ", _strategyInstances?.Select(x => x.Name) ?? new[] { "DailyPick" }),
                    Reason = $"每日选股Top1(Score:{top.FinalScore:F2})"
                });
            }
        }

        return planned;
    }

    private async Task<StockDailyData?> GetBarAsync(string stockId, DateTime tradeDate)
    {
        var series = await GetDailySeriesAsync(stockId, tradeDate);
        return series.LastOrDefault(x => x.TradeDate.Date == tradeDate.Date);
    }

    private async Task<List<StockDailyData>> GetDailySeriesAsync(string stockId, DateTime tradeDate)
    {
        if (_dailyCache.TryGetValue(stockId, out var cached))
            return cached;

        var start = tradeDate.Date.AddDays(-240);
        var end = _endDate;
        var series = await _dailyRepo.GetByStockIdAsync(stockId, start, end);
        series = series.OrderBy(x => x.TradeDate.Date).ThenBy(x => x.ID).ToList();
        _dailyCache[stockId] = series;
        return series;
    }

    private async Task<List<StockDailyIndicator>> GetIndicatorSeriesAsync(string stockId, DateTime tradeDate)
    {
        if (_indicatorCache.TryGetValue(stockId, out var cached))
            return cached;

        var daily = await GetDailySeriesAsync(stockId, tradeDate);
        var inds = await _indicatorProvider.GetOrComputeFromDailyDataAsync(
            stockId,
            daily,
            daily.First().TradeDate.Date,
            _endDate,
            new IndicatorProviderOptions { LookbackDays = 220, PersistComputedIndicators = false });

        inds = inds.OrderBy(x => x.TradeDate.Date).ToList();
        _indicatorCache[stockId] = inds;
        return inds;
    }
}

