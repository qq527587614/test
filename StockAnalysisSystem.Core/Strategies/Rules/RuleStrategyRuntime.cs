using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Indicators;
using StockAnalysisSystem.Core.Repositories;

namespace StockAnalysisSystem.Core.Strategies.Rules;

/// <summary>
/// 规则树策略运行时：通过 <see cref="IIndicatorProvider"/> 获取指标，再按日评估 Entry/Exit 规则产出信号。
/// </summary>
public sealed class RuleStrategyRuntime
{
    private readonly IStockDailyDataRepository _dailyRepo;
    private readonly IIndicatorProvider _indicatorProvider;

    public RuleStrategyRuntime(
        IStockDailyDataRepository dailyRepo,
        IIndicatorProvider indicatorProvider)
    {
        _dailyRepo = dailyRepo;
        _indicatorProvider = indicatorProvider;
    }

    public async Task<List<Signal>> GenerateSignalsAsync(
        string stockId,
        DateTime startDate,
        DateTime endDate,
        StrategyDefinition definition,
        IndicatorProviderOptions? indicatorOptions = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dailyData = await _dailyRepo.GetByStockIdAsync(
            stockId,
            startDate.Date.AddDays(-(indicatorOptions?.LookbackDays ?? 220)),
            endDate.Date);

        if (dailyData.Count == 0)
            return new List<Signal>();

        var orderedBars = dailyData
            .OrderBy(d => d.TradeDate.Date)
            .ThenBy(d => d.ID)
            .ToList();

        var indicators = await _indicatorProvider.GetOrComputeFromDailyDataAsync(
            stockId,
            orderedBars,
            startDate.Date,
            endDate.Date,
            indicatorOptions,
            cancellationToken);

        var indicatorByDate = indicators.ToDictionary(i => i.TradeDate.Date);

        var signals = new List<Signal>();
        for (var i = 0; i < orderedBars.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bar = orderedBars[i];
            var d = bar.TradeDate.Date;
            if (d < startDate.Date || d > endDate.Date)
                continue;

            indicatorByDate.TryGetValue(d, out var ind);
            StockDailyIndicator? prevInd = null;
            if (i > 0)
                indicatorByDate.TryGetValue(orderedBars[i - 1].TradeDate.Date, out prevInd);

            var ctx = new RuleContext
            {
                StockId = stockId,
                TradeDate = d,
                Bars = orderedBars,
                BarIndex = i,
                Bar = bar,
                PrevBar = i > 0 ? orderedBars[i - 1] : null,
                Indicator = ind,
                PrevIndicator = prevInd
            };

            if (definition.EntryRule != null && definition.EntryRule.Evaluate(ctx))
            {
                signals.Add(new Signal
                {
                    Date = d,
                    StockId = stockId,
                    Type = SignalType.Buy,
                    Strength = 1m,
                    Reason = $"RuleEntry:{definition.Name}"
                });
            }

            if (definition.ExitRule != null && definition.ExitRule.Evaluate(ctx))
            {
                signals.Add(new Signal
                {
                    Date = d,
                    StockId = stockId,
                    Type = SignalType.Sell,
                    Strength = 1m,
                    Reason = $"RuleExit:{definition.Name}"
                });
            }
        }

        return signals;
    }
}

