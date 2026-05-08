namespace StockAnalysisSystem.Core.Backtest.V2;

using StockAnalysisSystem.Core.Backtest;

public static class BacktestResultV2Extensions
{
    /// <summary>
    /// 临时过渡：把旧 <see cref="BacktestResult"/> 的权益曲线映射到 V2 合同（用于逐步迁移与 UI 并行展示）。
    /// </summary>
    public static BacktestResultV2 ToV2FromLegacy(this BacktestResult legacy)
    {
        var v2 = new BacktestResultV2
        {
            StartDate = legacy.StartDate,
            EndDate = legacy.EndDate,
            InitialCapital = legacy.InitialCapital,
            FinalEquity = legacy.FinalEquity
        };

        foreach (var p in legacy.EquityCurve)
        {
            v2.EquityCurve.Add(new PortfolioPointV2
            {
                Date = p.Date.Date,
                Equity = p.Equity,
                Cash = p.Cash,
                PositionValue = p.PositionValue
            });
        }

        foreach (var t in legacy.Trades.Where(x => x.SellDate.HasValue && x.SellPrice.HasValue))
        {
            v2.Trades.Add(new TradeRecordV2
            {
                StockId = t.StockId,
                StockCode = t.StockCode,
                StockName = t.StockName,
                StrategyName = t.StrategyName,
                BuyDate = t.BuyDate.Date,
                BuyPrice = t.BuyPrice,
                Shares = t.Shares,
                SellDate = t.SellDate!.Value.Date,
                SellPrice = t.SellPrice!.Value,
                Commission = t.Commission,
                SellReason = t.SellReason,
                ProfitLoss = t.ProfitLoss,
                ProfitLossPercent = t.ProfitLossPercent,
                HoldingDays = t.HoldingDays
            });
        }

        return v2;
    }
}

