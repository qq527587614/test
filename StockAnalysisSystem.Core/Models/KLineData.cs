namespace StockAnalysisSystem.Core.Models;

/// <summary>
/// K线数据模型
/// </summary>
public class KLineData
{
    /// <summary>
    /// 日期
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// 开盘价
    /// </summary>
    public decimal Open { get; set; }

    /// <summary>
    /// 最高价
    /// </summary>
    public decimal High { get; set; }

    /// <summary>
    /// 最低价
    /// </summary>
    public decimal Low { get; set; }

    /// <summary>
    /// 收盘价
    /// </summary>
    public decimal Close { get; set; }

    /// <summary>
    /// 成交量
    /// </summary>
    public decimal Volume { get; set; }

    /// <summary>
    /// 股票名称
    /// </summary>
    public string StockName { get; set; } = "";
}
