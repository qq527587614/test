using StockAnalysisSystem.Core.DailyPick;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Repositories;

namespace StockAnalysisSystem.Core.Backtest.V2;

public sealed class FirstBoardPullbackPortfolioBacktesterV2
{
    private readonly BacktestEngineV2 _engine;
    private readonly IStockRepository _stockRepo;
    private readonly IStockDailyDataRepository _dailyRepo;
    private readonly IStrategyRepository _strategyRepo;
    private readonly DailyPicker _dailyPicker;

    public FirstBoardPullbackPortfolioBacktesterV2(
        BacktestEngineV2 engine,
        IStockRepository stockRepo,
        IStockDailyDataRepository dailyRepo,
        IStrategyRepository strategyRepo,
        DailyPicker dailyPicker)
    {
        _engine = engine;
        _stockRepo = stockRepo;
        _dailyRepo = dailyRepo;
        _strategyRepo = strategyRepo;
        _dailyPicker = dailyPicker;
    }

    public async Task<BacktestResultV2> RunAsync(
        StockAnalysisSystem.Core.Backtest.FirstBoardPullbackPortfolioSettings settings,
        CancellationToken cancellationToken = default)
    {
        var maxSlots = Math.Clamp(settings.MaxSlots, 1, 50);

        var config = new BacktestConfigV2
        {
            StartDate = settings.StartDate.Date,
            EndDate = settings.EndDate.Date,
            InitialCapital = settings.InitialCapital,
            CommissionRate = settings.Commission,
            SlippageRate = settings.Slippage,
            PriceBasis = PriceBasis.CurrentPrice,
            RoundLot = 100
        };

        var exec = new DefaultExecutionModelV2();
        var portfolioModel = new EqualWeightSlotsPortfolioModelV2(exec, maxSlots);

        // 交易日历（用于 sessionsAfterEntry 计算）
        var tradeDates = await _dailyRepo.GetTradeDatesAsync(config.StartDate, config.EndDate);
        var dateToIndex = tradeDates
            .Select((d, i) => (d: d.Date, i))
            .ToDictionary(x => x.d, x => x.i);

        var strategyMetaList = await _strategyRepo.GetByIdsAsync(
            settings.CombineStrategyIds.Where(id => id > 0).Distinct().ToList());
        var metaById = strategyMetaList.ToDictionary(s => s.Id);
        var combineLabel = string.Join("+", settings.CombineStrategyIds.Where(id => metaById.ContainsKey(id)).Select(id => metaById[id].Name)) + "(十仓组合)";

        var signalSource = new FirstBoardPullbackPortfolioSignalSourceV2(
            _dailyRepo,
            _dailyPicker,
            settings,
            dateToIndex,
            combineLabel);

        return await _engine.RunAsync(config, signalSource, portfolioModel, cancellationToken);
    }
}

public sealed class FirstBoardPullbackPortfolioSignalSourceV2 : ISignalSourceV2
{
    private readonly IStockDailyDataRepository _dailyRepo;
    private readonly DailyPicker _dailyPicker;
    private readonly StockAnalysisSystem.Core.Backtest.FirstBoardPullbackPortfolioSettings _settings;
    private readonly Dictionary<DateTime, int> _dateToIndex;
    private readonly string _strategyLabel;

    // 日线缓存（仅区间内需要）
    private Dictionary<DateTime, List<StockDailyData>> _barsByDate = new();

    public FirstBoardPullbackPortfolioSignalSourceV2(
        IStockDailyDataRepository dailyRepo,
        DailyPicker dailyPicker,
        StockAnalysisSystem.Core.Backtest.FirstBoardPullbackPortfolioSettings settings,
        Dictionary<DateTime, int> dateToIndex,
        string strategyLabel)
    {
        _dailyRepo = dailyRepo;
        _dailyPicker = dailyPicker;
        _settings = settings;
        _dateToIndex = dateToIndex;
        _strategyLabel = strategyLabel;
    }

