using System.Text.Json;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.Core.Indicators;

/// <summary>
/// 技术指标计算器
/// </summary>
public class IndicatorCalculator
{
    /// <summary>
    /// 计算移动平均线
    /// </summary>
    public static List<decimal?> CalculateMA(List<decimal> prices, int period)
    {
        try
        {
            var result = new List<decimal?>();

            for (int i = 0; i < prices.Count; i++)
            {
                if (i < period - 1)
                {
                    result.Add(null);
                }
                else
                {
                    decimal sum = 0;
                    for (int j = 0; j < period; j++)
                    {
                        sum += prices[i - j];
                    }
                    result.Add(Math.Round(sum / period, 4));
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, 
                $"Method: {nameof(CalculateMA)}", 
                new { Period = period, PricesCount = prices.Count });
            throw;
        }
    }

    /// <summary>
    /// 计算EMA (指数移动平均)
    /// </summary>
    public static List<decimal?> CalculateEMA(List<decimal> prices, int period)
    {
        var result = new List<decimal?>();
        decimal multiplier = 2m / (period + 1);

        for (int i = 0; i < prices.Count; i++)
        {
            if (i < period - 1)
            {
                result.Add(null);
            }
            else if (i == period - 1)
            {
                // 第一个EMA使用简单平均
                decimal sum = 0;
                for (int j = 0; j < period; j++)
                {
                    sum += prices[i - j];
                }
                result.Add(Math.Round(sum / period, 4));
            }
            else
            {
                var prevEma = result[i - 1]!.Value;
                var ema = (prices[i] - prevEma) * multiplier + prevEma;
                result.Add(Math.Round(ema, 4));
            }
        }

        return result;
    }

    /// <summary>
    /// 计算MACD
    /// </summary>
    public static (List<decimal?> dif, List<decimal?> dea, List<decimal?> macd) CalculateMACD(
        List<decimal> prices, int fast = 12, int slow = 26, int signal = 9)
    {
        try
        {
            var emaFast = CalculateEMA(prices, fast);
            var emaSlow = CalculateEMA(prices, slow);

            // DIF = 快线EMA - 慢线EMA
            var dif = new List<decimal?>();
            for (int i = 0; i < prices.Count; i++)
            {
                if (emaFast[i].HasValue && emaSlow[i].HasValue)
                {
                    dif.Add(Math.Round(emaFast[i]!.Value - emaSlow[i]!.Value, 4));
                }
                else
                {
                    dif.Add(null);
                }
            }

            // DEA = DIF的EMA
            var dea = CalculateEMA(dif.Where(d => d.HasValue).Select(d => d!.Value).ToList(), signal);
            
            // 调整DEA的索引
            var adjustedDea = new List<decimal?>();
            int nullCount = dif.Count(d => !d.HasValue);
            for (int i = 0; i < nullCount; i++)
            {
                adjustedDea.Add(null);
            }
            adjustedDea.AddRange(dea);

            // MACD = 2 * (DIF - DEA)
            var macd = new List<decimal?>();
            for (int i = 0; i < prices.Count; i++)
            {
                if (dif[i].HasValue && i < adjustedDea.Count && adjustedDea[i].HasValue)
                {
                    macd.Add(Math.Round(2 * (dif[i]!.Value - adjustedDea[i]!.Value), 4));
                }
                else
                {
                    macd.Add(null);
                }
            }

            return (dif, adjustedDea, macd);
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, 
                $"Method: {nameof(CalculateMACD)}", 
                new { Fast = fast, Slow = slow, Signal = signal, PricesCount = prices.Count });
            throw;
        }
    }

