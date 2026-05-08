namespace StockAnalysisSystem.Core.Backtest.V2;

/// <summary>
/// V2 回测统一结果合同：所有回测都应产出一致的时间序列与绩效指标。
/// </summary>
public sealed class BacktestResultV2
{
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }

    /// <summary>初始资金（期初权益）。</summary>
    public required decimal InitialCapital { get; init; }

    /// <summary>期末权益（现金+持仓市值）。</summary>
    public decimal FinalEquity { get; set; }

    /// <summary>逐交易日组合状态点（权益/现金/持仓市值）。</summary>
    public List<PortfolioPointV2> EquityCurve { get; } = new();

    /// <summary>逐交易日回撤序列（underwater），数值范围 0~1。</summary>
    public List<DrawdownPointV2> DrawdownCurve { get; } = new();

    /// <summary>逐交易日收益率序列（相邻两点权益变化率），数值范围不做限制。</summary>
    public List<ReturnPointV2> DailyReturns { get; } = new();

    /// <summary>交易记录（完整闭合交易）。</summary>
    public List<TradeRecordV2> Trades { get; } = new();

    public BacktestMetricsV2 Metrics { get; set; } = new();
}

public sealed class PortfolioPointV2
{
    public required DateTime Date { get; init; }
    public required decimal Equity { get; init; }
    public required decimal Cash { get; init; }
    public required decimal PositionValue { get; init; }
}

public sealed class DrawdownPointV2
{
    public required DateTime Date { get; init; }
    /// <summary>回撤比例（例如 0.1234 表示 12.34% 回撤）。</summary>
    public required decimal Drawdown { get; init; }
    /// <summary>当日之前的权益峰值。</summary>
    public required decimal PeakEquity { get; init; }
}

public sealed class ReturnPointV2
{
    public required DateTime Date { get; init; }
    /// <summary>当日收益率（例如 0.01 表示 +1%）。</summary>
    public required decimal Return { get; init; }
}

public sealed class TradeRecordV2
{
    public required string StockId { get; init; }
    public required string StockCode { get; init; }
    public required string StockName { get; init; }

    /// <summary>买入来源/策略名称（可用于多策略对比）。</summary>
    public required string StrategyName { get; init; }

    public required DateTime BuyDate { get; init; }
    public required decimal BuyPrice { get; init; }
    public required int Shares { get; init; }

    public required DateTime SellDate { get; init; }
    public required decimal SellPrice { get; init; }

    public decimal Commission { get; init; }
    public string SellReason { get; init; } = string.Empty;

    public decimal ProfitLoss { get; init; }
    public decimal ProfitLossPercent { get; init; }
    public int HoldingDays { get; init; }
}

public sealed class BacktestMetricsV2
{
    /// <summary>总收益率（0~∞，例如 0.5 表示 +50%）。</summary>
    public decimal TotalReturn { get; set; }
    /// <summary>年化收益率（0~∞，例如 0.3 表示 +30%）。</summary>
    public decimal AnnualReturn { get; set; }

    /// <summary>最大回撤（0~1）。</summary>
    public decimal MaxDrawdown { get; set; }
    public DateTime? MaxDrawdownStartDate { get; set; }
    public DateTime? MaxDrawdownValleyDate { get; set; }
    public DateTime? MaxDrawdownRecoveryDate { get; set; }
    public int? MaxDrawdownDurationDays { get; set; }
    public int? MaxDrawdownRecoveryDays { get; set; }

    public decimal SharpeRatio { get; set; }
    public decimal SortinoRatio { get; set; }
    /// <summary>卡玛比率 = 年化收益率 / 最大回撤（两者均为 0~1）。</summary>
    public decimal CalmarRatio { get; set; }

    public int TradeCount { get; set; }
    public int WinCount { get; set; }
    public int LossCount { get; set; }
    public decimal WinRate { get; set; } // 0~1

    public decimal ProfitFactor { get; set; }
    public decimal AverageProfit { get; set; }
    public decimal AverageLoss { get; set; }
    public decimal MaxProfit { get; set; }
    public decimal MaxLoss { get; set; }

    /// <summary>交易日数量（来自 EquityCurve 点数）。</summary>
    public int TradingDays { get; set; }
}

