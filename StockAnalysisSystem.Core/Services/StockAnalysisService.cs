using Microsoft.EntityFrameworkCore;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.RealtimeData;

namespace StockAnalysisSystem.Core.Services;

/// <summary>
/// 股票分析服务
/// </summary>
public class StockAnalysisService
{
    private readonly IStockDailyDataRepository _dailyDataRepository;
    private readonly AppDbContext _context;

    public StockAnalysisService(
        IStockDailyDataRepository dailyDataRepository,
        AppDbContext context)
    {
        _dailyDataRepository = dailyDataRepository;
        _context = context;
    }

    /// <summary>
    /// 分析股票
    /// </summary>
    public async Task<StockAnalysisResult> AnalyzeStockAsync(string stockCode, string market)
    {
        var result = new StockAnalysisResult
        {
            StockCode = stockCode,
            Market = market,
            AnalysisTime = DateTime.Now
        };

        // 获取股票名称
        var stockInfo = await _context.StockInfos.FirstOrDefaultAsync(s =>
            s.StockCode == stockCode && s.Market == market);
        result.StockName = stockInfo?.StockName ?? "未知";

        // 构造stockId (格式: SH600000 或 SZ000001)
        var stockId = market.ToUpper() + stockCode;

        // 获取最近60个交易日数据
        var endDate = DateTime.Today;
        var startDate = endDate.AddDays(-90); // 获取90天内数据，确保有足够交易日
        var dailyData = await _dailyDataRepository.GetByStockIdAsync(stockId, startDate, endDate);

        if (dailyData == null || dailyData.Count < 20)
        {
            result.Score = 0;
            result.Recommendation = AnalysisRecommendation.持有观望;
            result.SignalDetails = "数据不足，无法分析";
            return result;
        }

        // 按日期排序（从旧到新）
        dailyData = dailyData.OrderBy(d => d.TradeDate).ToList();

        // 获取最新数据
        var latestData = dailyData.Last();
        result.ClosePrice = latestData.ClosePrice;
        result.ChangePercent = latestData.ChangePercent;
        result.Volume = latestData.Volume;

        // 计算MA
        var prices = dailyData.Select(d => d.ClosePrice).ToList();
        result.Ma5 = CalculateMA(prices, 5);
        result.Ma10 = CalculateMA(prices, 10);
        result.Ma20 = CalculateMA(prices, 20);

        // 计算MACD
        var (dif, dea, macd) = CalculateMACD(prices);
        result.MacdDif = dif;
        result.MacdDea = dea;
        result.MacdValue = macd;

        // 计算RSI
        result.Rsi6 = CalculateRSI(prices, 6);
        result.Rsi12 = CalculateRSI(prices, 12);

        // 计算KDJ (简化版)
        var (k, dVal, j) = CalculateKDJ(dailyData);
        result.K = k;
        result.D = dVal;
        result.J = j;

        // 计算BOLL
        var (upper, middle, lower) = CalculateBOLL(prices, 20, 2);
        result.BollUpper = upper;
        result.BollMiddle = middle;
        result.BollLower = lower;

        // 计算成交量均线
        var volumes = dailyData.Select(d => d.Volume).ToList();
        result.VolumeMa = CalculateMA(volumes, 5);

        // 计算评分
        result.MaScore = CalculateMaScore(result.Ma5, result.Ma10, result.Ma20);
        result.MacdScore = CalculateMacdScore(result.MacdDif, result.MacdDea, result.MacdValue);
        result.RsiScore = CalculateRsiScore(result.Rsi6, result.Rsi12);
        result.KdjScore = CalculateKdjScore(result.K, result.D);
        result.BollScore = CalculateBollScore(latestData.ClosePrice, result.BollUpper, result.BollLower);
        result.VolumeScore = CalculateVolumeScore(result.Volume, result.VolumeMa, latestData.ChangePercent);

        result.Score = result.MaScore + result.MacdScore + result.RsiScore +
                       result.KdjScore + result.BollScore + result.VolumeScore;

        result.Recommendation = GetRecommendation(result.Score);

        result.SignalDetails = GenerateSignalDetails(result);

        return result;
    }

