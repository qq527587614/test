using StockAnalysisSystem.Core.Entities;

namespace StockAnalysisSystem.Core.Strategies.Rules;

public sealed class RuleContext
{
    public required string StockId { get; init; }
    public required DateTime TradeDate { get; init; }

    /// <summary>
    /// 当前评估日所在股票的有序日线序列（TradeDate 升序）。
    /// 用于需要回看历史窗口的规则节点（例如：首板后回落）。
    /// </summary>
    public required IReadOnlyList<StockDailyData> Bars { get; init; }

    /// <summary>
    /// 当前评估日对应 <see cref="Bars"/> 的下标。
    /// </summary>
    public required int BarIndex { get; init; }

    public required StockDailyData Bar { get; init; }
    public StockDailyData? PrevBar { get; init; }

    public StockDailyIndicator? Indicator { get; init; }
    public StockDailyIndicator? PrevIndicator { get; init; }
}

public enum ValueSource
{
    // price fields
    OpenPrice,
    ClosePrice,
    HighPrice,
    LowPrice,
    Volume,
    Amount,
    ChangePercent,
    TurnoverRate,

    // indicator fields
    MA5,
    MA10,
    MA20,
    RSI6,
    RSI12,
    VolumeMA5,
    VolumeMA10,
    VolumeMA120
}

public enum CompareOp
{
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
    Equal,
    NotEqual
}

