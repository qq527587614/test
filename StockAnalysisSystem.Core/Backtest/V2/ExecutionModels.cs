namespace StockAnalysisSystem.Core.Backtest.V2;

using StockAnalysisSystem.Core.Entities;

public sealed class DefaultExecutionModelV2 : IExecutionModelV2
{
    public decimal? GetBuyFillPrice(StockDailyData bar, BacktestConfigV2 config)
    {
        var px = ResolveBasisPrice(bar, config.PriceBasis);
        if (!px.HasValue || px.Value <= 0)
            return null;
        return px.Value * (1m + config.SlippageRate);
    }

    public decimal? GetSellFillPrice(StockDailyData bar, BacktestConfigV2 config)
    {
        var px = ResolveBasisPrice(bar, config.PriceBasis);
        if (!px.HasValue || px.Value <= 0)
            return null;
        return px.Value * (1m - config.SlippageRate);
    }

    internal static decimal? ResolveBasisPrice(StockDailyData bar, PriceBasis basis)
    {
        return basis switch
        {
            PriceBasis.Open => bar.OpenPrice,
            PriceBasis.Close => bar.ClosePrice,
            PriceBasis.CurrentPrice => bar.CurrentPrice,
            _ => bar.ClosePrice
        };
    }
}