    /// <summary>
    /// 分析股票（结合分时数据和日线数据）
    /// </summary>
    /// <param name="stockCode">股票代码</param>
    /// <param name="market">市场(SH/SZ)</param>
    /// <param name="intradayData">分时数据</param>
    /// <param name="currentPrice">当前价格（来自实时行情）</param>
    /// <param name="changePercent">涨跌幅（来自实时行情）</param>
    public async Task<StockAnalysisResult> AnalyzeStockWithIntradayAsync(
        string stockCode,
        string market,
        List<MinuteChartData>? intradayData,
        decimal? currentPrice,
        decimal? changePercent)
    {
        var result = new StockAnalysisResult
        {
            StockCode = stockCode,
            Market = market,
            AnalysisTime = DateTime.Now
        };

        // 获取股票名称
        var stockInfo = await _context.StockInfos.FirstOrDefaultAsync(s =>
            s.StockCode == stockCode && s.Market == market);
        result.StockName = stockInfo?.StockName ?? "未知";

        // 构造stockId (格式: SH600000 或 SZ000001)
        var stockId = market.ToUpper() + stockCode;

        // 获取最近60个交易日数据
        var endDate = DateTime.Today;
        var startDate = endDate.AddDays(-90);
        var dailyData = await _dailyDataRepository.GetByStockIdAsync(stockId, startDate, endDate);

        if (dailyData == null || dailyData.Count < 20)
        {
            result.Score = 0;
            result.Recommendation = AnalysisRecommendation.持有观望;
            result.SignalDetails = "数据不足，无法分析";
            return result;
        }

        // 按日期排序
        dailyData = dailyData.OrderBy(d => d.TradeDate).ToList();

        // 合并分时数据和日线数据来计算指标
        var priceList = new List<decimal>();
        var volumeList = new List<decimal>();

        // 添加历史日线数据
        foreach (var day in dailyData)
        {
            priceList.Add(day.ClosePrice);
            volumeList.Add(day.Volume);
        }

        // 如果有分时数据，用分时数据的收盘价替换今天的日线数据
        if (intradayData != null && intradayData.Count > 0)
        {
            var latestIntraday = intradayData.Last();
            if (currentPrice.HasValue)
            {
                // 用实时价格替换今天的收盘价
                if (priceList.Count > 0)
                {
                    priceList[priceList.Count - 1] = currentPrice.Value;
                }
            }

            // 添加分时数据作为额外的参考点（用于更精确的当日指标）
            foreach (var minute in intradayData)
            {
                priceList.Add(minute.Close);
                volumeList.Add(minute.Volume);
            }
        }

        // 获取最新数据
        var latestData = dailyData.Last();
        result.ClosePrice = currentPrice ?? latestData.ClosePrice;
        result.ChangePercent = changePercent ?? latestData.ChangePercent;
        result.Volume = intradayData?.LastOrDefault()?.Volume ?? latestData.Volume;

        // 计算MA（使用合并后的数据）
        result.Ma5 = CalculateMA(priceList, 5);
        result.Ma10 = CalculateMA(priceList, 10);
        result.Ma20 = CalculateMA(priceList, 20);

        // 计算MACD
        var (dif, dea, macd) = CalculateMACD(priceList);
        result.MacdDif = dif;
        result.MacdDea = dea;
        result.MacdValue = macd;

        // 计算RSI
        result.Rsi6 = CalculateRSI(priceList, 6);
        result.Rsi12 = CalculateRSI(priceList, 12);

        // 计算KDJ (使用日线数据)
        var (k, dVal, j) = CalculateKDJ(dailyData);
        result.K = k;
        result.D = dVal;
        result.J = j;

        // 计算BOLL
        var (upper, middle, lower) = CalculateBOLL(priceList, 20, 2);
        result.BollUpper = upper;
        result.BollMiddle = middle;
        result.BollLower = lower;

        // 计算成交量均线
        result.VolumeMa = CalculateMA(volumeList, 5);

        // 计算评分
        result.MaScore = CalculateMaScore(result.Ma5, result.Ma10, result.Ma20);
        result.MacdScore = CalculateMacdScore(result.MacdDif, result.MacdDea, result.MacdValue);
        result.RsiScore = CalculateRsiScore(result.Rsi6, result.Rsi12);
        result.KdjScore = CalculateKdjScore(result.K, result.D);
        result.BollScore = CalculateBollScore(result.ClosePrice, result.BollUpper, result.BollLower);
        result.VolumeScore = CalculateVolumeScore(result.Volume, result.VolumeMa, result.ChangePercent);

        result.Score = result.MaScore + result.MacdScore + result.RsiScore +
                       result.KdjScore + result.BollScore + result.VolumeScore;

        result.Recommendation = GetRecommendation(result.Score);

        result.SignalDetails = GenerateSignalDetails(result);

        return result;
    }

