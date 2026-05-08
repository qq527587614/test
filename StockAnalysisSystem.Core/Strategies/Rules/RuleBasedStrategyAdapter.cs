using StockAnalysisSystem.Core.Entities;

namespace StockAnalysisSystem.Core.Strategies.Rules;

/// <summary>
/// 将 <see cref="StrategyDefinition"/> 适配为 legacy <see cref="IStrategy"/>，以复用现有回测入口（StrategyBacktesterV2）。
/// </summary>
public sealed class RuleBasedStrategyAdapter : IStrategy
{
    private readonly StrategyDefinition _definition;

    public RuleBasedStrategyAdapter(StrategyDefinition definition)
    {
        _definition = definition;
        Parameters = definition.Parameters ?? new Dictionary<string, object>();
    }

    public string Name => _definition.Name;
    public string StrategyType => "RuleDefinition";

    public Dictionary<string, object> Parameters { get; set; }

    public List<Signal> GenerateSignals(string stockId, List<StockDailyData> dailyData, List<StockDailyIndicator> indicators)
    {
        var entry = _definition.EntryRule;
        var exit = _definition.ExitRule;
        if (entry == null && exit == null)
            return new List<Signal>();

        if (dailyData.Count == 0)
            return new List<Signal>();

        var orderedBars = dailyData
            .OrderBy(d => d.TradeDate.Date)
            .ThenBy(d => d.ID)
            .ToList();

        var indByDate = indicators
            .GroupBy(i => i.TradeDate.Date)
            .ToDictionary(g => g.Key, g => g.Last());

        var signals = new List<Signal>();
        for (var i = 0; i < orderedBars.Count; i++)
        {
            var bar = orderedBars[i];
            var d = bar.TradeDate.Date;

            indByDate.TryGetValue(d, out var ind);
            StockDailyIndicator? prevInd = null;
            if (i > 0)
                indByDate.TryGetValue(orderedBars[i - 1].TradeDate.Date, out prevInd);

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

            if (entry != null && entry.Evaluate(ctx))
            {
                signals.Add(new Signal
                {
                    Date = d,
                    StockId = stockId,
                    Type = SignalType.Buy,
                    Strength = 1m,
                    Reason = $"RuleEntry:{Name}"
                });
            }

            if (exit != null && exit.Evaluate(ctx))
            {
                signals.Add(new Signal
                {
                    Date = d,
                    StockId = stockId,
                    Type = SignalType.Sell,
                    Strength = 1m,
                    Reason = $"RuleExit:{Name}"
                });
            }
        }

        return signals;
    }
}

