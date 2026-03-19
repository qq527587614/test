namespace StockAnalysisSystem.Core.Backtest;

/// <summary>
/// 回测设置
/// </summary>
public class BacktestSettings
{
    public decimal InitialCapital { get; set; } = 1000000m;
    public decimal Commission { get; set; } = 0.00025m;  // 手续费率
    public decimal Slippage { get; set; } = 0.001m;      // 滑点
    public int MaxPositions { get; set; } = 10;         // 最大持仓数
    public decimal PositionSize { get; set; } = 0.1m;   // 单只股票仓位比例（每次买入使用可用资金的10%）
    public decimal StopLossPercent { get; set; } = -5m;  // 止损百分比（-5表示亏损5%时卖出）
}

/// <summary>
/// 每日选股回测设置
/// </summary>
public class DailyPickBacktestSettings
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int SharesPerPick { get; set; } = 100;        // 每次选股买入股数（整手）
    public decimal Commission { get; set; } = 0.00025m;  // 手续费率
    public decimal Slippage { get; set; } = 0.001m;      // 滑点
}

/// <summary>
/// 回测进度
/// </summary>
public class BacktestProgress
{
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public string Message { get; set; } = string.Empty;
    public double Percentage => TotalSteps > 0 ? (double)CurrentStep / TotalSteps * 100 : 0;
}

/// <summary>
/// 交易记录
/// </summary>
public class TradeRecord
{
    public string StockId { get; set; } = string.Empty;
    public string StockCode { get; set; } = string.Empty;
    public string StockName { get; set; } = string.Empty;
    public string StrategyName { get; set; } = string.Empty;  // 买入策略名称
    public DateTime BuyDate { get; set; }
    public decimal BuyPrice { get; set; }
    public int Shares { get; set; }
    public DateTime? SellDate { get; set; }
    public decimal? SellPrice { get; set; }
    public decimal ProfitLoss { get; set; }
    public decimal ProfitLossPercent { get; set; }
    public decimal Commission { get; set; }
    public int HoldingDays { get; set; }
    public string SellReason { get; set; } = string.Empty;
}

/// <summary>
/// 资金曲线点
/// </summary>
public class EquityPoint
{
    public DateTime Date { get; set; }
    public decimal Equity { get; set; }
    public decimal Cash { get; set; }
    public decimal PositionValue { get; set; }
}

/// <summary>
/// 持仓信息
/// </summary>
public class Position
{
    public string StockId { get; set; } = string.Empty;
    public string StockCode { get; set; } = string.Empty;
    public int Shares { get; set; }
    public decimal CostPrice { get; set; }
    public DateTime BuyDate { get; set; }
}

/// <summary>
/// 回测结果
/// </summary>
public class BacktestResult
{
    // 基本指标
    public decimal TotalReturn { get; set; }           // 总收益率
    public decimal AnnualReturn { get; set; }          // 年化收益率
    public decimal MaxDrawdown { get; set; }           // 最大回撤
    public decimal SharpeRatio { get; set; }           // 夏普比率
    public decimal WinRate { get; set; }               // 胜率
    public int TradeCount { get; set; }                // 交易次数
    public int WinCount { get; set; }                  // 盈利次数
    public int LossCount { get; set; }                 // 亏损次数
    public decimal ProfitFactor { get; set; }          // 盈亏比
    public decimal AverageProfit { get; set; }         // 平均盈利
    public decimal AverageLoss { get; set; }           // 平均亏损
    public decimal MaxProfit { get; set; }             // 单笔最大盈利
    public decimal MaxLoss { get; set; }               // 单笔最大亏损
    public int TradingDays { get; set; }               // 交易天数
    
    // 资金曲线
    public List<EquityPoint> EquityCurve { get; set; } = new();
    
    // 交易记录
    public List<TradeRecord> Trades { get; set; } = new();
    
    // 时间范围
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    
    // 初始资金
    public decimal InitialCapital { get; set; }
    public decimal FinalEquity { get; set; }
}
