using StockAnalysisSystem.Core.Models;

namespace StockAnalysisSystem.Core.Services;

/// <summary>
/// K线数据服务接口
/// </summary>
public interface IKLineDataService
{
    /// <summary>
    /// 获取K线数据
    /// </summary>
    /// <param name="stockCode">股票代码（如 "sh600000"）</param>
    /// <param name="period">周期类型</param>
    /// <param name="count">获取数据条数（默认500条）</param>
    /// <returns>K线数据列表</returns>
    Task<List<KLineData>> GetKLineDataAsync(string stockCode, PeriodType period, int count = 500);
}
