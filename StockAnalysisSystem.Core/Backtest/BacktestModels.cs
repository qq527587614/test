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
    public List<int>? StrategyIds { get; set; }          // 组合策略ID列表（为null时使用历史选股记录）
}

/// <summary>
/// 「十等份仓位 + 盈利收盘卖 / 最多持有 N 个交易日」组合回测参数；选股与每日选股「组合选股」一致（多策略且交集）。
/// </summary>
public class FirstBoardPullbackPortfolioSettings
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    /// <summary>
    /// 与每日选股界面「组合选股」勾选顺序一致：对每个 Id 单独调用 <c>PickAsync(仅该策略)</c> 后取股票交集。
    /// 仅一项时等价于单策略选股。
    /// </summary>
    public List<int> CombineStrategyIds { get; set; } = new();
    public decimal InitialCapital { get; set; } = 1_000_000m;
    public decimal Commission { get; set; } = 0.00025m;
    public decimal Slippage { get; set; } = 0.001m;
    /// <summary>最大同时持仓数（资金等分份数），默认 10。</summary>
    public int MaxSlots { get; set; } = 10;
    /// <summary>买入后第几个交易日收盘起可评估；达到该天数仍未盈利则强制卖出（默认 3 表示 T+1、T+2、T+3 三个收盘评估，T+3 未盈利也卖）。</summary>
    public int MaxHoldingSessionsAfterEntry { get; set; } = 3;
    /// <summary>是否启用市场环境过滤（按当日上涨家数占比）。</summary>
    public bool EnableMarketFilter { get; set; } = true;
    /// <summary>市场过滤阈值：当日上涨家数占比需 >= 该值（0~1）。</summary>
    public decimal MinMarketUpRatio { get; set; } = 0.45m;
    /// <summary>每日最多买入候选数量（按 FinalScore 排序）。</summary>
    public int MaxPicksPerDay { get; set; } = 3;
    /// <summary>最小止盈阈值（%）：达到该收益率才止盈卖出。</summary>
    public decimal TakeProfitMinPercent { get; set; } = 1.5m;
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