    #region 技术指标计算

    private decimal? CalculateMA(List<decimal> prices, int period)
    {
        if (prices.Count < period) return null;
        return prices.TakeLast(period).Average();
    }

    private (decimal dif, decimal dea, decimal macd) CalculateMACD(List<decimal> prices, int fast = 12, int slow = 26, int signal = 9)
    {
        if (prices.Count < slow) return (0, 0, 0);

        var emaFast = CalculateEMA(prices, fast);
        var emaSlow = CalculateEMA(prices, slow);

        var dif = emaFast - emaSlow;
        var dea = dif * 0.8m; // 简化版DEA
        var macd = (dif - dea) * 2;

        return (dif, dea, macd);
    }

    private decimal CalculateEMA(List<decimal> prices, int period)
    {
        if (prices.Count < period) return 0;
        var multiplier = 2m / (period + 1);
        var ema = prices.Take(period).Average();

        for (int i = period; i < prices.Count; i++)
        {
            ema = (prices[i] - ema) * multiplier + ema;
        }

        return ema;
    }

    private decimal CalculateRSI(List<decimal> prices, int period = 14)
    {
        if (prices.Count < period + 1) return 50;

        var gains = new List<decimal>();
        var losses = new List<decimal>();

        for (int i = 1; i < prices.Count; i++)
        {
            var change = prices[i] - prices[i - 1];
            gains.Add(change > 0 ? change : 0);
            losses.Add(change < 0 ? -change : 0);
        }

        if (gains.Count < period) return 50;

        var avgGain = gains.TakeLast(period).Average();
        var avgLoss = losses.TakeLast(period).Average();

        if (avgLoss == 0) return 100;

        var rs = avgGain / avgLoss;
        return 100 - (100 / (1 + rs));
    }

    private (decimal k, decimal d, decimal j) CalculateKDJ(List<StockDailyData> data, int n = 9)
    {
        if (data.Count < n) return (50, 50, 50);

        var recentData = data.TakeLast(n).ToList();
        var highMax = recentData.Max(d => d.HighPrice);
        var lowMin = recentData.Min(d => d.LowPrice);

        if (highMax == lowMin) return (50, 50, 50);

        var rsv = ((decimal)recentData.Last().ClosePrice - lowMin) / (highMax - lowMin) * 100;
        var k = rsv; // 简化版
        var d = k * 0.9m;
        var j = k * 3 - d * 2;

        return (k, d, j);
    }

    private (decimal upper, decimal middle, decimal lower) CalculateBOLL(List<decimal> prices, int period = 20, decimal k = 2)
    {
        if (prices.Count < period) return (0, 0, 0);

        var ma = prices.TakeLast(period).Average();
        var stdDev = CalculateStdDev(prices.TakeLast(period).ToList());

        var upper = ma + k * stdDev;
        var lower = ma - k * stdDev;

        return (upper, ma, lower);
    }

    private decimal CalculateStdDev(List<decimal> values)
    {
        if (values.Count < 2) return 0;

        var avg = values.Average();
        var sumOfSquares = values.Sum(v => (v - avg) * (v - avg));
        return (decimal)Math.Sqrt((double)(sumOfSquares / values.Count));
    }

    #endregion

    #region 评分计算

    private int CalculateMaScore(decimal? ma5, decimal? ma10, decimal? ma20)
    {
        if (ma5 == null || ma10 == null || ma20 == null) return 10;

        // 多头排列：MA5 > MA10 > MA20
        if (ma5 > ma10 && ma10 > ma20) return 20;
        // 接近多头
        if (ma5 > ma10 || ma10 > ma20) return 12;
        // 空头排列
        if (ma5 < ma10 && ma10 < ma20) return 0;
        // 接近空头
        if (ma5 < ma10 || ma10 < ma20) return 8;

        return 10;
    }

    private int CalculateMacdScore(decimal? dif, decimal? dea, decimal? macd)
    {
        if (dif == null || dea == null) return 12;

        // 金叉且多头：DIF > DEA 且 MACD > 0
        if (dif > dea && macd > 0) return 25;
        // 死叉且空头：DIF < DEA 且 MACD < 0
        if (dif < dea && macd < 0) return 0;
        // DIF > DEA 但 MACD < 0 (背离)
        if (dif > dea) return 18;
        // DIF < DEA 但 MACD > 0 (背离)
        if (dif < dea) return 6;

        return 12;
    }