    /// <summary>
    /// 计算RSI
    /// </summary>
    public static List<decimal?> CalculateRSI(List<decimal> prices, int period = 14)
    {
        try
        {
            var result = new List<decimal?>();

            if (prices.Count < period + 1)
            {
                for (int i = 0; i < prices.Count; i++)
                    result.Add(null);
                return result;
            }

            decimal gainSum = 0;
            decimal lossSum = 0;

            for (int i = 1; i <= period; i++)
            {
                var change = prices[i] - prices[i - 1];
                if (change > 0)
                    gainSum += change;
                else
                    lossSum += Math.Abs(change);
            }

            // 前period个设为null
            for (int i = 0; i < period; i++)
                result.Add(null);

            if (gainSum + lossSum == 0)
            {
                result.Add(50m);
            }
            else if (lossSum == 0)
            {
                // 只有涨幅没有跌幅时，RSI = 100
                result.Add(100m);
            }
            else
            {
                var rs = gainSum / lossSum;
                result.Add(Math.Round(100 - 100 / (1 + rs), 4));
            }

            // 后续使用平滑方法
            decimal avgGain = gainSum / period;
            decimal avgLoss = lossSum / period;

            for (int i = period + 1; i < prices.Count; i++)
            {
                var change = prices[i] - prices[i - 1];
                if (change > 0)
                {
                    avgGain = (avgGain * (period - 1) + change) / period;
                    avgLoss = (avgLoss * (period - 1)) / period;
                }
                else
                {
                    avgGain = (avgGain * (period - 1)) / period;
                    avgLoss = (avgLoss * (period - 1) + Math.Abs(change)) / period;
                }

                if (avgGain + avgLoss == 0)
                {
                    result.Add(50m);
                }
                else if (avgLoss == 0)
                {
                    // 只有涨幅没有跌幅时，RSI = 100
                    result.Add(100m);
                }
                else
                {
                    var rs = avgGain / avgLoss;
                    result.Add(Math.Round(100 - 100 / (1 + rs), 4));
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, 
                $"Method: {nameof(CalculateRSI)}", 
                new { Period = period, PricesCount = prices.Count });
            throw;
        }
    }

    /// <summary>
    /// 计算KDJ
    /// </summary>
    public static (List<decimal?> k, List<decimal?> d, List<decimal?> j) CalculateKDJ(
        List<StockDailyData> data, int n = 9, int m1 = 3, int m2 = 3)
    {
        try
        {
            var kList = new List<decimal?>();
            var dList = new List<decimal?>();
            var jList = new List<decimal?>();

            decimal prevK = 50;
            decimal prevD = 50;

            for (int i = 0; i < data.Count; i++)
            {
                if (i < n - 1)
                {
                    kList.Add(null);
                    dList.Add(null);
                    jList.Add(null);
                    continue;
                }

                // 计算N日内的最高价和最低价
                decimal highestHigh = decimal.MinValue;
                decimal lowestLow = decimal.MaxValue;
                for (int idx = 0; idx < n; idx++)
                {
                    if (data[i - idx].HighPrice > highestHigh)
                        highestHigh = data[i - idx].HighPrice;
                    if (data[i - idx].LowPrice < lowestLow)
                        lowestLow = data[i - idx].LowPrice;
                }

                decimal rsv = 0;
                if (highestHigh != lowestLow)
                {
                    rsv = (data[i].ClosePrice - lowestLow) / (highestHigh - lowestLow) * 100;
                }
                else
                {
                    rsv = 50;
                }

                // 计算K值
                decimal k = (2m / 3) * prevK + (1m / 3) * rsv;
                
                // 计算D值
                decimal d = (2m / 3) * prevD + (1m / 3) * k;
                
                // 计算J值
                decimal j = 3 * k - 2 * d;

                kList.Add(Math.Round(k, 4));
                dList.Add(Math.Round(d, 4));
                jList.Add(Math.Round(j, 4));

                prevK = k;
                prevD = d;
            }

            return (kList, dList, jList);
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, 
                $"Method: {nameof(CalculateKDJ)}", 
                new { N = n, M1 = m1, M2 = m2, DataCount = data.Count });
            throw;
        }
    }

