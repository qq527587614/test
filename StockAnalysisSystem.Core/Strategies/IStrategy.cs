using StockAnalysisSystem.Core.Entities;

namespace StockAnalysisSystem.Core.Strategies;

/// <summary>
/// 信号类型
/// </summary>
public enum SignalType
{
    Hold = 0,
    Buy = 1,
    Sell = -1
}

/// <summary>
/// 交易信号
/// </summary>
public class Signal
{
    public DateTime Date { get; set; }
    public SignalType Type { get; set; }
    public decimal Strength { get; set; } = 1.0m;
    public string Reason { get; set; } = string.Empty;
    public string StockId { get; set; } = string.Empty;
}

/// <summary>
/// 策略接口
/// </summary>
public interface IStrategy
{
    string Name { get; }
    string StrategyType { get; }
    Dictionary<string, object> Parameters { get; set; }
    
    /// <summary>
    /// 生成交易信号
    /// </summary>
    /// <param name="stockId">股票ID</param>
    /// <param name="dailyData">日线数据</param>
    /// <param name="indicators">指标数据</param>
    /// <returns>信号列表</returns>
    List<Signal> GenerateSignals(string stockId, List<StockDailyData> dailyData, List<StockDailyIndicator> indicators);
}