    public async Task<IReadOnlyList<PlannedOrderV2>> GetPlannedOrdersAsync(
        DateTime tradeDate,
        PortfolioStateV2 portfolio,
        CancellationToken cancellationToken = default)
    {
        tradeDate = tradeDate.Date;
        cancellationToken.ThrowIfCancellationRequested();

        // 预加载当日 bars（便于市场过滤/卖出判定）
        await EnsureBarsLoadedAsync(tradeDate);

        var planned = new List<PlannedOrderV2>();

        // 1) 平仓：建仓当日不评估；自次一交易日起按收盘规则处理
        foreach (var pos in portfolio.Positions.Values.ToList())
        {
            if (pos.BuyDate.Date >= tradeDate.Date)
                continue;

            if (!_dateToIndex.TryGetValue(pos.BuyDate.Date, out var entryIdx))
                continue;
            if (!_dateToIndex.TryGetValue(tradeDate.Date, out var curIdx))
                continue;

            var sessionsAfterEntry = curIdx - entryIdx;
            if (sessionsAfterEntry <= 0)
                continue;

            var bar = GetBar(pos.StockId, tradeDate);
            if (bar == null)
                continue;

            var rawClose = bar.CurrentPrice;
            if (!rawClose.HasValue || rawClose.Value <= 0)
                continue;

            var buyPx = pos.CostPrice;
            var takeProfitMinRatio = Math.Max(0m, _settings.TakeProfitMinPercent) / 100m;
            var takeProfit = rawClose.Value >= buyPx * (1m + takeProfitMinRatio);
            var forceTime = sessionsAfterEntry >= Math.Max(1, _settings.MaxHoldingSessionsAfterEntry);
            var isUpDay = bar.ChangePercent.HasValue && bar.ChangePercent.Value > 0;

            if (takeProfit || forceTime || isUpDay)
            {
                var reason = takeProfit
                    ? "盈利止盈(CurrentPrice)"
                    : forceTime
                        ? $"满{Math.Max(1, _settings.MaxHoldingSessionsAfterEntry)}个交易日未盈利强平(CurrentPrice)"
                        : "当日上涨平仓(涨跌幅>0)";

                planned.Add(new PlannedOrderV2
                {
                    Side = OrderSideV2.Sell,
                    StockId = pos.StockId,
                    StrategyName = pos.StrategyName,
                    Reason = reason
                });
            }
        }

        // 2) 开仓：空闲仓位 = 总份数 - 当前未平
        var maxSlots = Math.Clamp(_settings.MaxSlots, 1, 50);
        var freeSlots = maxSlots - portfolio.Positions.Count;
        if (freeSlots <= 0 || portfolio.Cash <= 0)
            return planned;

        if (_settings.EnableMarketFilter)
        {
            var bars = _barsByDate.TryGetValue(tradeDate, out var list) ? list : new List<StockDailyData>();
            var marketBars = bars.Where(d => d.ChangePercent.HasValue).ToList();
            if (marketBars.Count > 0)
            {
                var upRatio = (decimal)marketBars.Count(d => d.ChangePercent!.Value >= 0m) / marketBars.Count;
                var minUp = Math.Clamp(_settings.MinMarketUpRatio, 0m, 1m);
                if (upRatio < minUp)
                    return planned; // 当日不新开仓
            }
        }

        // 与 DailyPickForm 组合选股一致：各策略单独 PickAsync，再取交集
        var orderedDistinctIds = _settings.CombineStrategyIds.Where(id => id > 0).Distinct().ToList();
        if (orderedDistinctIds.Count == 0)
            return planned;

        var strategyResults = new Dictionary<int, List<DailyPickResult>>();
        foreach (var sid in orderedDistinctIds)
        {
            var one = await _dailyPicker.PickAsync(tradeDate, new List<int> { sid }, useDeepSeek: false, progress: null);
            strategyResults[sid] = one;
        }

        var picks = DailyPicker.CombinePickResultsIntersection(orderedDistinctIds, strategyResults);
        var held = new HashSet<string>(portfolio.Positions.Keys);
        var buyCandidates = picks
            .OrderByDescending(p => p.FinalScore)
            .Take(Math.Max(1, _settings.MaxPicksPerDay))
            .Where(p => !held.Contains(p.StockId))
            .Take(freeSlots)
            .ToList();

        foreach (var p in buyCandidates)
        {
            planned.Add(new PlannedOrderV2
            {
                Side = OrderSideV2.Buy,
                StockId = p.StockId,
                StrategyName = _strategyLabel,
                Reason = $"组合选股买入(Score:{p.FinalScore:F2})"
            });
        }

        return planned;
    }

    private async Task EnsureBarsLoadedAsync(DateTime tradeDate)
    {
        if (_barsByDate.ContainsKey(tradeDate.Date))
            return;

        // 为简化：按日期批量取当日全市场 bars（与 legacy 逻辑一致的市场过滤统计）
        var all = await _dailyRepo.GetByDateRangeAsync(tradeDate.Date, tradeDate.Date);
        _barsByDate[tradeDate.Date] = all;
    }

    private StockDailyData? GetBar(string stockId, DateTime tradeDate)
    {
        if (!_barsByDate.TryGetValue(tradeDate.Date, out var list))
            return null;
        return list.FirstOrDefault(x => x.StockID == stockId && x.TradeDate.Date == tradeDate.Date);
    }
}

