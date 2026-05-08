using System.Collections.Concurrent;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Indicators;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.Strategies;

namespace StockAnalysisSystem.Core.Backtest.V2;

public sealed class StrategyBacktestSettingsV2
{
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }

    public decimal InitialCapital { get; init; } = 1_000_000m;
    public decimal CommissionRate { get; init; } = 0.00025m;
    public decimal SlippageRate { get; init; } = 0.001m;

    public int MaxPositions { get; init; } = 10;
    public decimal StopLossPercent { get; init; } = -5m;

    /// <summary>当不为空时，仅回测指定股票。</summary>
    public List<string>? StockIds { get; init; }

    /// <summary>当大于 0 时，仅回测前 N 只股票（用于快速试跑/调参）。</summary>
    public int MaxStockCount { get; init; } = 0;
}

public sealed class StrategyBacktesterV2
{
    private readonly BacktestEngineV2 _engine;
    private readonly IStockRepository _stockRepo;
    private readonly IStockDailyDataRepository _dailyRepo;
    private readonly IIndicatorRepository _indicatorRepo;

    public StrategyBacktesterV2(
        BacktestEngineV2 engine,
        IStockRepository stockRepo,
        IStockDailyDataRepository dailyRepo,
        IIndicatorRepository indicatorRepo)
    {
        _engine = engine;
        _stockRepo = stockRepo;
        _dailyRepo = dailyRepo;
        _indicatorRepo = indicatorRepo;
    }

    public async Task<BacktestResultV2> RunAsync(
        IStrategy strategy,
        StrategyBacktestSettingsV2 settings,
        CancellationToken cancellationToken = default)
    {
        // 对齐到交易日（避免 EndDate 非交易日导致到期清算不触发）
        var start = settings.StartDate.Date;
        var end = settings.EndDate.Date;
        var calendar = await _dailyRepo.GetTradeDatesAsync(start, end);
        if (calendar.Count > 0)
        {
            var lastTradeDate = calendar.Max().Date;
            if (lastTradeDate < end)
                end = lastTradeDate;
        }

        var config = new BacktestConfigV2
        {
            StartDate = start,
            EndDate = end,
            InitialCapital = settings.InitialCapital,
            CommissionRate = settings.CommissionRate,
            SlippageRate = settings.SlippageRate,
            PriceBasis = PriceBasis.Close,
            RoundLot = 100
        };

        var exec = new DefaultExecutionModelV2();
        var portfolioModel = new EqualWeightSlotsPortfolioModelV2(exec, settings.MaxPositions);

        // 预加载数据（复用 legacy 思路，避免并发 EF 问题）
        var stocks = await _stockRepo.GetAllAsync();
        if (settings.StockIds != null && settings.StockIds.Count > 0)
        {
            var set = new HashSet<string>(settings.StockIds, StringComparer.Ordinal);
            stocks = stocks.Where(s => set.Contains(s.Id)).ToList();
        }
        if (settings.MaxStockCount > 0)
        {
            stocks = stocks.Take(settings.MaxStockCount).ToList();
        }

        var stockIds = stocks.Select(s => s.Id).ToList();
        var allData = stockIds.Count == 0
            ? new List<StockDailyData>()
            : await _dailyRepo.GetByStockIdsAndDateRangeAsync(stockIds, config.StartDate.AddDays(-220), config.EndDate);
        var allInd = stockIds.Count == 0
            ? new List<StockDailyIndicator>()
            : await _indicatorRepo.GetByStockIdsAndDateRangeAsync(stockIds, config.StartDate.AddDays(-220), config.EndDate);

        var dailyByStock = allData.ToLookup(x => x.StockID);
        var indByStock = allInd.ToLookup(x => x.StockId);

        var signalsByStock = new ConcurrentDictionary<string, List<Signal>>(StringComparer.Ordinal);
        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken };

        await Parallel.ForEachAsync(stocks, options, async (s, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            var daily = dailyByStock[s.Id].OrderBy(x => x.TradeDate.Date).ThenBy(x => x.ID).ToList();
            if (daily.Count == 0)
                return;
            var inds = indByStock[s.Id].OrderBy(x => x.TradeDate.Date).ToList();
            if (inds.Count == 0)
                inds = IndicatorCalculator.CalculateAll(s.Id, daily);
            var sigs = strategy.GenerateSignals(s.Id, daily, inds);
            signalsByStock[s.Id] = sigs;
            await Task.CompletedTask;
        });

        var signalSource = new PrecomputedSignalSourceV2(
            strategy.Name,
            signalsByStock,
            settings.StopLossPercent,
            end);

        return await _engine.RunAsync(config, signalSource, portfolioModel, cancellationToken);
    }
}

public sealed class PrecomputedSignalSourceV2 : ISignalSourceV2
{
    private readonly string _strategyName;
    private readonly ConcurrentDictionary<string, List<Signal>> _signalsByStock;
    private readonly decimal _stopLossPercent;
    private readonly DateTime? _endDate;

    public PrecomputedSignalSourceV2(
        string strategyName,
        ConcurrentDictionary<string, List<Signal>> signalsByStock,
        decimal stopLossPercent,
        DateTime? endDate = null)
    {
        _strategyName = strategyName;
        _signalsByStock = signalsByStock;
        _stopLossPercent = stopLossPercent;
        _endDate = endDate?.Date;
    }

    public Task<IReadOnlyList<PlannedOrderV2>> GetPlannedOrdersAsync(
        DateTime tradeDate,
        PortfolioStateV2 portfolio,
        CancellationToken cancellationToken = default)
    {
        tradeDate = tradeDate.Date;
        cancellationToken.ThrowIfCancellationRequested();

        var planned = new List<PlannedOrderV2>();

        // 0) 到期清算：最后一天全部卖出（避免持仓跨出区间导致 Trades=0、指标失真）
        if (_endDate.HasValue && tradeDate >= _endDate.Value && portfolio.Positions.Count > 0)
        {
            planned.AddRange(portfolio.Positions.Values.Select(p => new PlannedOrderV2
            {
                Side = OrderSideV2.Sell,
                StockId = p.StockId,
                StrategyName = _strategyName,
                Reason = "回测区间结束清算"
            }));
            return Task.FromResult((IReadOnlyList<PlannedOrderV2>)planned);
        }

        // 1) 先处理卖出（策略卖出信号/止损由 PortfolioModel 在成交价上完成）
        foreach (var pos in portfolio.Positions.Values)
        {
            if (_signalsByStock.TryGetValue(pos.StockId, out var sigs))
            {
                var sell = sigs.FirstOrDefault(s => s.Type == SignalType.Sell && s.Date.Date == tradeDate.Date);
                if (sell != null)
                {
                    planned.Add(new PlannedOrderV2
                    {
                        Side = OrderSideV2.Sell,
                        StockId = pos.StockId,
                        StrategyName = _strategyName,
                        Reason = sell.Reason
                    });
                }
            }
        }

        // 2) 买入：当日 Buy 信号按 Strength 取前 N（剩余 slots 由 PortfolioModel 自行限制）
        var buyCandidates = new List<(string stockId, decimal strength, string reason)>();
        foreach (var kv in _signalsByStock)
        {
            var buy = kv.Value.FirstOrDefault(s => s.Type == SignalType.Buy && s.Date.Date == tradeDate.Date);
            if (buy == null)
                continue;
            if (portfolio.Positions.ContainsKey(kv.Key))
                continue;
            buyCandidates.Add((kv.Key, buy.Strength, buy.Reason));
        }

        foreach (var c in buyCandidates.OrderByDescending(x => x.strength))
        {
            planned.Add(new PlannedOrderV2
            {
                Side = OrderSideV2.Buy,
                StockId = c.stockId,
                StrategyName = _strategyName,
                Reason = c.reason
            });
        }

        return Task.FromResult((IReadOnlyList<PlannedOrderV2>)planned);
    }
}

