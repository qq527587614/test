using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Repositories;

namespace StockAnalysisSystem.Core.Backtest.V2;

public sealed class BacktestEngineV2
{
    private readonly IStockRepository _stockRepo;
    private readonly IStockDailyDataRepository _dailyRepo;
    private readonly AnalyticsServiceV2 _analytics;

    public BacktestEngineV2(
        IStockRepository stockRepo,
        IStockDailyDataRepository dailyRepo,
        AnalyticsServiceV2 analytics)
    {
        _stockRepo = stockRepo;
        _dailyRepo = dailyRepo;
        _analytics = analytics;
    }

    public async Task<BacktestResultV2> RunAsync(
        BacktestConfigV2 config,
        ISignalSourceV2 signalSource,
        IPortfolioModelV2 portfolioModel,
        CancellationToken cancellationToken = default)
    {
        var result = new BacktestResultV2
        {
            StartDate = config.StartDate.Date,
            EndDate = config.EndDate.Date,
            InitialCapital = config.InitialCapital,
            FinalEquity = config.InitialCapital
        };

        var stocks = await _stockRepo.GetAllAsync();
        var stockById = stocks.ToDictionary(s => s.Id, s => s);

        // 交易日历来自日线表（现有实现约定）
        var tradeDates = await _dailyRepo.GetTradeDatesAsync(config.StartDate.Date, config.EndDate.Date);
        if (tradeDates.Count == 0)
            return result;

        // 为了不在循环中频繁打 DB，这里批量拉取区间内全部日线，按股票+日期索引
        var allBars = await _dailyRepo.GetByDateRangeAsync(config.StartDate.Date, config.EndDate.Date);
        var barsByDate = allBars
            .GroupBy(b => b.TradeDate.Date)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.StockID, x => x));

        var portfolio = new PortfolioStateV2 { Cash = config.InitialCapital };

        decimal peak = config.InitialCapital;
        decimal? prevEquity = null;

        foreach (var d in tradeDates.Select(x => x.Date).OrderBy(x => x))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!barsByDate.TryGetValue(d, out var barsToday))
                barsToday = new Dictionary<string, StockDailyData>();

            var market = new MarketSnapshotV2
            {
                TradeDate = d,
                BarsByStockId = barsToday,
                StockById = stockById
            };

            // 1) 获取当日计划单
            var planned = await signalSource.GetPlannedOrdersAsync(d, portfolio, cancellationToken);

            // 2) 执行订单（更新组合与交易记录）
            portfolioModel.ApplyOrders(d, planned, config, market, portfolio, result);

            // 3) 估值：现金 + 持仓市值
            decimal mv = 0;
            foreach (var pos in portfolio.Positions.Values)
            {
                if (!barsToday.TryGetValue(pos.StockId, out var bar))
                    continue;
                var px = DefaultExecutionModelV2.ResolveBasisPrice(bar, config.PriceBasis);
                if (px.HasValue && px.Value > 0)
                    mv += px.Value * pos.Shares;
            }

            var equity = portfolio.Cash + mv;
            result.FinalEquity = equity;
            result.EquityCurve.Add(new PortfolioPointV2
            {
                Date = d,
                Cash = portfolio.Cash,
                PositionValue = mv,
                Equity = equity
            });

            // 4) 日收益
            if (prevEquity.HasValue && prevEquity.Value > 0)
            {
                var r = (equity - prevEquity.Value) / prevEquity.Value;
                result.DailyReturns.Add(new ReturnPointV2 { Date = d, Return = r });
            }

            prevEquity = equity;

            // 5) 回撤（underwater）
            if (equity > peak)
                peak = equity;
            var dd = peak > 0 ? (peak - equity) / peak : 0m;
            result.DrawdownCurve.Add(new DrawdownPointV2 { Date = d, Drawdown = dd, PeakEquity = peak });
        }

        _analytics.Fill(result);
        return result;
    }
}