    /// <summary>
    /// 计算布林带
    /// </summary>
    public static (List<decimal?> upper, List<decimal?> middle, List<decimal?> lower) CalculateBOLL(
        List<decimal> prices, int period = 20, decimal k = 2)
    {
        var middle = CalculateMA(prices, period);
        var upper = new List<decimal?>();
        var lower = new List<decimal?>();

        for (int i = 0; i < prices.Count; i++)
        {
            if (i < period - 1)
            {
                upper.Add(null);
                lower.Add(null);
                continue;
            }

            // 计算标准差
            decimal sum = 0;
            for (int j = 0; j < period; j++)
            {
                sum += prices[i - j];
            }
            decimal mean = sum / period;

            decimal sumSquaredDiff = 0;
            for (int j = 0; j < period; j++)
            {
                var diff = prices[i - j] - mean;
                sumSquaredDiff += diff * diff;
            }
            decimal stdDev = (decimal)Math.Sqrt((double)(sumSquaredDiff / period));

            upper.Add(Math.Round(middle[i]!.Value + k * stdDev, 4));
            lower.Add(Math.Round(middle[i]!.Value - k * stdDev, 4));
        }

        return (upper, middle, lower);
    }

    /// <summary>
    /// 批量计算所有指标并返回实体列表
    /// </summary>
    public static List<StockDailyIndicator> CalculateAll(string stockId, List<StockDailyData> data)
    {
        try
        {
            var result = new List<StockDailyIndicator>();

            if (data.Count == 0)
                return result;

            var prices = data.Select(d => d.ClosePrice).ToList();
            var volumes = data.Select(d => d.Volume).ToList();

            // 计算各指标
            var ma5 = CalculateMA(prices, 5);
            var ma10 = CalculateMA(prices, 10);
            var ma20 = CalculateMA(prices, 20);
            var (dif, dea, macd) = CalculateMACD(prices);
            var (k, d, j) = CalculateKDJ(data);
            var rsi6 = CalculateRSI(prices, 6);
            var rsi12 = CalculateRSI(prices, 12);
            var (bollUpper, bollMiddle, bollLower) = CalculateBOLL(prices);
            var volumeMa5 = CalculateMA(volumes, 5);
            var volumeMa10 = CalculateMA(volumes, 10);
            var volumeMa120 = CalculateMA(volumes, 120);

            for (int i = 0; i < data.Count; i++)
            {
                var indicator = new StockDailyIndicator
                {
                    StockId = stockId,
                    TradeDate = data[i].TradeDate,
                    MA5 = ma5[i],
                    MA10 = ma10[i],
                    MA20 = ma20[i],
                    RSI6 = rsi6[i],
                    RSI12 = rsi12[i],
                    VolumeMA5 = volumeMa5[i],
                    VolumeMA10 = volumeMa10[i],
                    VolumeMA120 = volumeMa120[i],
                    CreatedAt = DateTime.Now
                };

                // MACD JSON
                if (dif[i].HasValue)
                {
                    indicator.MACD = JsonSerializer.Serialize(new
                    {
                        DIF = dif[i],
                        DEA = dea[i],
                        MACD = macd[i]
                    });
                }

                // KDJ JSON
                if (k[i].HasValue)
                {
                    indicator.KDJ = JsonSerializer.Serialize(new
                    {
                        K = k[i],
                        D = d[i],
                        J = j[i]
                    });
                }

                // BOLL JSON
                if (bollUpper[i].HasValue)
                {
                    indicator.BOLL = JsonSerializer.Serialize(new
                    {
                        Upper = bollUpper[i],
                        Middle = bollMiddle[i],
                        Lower = bollLower[i]
                    });
                }

                result.Add(indicator);
            }

            return result;
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, 
                $"Method: {nameof(CalculateAll)}", 
                new { StockId = stockId, DataCount = data.Count });
            throw;
        }
    }
}
