namespace StockAnalysisSystem.Core.Backtest.V2;

using StockAnalysisSystem.Core.Entities;

public interface ISignalSourceV2
{
    /// <summary>
    /// 返回当日希望执行的动作集合（买/卖），引擎会交由 Portfolio/Execution 决定是否执行。
    /// </summary>
    Task<IReadOnlyList<PlannedOrderV2>> GetPlannedOrdersAsync(
        DateTime tradeDate,
        PortfolioStateV2 portfolio,
        CancellationToken cancellationToken = default);
}

public interface IExecutionModelV2
{
    decimal? GetBuyFillPrice(StockDailyData bar, BacktestConfigV2 config);
    decimal? GetSellFillPrice(StockDailyData bar, BacktestConfigV2 config);
}

public interface IPortfolioModelV2
{
    /// <summary>尝试执行订单（会更新 cash/positions/trades）。</summary>
    void ApplyOrders(
        DateTime tradeDate,
        IReadOnlyList<PlannedOrderV2> plannedOrders,
        BacktestConfigV2 config,
        MarketSnapshotV2 market,
        PortfolioStateV2 portfolio,
        BacktestResultV2 result);
}

public enum OrderSideV2
{
    Buy,
    Sell
}

public sealed class PlannedOrderV2
{
    public required OrderSideV2 Side { get; init; }
    public required string StockId { get; init; }
    public string StrategyName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class PositionV2
{
    public required string StockId { get; init; }
    public required string StockCode { get; init; }
    public required string StockName { get; init; }

    public required DateTime BuyDate { get; set; }
    public required decimal CostPrice { get; set; }
    public required int Shares { get; set; }
    public string StrategyName { get; set; } = string.Empty;
}

public sealed class PortfolioStateV2
{
    public decimal Cash { get; set; }
    public Dictionary<string, PositionV2> Positions { get; } = new(StringComparer.Ordinal);
}

public sealed class MarketSnapshotV2
{
    public required DateTime TradeDate { get; init; }

    /// <summary>当日每只股票的日线 bar。</summary>
    public required Dictionary<string, StockDailyData> BarsByStockId { get; init; }

    /// <summary>当日股票基本信息（code/name）。</summary>
    public required Dictionary<string, StockInfo> StockById { get; init; }
}

