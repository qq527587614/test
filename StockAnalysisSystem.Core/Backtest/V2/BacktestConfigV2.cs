namespace StockAnalysisSystem.Core.Backtest.V2;

public sealed class BacktestConfigV2
{
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }

    public decimal InitialCapital { get; init; } = 1_000_000m;
    public decimal CommissionRate { get; init; } = 0.00025m;
    public decimal SlippageRate { get; init; } = 0.001m;

    /// <summary>用于组合估值/撮合的价格口径。</summary>
    public PriceBasis PriceBasis { get; init; } = PriceBasis.Close;

    /// <summary>每次下单的最小手数（A股常用 100 股）。</summary>
    public int RoundLot { get; init; } = 100;
}

public enum PriceBasis
{
    Open,
    Close,
    /// <summary>
    /// 使用行情表里的 CurrentPrice（若为空/<=0，调用方应自行决定是否跳过或回退）。
    /// </summary>
    CurrentPrice
}