    private int CalculateRsiScore(decimal? rsi6, decimal? rsi12)
    {
        if (rsi6 == null) return 10;

        var rsi = (rsi6.Value + rsi12.Value) / 2;

        // 超卖区域，可能反弹
        if (rsi < 30) return 20;
        // 超买区域，可能回调
        if (rsi > 70) return 0;
        // 偏多区域
        if (rsi > 50) return 14;
        // 偏空区域
        if (rsi < 50) return 6;

        return 10;
    }

    private int CalculateKdjScore(decimal? k, decimal? d)
    {
        if (k == null || d == null) return 7;

        // K值低于20超卖，可能反弹
        if (k < 20) return 15;
        // K值高于80超买，可能回调
        if (k > 80) return 0;
        // 金叉信号
        if (k > d) return 12;
        // 死叉信号
        if (k < d) return 4;

        return 7;
    }

    private int CalculateBollScore(decimal? closePrice, decimal? upper, decimal? lower)
    {
        if (closePrice == null || upper == null || lower == null) return 5;

        // 触及下轨，可能反弹
        if (closePrice <= lower) return 10;
        // 触及上轨，可能回调
        if (closePrice >= upper) return 0;
        // 在中轨上方
        if (closePrice > upper - (upper - lower) * 0.3m) return 7;

        return 5;
    }

    private int CalculateVolumeScore(decimal? volume, decimal? volumeMa, decimal? changePercent)
    {
        if (volume == null || volumeMa == null || changePercent == null) return 5;

        // 放量上涨
        if (volume > volumeMa && changePercent > 0) return 10;
        // 缩量下跌
        if (volume < volumeMa && changePercent < 0) return 0;
        // 放量下跌
        if (volume > volumeMa && changePercent < 0) return 4;
        // 缩量上涨
        if (volume < volumeMa && changePercent > 0) return 6;

        return 5;
    }

    private AnalysisRecommendation GetRecommendation(int score)
    {
        return score switch
        {
            >= 80 => AnalysisRecommendation.强烈买入,
            >= 60 => AnalysisRecommendation.建议买入,
            >= 40 => AnalysisRecommendation.持有观望,
            >= 20 => AnalysisRecommendation.建议卖出,
            _ => AnalysisRecommendation.强烈卖出
        };
    }

    private string GenerateSignalDetails(StockAnalysisResult result)
    {
        var details = new List<string>();

        // MA信号
        if (result.Ma5 > result.Ma10 && result.Ma10 > result.Ma20)
            details.Add("MA: 多头排列 📈");
        else if (result.Ma5 < result.Ma10 && result.Ma10 < result.Ma20)
            details.Add("MA: 空头排列 📉");
        else
            details.Add("MA: 均线缠绕");

        // MACD信号
        if (result.MacdDif > result.MacdDea && result.MacdValue > 0)
            details.Add("MACD: 金叉上涨");
        else if (result.MacdDif < result.MacdDea && result.MacdValue < 0)
            details.Add("MACD: 死叉下跌");
        else
            details.Add("MACD: 盘整");

        // RSI信号
        if (result.Rsi6 < 30)
            details.Add("RSI: 超卖");
        else if (result.Rsi6 > 70)
            details.Add("RSI: 超买");
        else if (result.Rsi6 > 50)
            details.Add("RSI: 偏多");
        else
            details.Add("RSI: 偏空");

        // KDJ信号
        if (result.K < 20)
            details.Add("KDJ: 超卖");
        else if (result.K > 80)
            details.Add("KDJ: 超买");
        else if (result.K > result.D)
            details.Add("KDJ: 金叉");
        else
            details.Add("KDJ: 死叉");

        // BOLL信号
        if (result.ClosePrice <= result.BollLower)
            details.Add("BOLL: 触及下轨");
        else if (result.ClosePrice >= result.BollUpper)
            details.Add("BOLL: 触及上轨");
        else
            details.Add("BOLL: 区间运行");

        // 成交量
        if (result.Volume > result.VolumeMa && result.ChangePercent > 0)
            details.Add("成交量: 放量上涨");
        else if (result.Volume < result.VolumeMa && result.ChangePercent < 0)
            details.Add("成交量: 缩量下跌");
        else
            details.Add("成交量: 正常");

        return string.Join(" | ", details);
    }

    #endregion
}
