using System.Text.Json;
using StockAnalysisSystem.Core.Entities;

namespace StockAnalysisSystem.Core.Strategies;

/// <summary>
/// 策略参数辅助方法
/// </summary>
public static class StrategyParameterHelper
{
    /// <summary>
    /// 安全地将参数值转换为整数
    /// </summary>
    public static int GetIntValue(object value, int defaultValue = 0)
    {
        return value switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            double doubleValue => (int)doubleValue,
            decimal decimalValue => (int)decimalValue,
            float floatValue => (int)floatValue,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number 
                => jsonElement.TryGetInt32(out var intVal) ? intVal : (int)jsonElement.GetDouble(),
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.String 
                && int.TryParse(jsonElement.GetString(), out var parsedVal) => parsedVal,
            string strValue when int.TryParse(strValue, out var parsedStr) => parsedStr,
            _ => defaultValue
        };
    }

    /// <summary>
    /// 安全地将参数值转换为小数
    /// </summary>
    public static decimal GetDecimalValue(object value, decimal defaultValue = 0)
    {
        return value switch
        {
            decimal decimalValue => decimalValue,
            double doubleValue => (decimal)doubleValue,
            float floatValue => (decimal)floatValue,
            int intValue => intValue,
            long longValue => longValue,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number 
                => jsonElement.GetDecimal(),
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.String 
                && decimal.TryParse(jsonElement.GetString(), out var parsedVal) => parsedVal,
            string strValue when decimal.TryParse(strValue, out var parsedStr) => parsedStr,
            _ => defaultValue
        };
    }

    /// <summary>
    /// 安全地将参数值转换为布尔值
    /// </summary>
    public static bool GetBoolValue(object value, bool defaultValue = false)
    {
        return value switch
        {
            bool boolValue => boolValue,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.True => true,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.False => false,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.String 
                && bool.TryParse(jsonElement.GetString(), out var parsedVal) => parsedVal,
            string strValue when bool.TryParse(strValue, out var parsedStr) => parsedStr,
            _ => defaultValue
        };
    }

    /// <summary>
    /// 安全地将参数值转换为字符串
    /// </summary>
    public static string GetStringValue(object value, string defaultValue = "")
    {
        return value switch
        {
            string strValue => strValue,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.String 
                => jsonElement.GetString() ?? defaultValue,
            JsonElement jsonElement => jsonElement.ToString(),
            _ => value?.ToString() ?? defaultValue
        };
    }
}

/// <summary>
/// 均线交叉策略
/// 短期均线上穿长期均线买入，下穿卖出
/// </summary>
public class MovingAverageCrossStrategy : IStrategy
{
    public string Name => "均线交叉策略";
    public string StrategyType => "MovingAverageCross";
    public Dictionary<string, object> Parameters { get; set; } = new()
    {
        ["ShortPeriod"] = 5,
        ["LongPeriod"] = 20
    };

    public List<Signal> GenerateSignals(string stockId, List<StockDailyData> dailyData, List<StockDailyIndicator> indicators)
    {
        var signals = new List<Signal>();
        
        if (dailyData.Count < 2 || indicators.Count < 2)
            return signals;

        var shortPeriod = StrategyParameterHelper.GetIntValue(Parameters["ShortPeriod"], 5);
        var longPeriod = StrategyParameterHelper.GetIntValue(Parameters["LongPeriod"], 20);

        // 计算均线
        var prices = dailyData.Select(d => d.ClosePrice).ToList();
        var shortMA = Indicators.IndicatorCalculator.CalculateMA(prices, shortPeriod);
        var longMA = Indicators.IndicatorCalculator.CalculateMA(prices, longPeriod);

        // 生成信号
        for (int i = 1; i < dailyData.Count; i++)
        {
            var prevShortMA = shortMA[i - 1];
            var currShortMA = shortMA[i];
            var prevLongMA = longMA[i - 1];
            var currLongMA = longMA[i];

            if (!prevShortMA.HasValue || !currShortMA.HasValue || !prevLongMA.HasValue || !currLongMA.HasValue)
                continue;

            var signal = new Signal
            {
                Date = dailyData[i].TradeDate,
                StockId = stockId
            };

            // 金叉买入
            if (prevShortMA.Value <= prevLongMA.Value && currShortMA.Value > currLongMA.Value)
            {
                signal.Type = SignalType.Buy;
                signal.Reason = $"MA{shortPeriod}上穿MA{longPeriod}，金叉买入信号";
                signals.Add(signal);
            }
            // 死叉卖出
            else if (prevShortMA.Value >= prevLongMA.Value && currShortMA.Value < currLongMA.Value)
            {
                signal.Type = SignalType.Sell;
                signal.Reason = $"MA{shortPeriod}下穿MA{longPeriod}，死叉卖出信号";
                signals.Add(signal);
            }
        }

        return signals;
    }
}

/// <summary>
/// MACD交叉策略
/// MACD金叉买入，死叉卖出
/// </summary>
public class MACDCrossStrategy : IStrategy
{
    public string Name => "MACD交叉策略";
    public string StrategyType => "MACDCross";
    public Dictionary<string, object> Parameters { get; set; } = new()
    {
        ["FastPeriod"] = 12,
        ["SlowPeriod"] = 26,
        ["SignalPeriod"] = 9
    };

    public List<Signal> GenerateSignals(string stockId, List<StockDailyData> dailyData, List<StockDailyIndicator> indicators)
    {
        var signals = new List<Signal>();
        
        if (dailyData.Count < 35)
            return signals;

        var fast = StrategyParameterHelper.GetIntValue(Parameters["FastPeriod"], 12);
        var slow = StrategyParameterHelper.GetIntValue(Parameters["SlowPeriod"], 26);
        var signalPeriod = StrategyParameterHelper.GetIntValue(Parameters["SignalPeriod"], 9);

        var prices = dailyData.Select(d => d.ClosePrice).ToList();
        var (dif, dea, macd) = Indicators.IndicatorCalculator.CalculateMACD(prices, fast, slow, signalPeriod);

        for (int i = 1; i < dailyData.Count; i++)
        {
            if (!dif[i].HasValue || !dea[i].HasValue || 
                !dif[i - 1].HasValue || !dea[i - 1].HasValue)
                continue;

            var signal = new Signal
            {
                Date = dailyData[i].TradeDate,
                StockId = stockId
            };

            // DIF上穿DEA（金叉）
            if (dif[i - 1].Value <= dea[i - 1].Value && dif[i].Value > dea[i].Value)
            {
                signal.Type = SignalType.Buy;
                signal.Reason = "MACD金叉买入信号";
                signals.Add(signal);
            }
            // DIF下穿DEA（死叉）
            else if (dif[i - 1].Value >= dea[i - 1].Value && dif[i].Value < dea[i].Value)
            {
                signal.Type = SignalType.Sell;
                signal.Reason = "MACD死叉卖出信号";
                signals.Add(signal);
            }
        }

        return signals;
    }
}

/// <summary>
/// RSI超卖策略
/// RSI低于阈值买入，高于阈值卖出
/// </summary>
public class RSIOverSoldStrategy : IStrategy
{
    public string Name => "RSI超卖策略";
    public string StrategyType => "RSIOverSold";
    public Dictionary<string, object> Parameters { get; set; } = new()
    {
        ["Period"] = 6,
        ["OversoldThreshold"] = 30,
        ["OverboughtThreshold"] = 70
    };

    public List<Signal> GenerateSignals(string stockId, List<StockDailyData> dailyData, List<StockDailyIndicator> indicators)
    {
        var signals = new List<Signal>();
        
        if (dailyData.Count < 10)
            return signals;

        var period = StrategyParameterHelper.GetIntValue(Parameters["Period"], 6);
        var oversold = StrategyParameterHelper.GetDecimalValue(Parameters["OversoldThreshold"], 30);
        var overbought = StrategyParameterHelper.GetDecimalValue(Parameters["OverboughtThreshold"], 70);

        var prices = dailyData.Select(d => d.ClosePrice).ToList();
        var rsi = Indicators.IndicatorCalculator.CalculateRSI(prices, period);

        for (int i = 1; i < dailyData.Count; i++)
        {
            if (!rsi[i].HasValue || !rsi[i - 1].HasValue)
                continue;

            var signal = new Signal
            {
                Date = dailyData[i].TradeDate,
                StockId = stockId
            };

            // RSI从下往上穿过超卖线
            if (rsi[i - 1].Value <= oversold && rsi[i].Value > oversold)
            {
                signal.Type = SignalType.Buy;
                signal.Reason = $"RSI({period})={rsi[i].Value:F2}，从超卖区反弹买入";
                signals.Add(signal);
            }
            // RSI从上往下穿过超买线
            else if (rsi[i - 1].Value >= overbought && rsi[i].Value < overbought)
            {
                signal.Type = SignalType.Sell;
                signal.Reason = $"RSI({period})={rsi[i].Value:F2}，从超买区回落卖出";
                signals.Add(signal);
            }
        }

        return signals;
    }
}

/// <summary>
/// 组合策略
/// 多个策略信号的组合
/// </summary>
public class CombinedStrategy : IStrategy
{
    public string Name => "组合策略";
    public string StrategyType => "Combined";
    public Dictionary<string, object> Parameters { get; set; } = new()
    {
        ["Strategies"] = "MovingAverageCross,MACDCross",
        ["RequireAll"] = false  // 是否要求所有策略都发出信号
    };

    private readonly List<IStrategy> _subStrategies = new();

    public void InitializeStrategies()
    {
        _subStrategies.Clear();
        var strategyTypes = StrategyParameterHelper.GetStringValue(Parameters["Strategies"], "MovingAverageCross,MACDCross")
            .Split(',', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var type in strategyTypes)
        {
            var strategy = StrategyFactory.Create(type.Trim(), new Dictionary<string, object>());
            if (strategy != null)
                _subStrategies.Add(strategy);
        }
    }

    public List<Signal> GenerateSignals(string stockId, List<StockDailyData> dailyData, List<StockDailyIndicator> indicators)
    {
        if (_subStrategies.Count == 0)
            InitializeStrategies();

        var requireAll = StrategyParameterHelper.GetBoolValue(Parameters["RequireAll"], false);
        var allSignals = new Dictionary<DateTime, List<Signal>>();

        foreach (var strategy in _subStrategies)
        {
            var signals = strategy.GenerateSignals(stockId, dailyData, indicators);
            foreach (var signal in signals)
            {
                if (!allSignals.ContainsKey(signal.Date))
                    allSignals[signal.Date] = new List<Signal>();
                allSignals[signal.Date].Add(signal);
            }
        }

        var result = new List<Signal>();

        foreach (var kvp in allSignals)
        {
            var date = kvp.Key;
            var dateSignals = kvp.Value;

            var buyCount = dateSignals.Count(s => s.Type == SignalType.Buy);
            var sellCount = dateSignals.Count(s => s.Type == SignalType.Sell);

            if (requireAll)
            {
                // 要求所有策略都发出信号
                if (buyCount == _subStrategies.Count)
                {
                    result.Add(new Signal
                    {
                        Date = date,
                        StockId = stockId,
                        Type = SignalType.Buy,
                        Reason = $"所有策略一致买入信号"
                    });
                }
                else if (sellCount == _subStrategies.Count)
                {
                    result.Add(new Signal
                    {
                        Date = date,
                        StockId = stockId,
                        Type = SignalType.Sell,
                        Reason = $"所有策略一致卖出信号"
                    });
                }
            }
            else
            {
                // 多数投票
                if (buyCount > sellCount && buyCount > _subStrategies.Count / 2)
                {
                    result.Add(new Signal
                    {
                        Date = date,
                        StockId = stockId,
                        Type = SignalType.Buy,
                        Strength = (decimal)buyCount / _subStrategies.Count,
                        Reason = $"{buyCount}/{_subStrategies.Count}策略买入信号"
                    });
                }
                else if (sellCount > buyCount && sellCount > _subStrategies.Count / 2)
                {
                    result.Add(new Signal
                    {
                        Date = date,
                        StockId = stockId,
                        Type = SignalType.Sell,
                        Strength = (decimal)sellCount / _subStrategies.Count,
                        Reason = $"{sellCount}/{_subStrategies.Count}策略卖出信号"
                    });
                }
            }
        }

        return result;
    }
}

/// <summary>
/// 均线多头策略
/// 1. 5日线大于10日线（均线多头）
/// 2. 当天收盘价大于5日线
/// 3. 当天收盘价大于开盘价（阳线）
/// 4. 最近5天至少有两天量能大于120日量能均线3倍
/// 5. 最近20天没有涨停
/// 6. 当天收盘价不能是最近5日收盘价的最高价
/// 7. 当日收盘价不能便宜5日线太远（价差不超过3%）
/// 8. 当日的收盘价不能有长上影线（上影线长度不超过实体长度的30%）
/// </summary>
public class MultiMovingAverageStrategy : IStrategy
{
    public string Name => "均线多头策略";
    public string StrategyType => "MultiMovingAverage";
    public Dictionary<string, object> Parameters { get; set; } = new()
    {
        ["ShortPeriod"] = 5,
        ["MediumPeriod"] = 10,
        ["VolumeMaPeriod"] = 120,
        ["VolumeMultiplier"] = 3.0,
        ["CheckDays"] = 5,
        ["RequiredExpansionDays"] = 2,
        ["NoLimitUpDays"] = 20,
        ["LimitUpPercent"] = 9.95m  // 涨停阈值（9.95%及以上视为涨停）
    };

    public List<Signal> GenerateSignals(string stockId, List<StockDailyData> dailyData, List<StockDailyIndicator> indicators)
    {
        var result = new List<Signal>();

        if (dailyData.Count < Math.Max(
            StrategyParameterHelper.GetIntValue(Parameters["MediumPeriod"]),
            StrategyParameterHelper.GetIntValue(Parameters["VolumeMaPeriod"])))
        {
            return result;
        }

        var shortPeriod = StrategyParameterHelper.GetIntValue(Parameters["ShortPeriod"]);
        var mediumPeriod = StrategyParameterHelper.GetIntValue(Parameters["MediumPeriod"]);
        var volumeMaPeriod = StrategyParameterHelper.GetIntValue(Parameters["VolumeMaPeriod"]);
        var volumeMultiplier = StrategyParameterHelper.GetDecimalValue(Parameters["VolumeMultiplier"]);
        var checkDays = StrategyParameterHelper.GetIntValue(Parameters["CheckDays"]);
        var requiredExpansionDays = StrategyParameterHelper.GetIntValue(Parameters["RequiredExpansionDays"]);
        var noLimitUpDays = StrategyParameterHelper.GetIntValue(Parameters["NoLimitUpDays"], 20);  // 默认20天
        var limitUpPercent = StrategyParameterHelper.GetDecimalValue(Parameters["LimitUpPercent"], 9.95m);  // 默认9.95%

        for (int i = Math.Max(volumeMaPeriod, mediumPeriod); i < dailyData.Count; i++)
        {
            var currentIndicator = indicators[i];
            var currentData = dailyData[i];

            // 检查均线多头：5日线大于10日线
            if (!currentIndicator.MA5.HasValue || !currentIndicator.MA10.HasValue)
                continue;

            if (currentIndicator.MA5.Value <= currentIndicator.MA10.Value)
                continue;

            // 检查收盘价大于5日线
            if (currentData.ClosePrice <= currentIndicator.MA5.Value)
                continue;

            // 检查当天收盘价大于开盘价（阳线）
            if (currentData.ClosePrice <= currentData.OpenPrice)
                continue;

            // 检查最近N天的量能放大情况
            int expansionDays = 0;
            int startCheckIndex = Math.Max(0, i - checkDays + 1);

            for (int j = startCheckIndex; j <= i; j++)
            {
                if (indicators[j].VolumeMA120.HasValue &&
                    dailyData[j].Volume > indicators[j].VolumeMA120.Value * volumeMultiplier)
                {
                    expansionDays++;
                }
            }

            // 至少有N天量能放大
            if (expansionDays < requiredExpansionDays)
                continue;

            // 检查最近20天没有涨停
            if (i >= noLimitUpDays)
            {
                bool hasLimitUp = false;
                int startLimitUpIndex = Math.Max(0, i - noLimitUpDays);

                for (int j = startLimitUpIndex; j < i; j++)
                {
                    if (dailyData[j].ChangePercent.HasValue && dailyData[j].ChangePercent.Value >= limitUpPercent)
                    {
                        hasLimitUp = true;
                        break;
                    }
                }

                if (hasLimitUp)
                {
                    continue;
                }
            }
            else if (i < noLimitUpDays)
            {
                // 数据不足，无法比较
                continue;
            }

            //// 检查当天收盘价不能是最近5日收盘价的最高价
            //if (i >= checkDays)
            //{
            //    decimal? maxCloseInLast5Days = null;
            //    int closeCheckStartIndex = i - checkDays + 1;

            //    for (int j = closeCheckStartIndex; j <= i; j++)
            //    {
            //        if (!maxCloseInLast5Days.HasValue || dailyData[j].ClosePrice > maxCloseInLast5Days.Value)
            //        {
            //            maxCloseInLast5Days = dailyData[j].ClosePrice;
            //        }
            //    }

            //    if (maxCloseInLast5Days.HasValue && currentData.ClosePrice >= maxCloseInLast5Days.Value)
            //    {
            //        continue;
            //    }
            //}
            //else
            //{
            //    // 数据不足
            //    continue;
            //}

            // 检查当日收盘价不能便宜5日线太远（价差不超过3%）
            if (currentIndicator.MA5.HasValue)
            {
                decimal priceDiffPercent = (currentData.ClosePrice - currentIndicator.MA5.Value) / currentIndicator.MA5.Value * 100;
                if (priceDiffPercent < -3m)
                {
                    continue;
                }
            }

            // 检查当日收盘价不能有长上影线（上影线长度不超过实体长度的30%）
            if (currentData.HighPrice > 0 && currentData.LowPrice > 0 && currentData.ClosePrice > 0)
            {
                decimal bodyLength = Math.Abs(currentData.ClosePrice - currentData.LowPrice);
                decimal upperShadowLength = currentData.HighPrice - currentData.ClosePrice;

                // 如果上影线长度超过实体的30%，则跳过
                if (upperShadowLength > bodyLength * 0.3m)
                {
                    continue;
                }
            }

            // 所有条件满足，生成买入信号
            result.Add(new Signal
            {
                Date = currentData.TradeDate,
                StockId = stockId,
                Type = SignalType.Buy,
                Reason = $"均线多头(MA{shortPeriod}>{mediumPeriod}), 收盘价>{currentIndicator.MA5.Value:F2}, 收盘价>开盘价(阳线), 最近{checkDays}天有{expansionDays}天量能>{volumeMultiplier}倍120日均量, 最近{noLimitUpDays}天无涨停, 非近期新高, 价差合理, 无长上影",
                Strength = 0.8m
            });
        }

        return result;
    }
}

/// <summary>
/// 首板后回落策略（方案 A）
/// 对每个交易日 T：在 [T-N, T) 内自近向远寻找最近一次「首板」（当日达涨停阈值且前一日未达阈值），
/// 仅用首板日至 T 之间的数据（无未来函数）判断 T 是否满足回落条件；每个满足条件的 T 各产出一条买入信号。
/// </summary>
public class FirstBoardPullbackStrategy : IStrategy
{
    public string Name => "首板后回落策略";
    public string StrategyType => "FirstBoardPullback";
    public Dictionary<string, object> Parameters { get; set; } = new()
    {
        ["LimitUpThreshold"] = 9.95m,  // 涨停阈值
        ["PullbackRange"] = 0.03m,     // 收盘价相对首板最低价最大允许偏离（比例），与 MaxDeviation… 取更严
        ["MaxDeviationFromFirstBoardLowPercent"] = 3m, // 相对首板最低价不得超过的百分点（与 PullbackRange 取 min）
        ["MaxDaysAfterLimitUp"] = 10,      // 首板后最大天数（10天内有效）
        ["MinDaysAfterLimitUp"] = 1,        // 首板后最小天数（至少1天后才选）
        ["FirstBoardLookbackDays"] = 30,    // 方案 A：仅在该自然日窗口内寻找最近一次首板
        ["MaxDailyDropPercent"] = 9m        // 信号日跌幅上限（%），当日 ChangePercent 不得低于 -该值
    };

    public List<Signal> GenerateSignals(string stockId, List<StockDailyData> dailyData, List<StockDailyIndicator> indicators)
    {
        var result = new List<Signal>();

        if (dailyData.Count < 10)
            return result;

        var ordered = dailyData.OrderBy(d => d.TradeDate.Date).ThenBy(d => d.ID).ToList();

        var limitUpThreshold = StrategyParameterHelper.GetDecimalValue(Parameters["LimitUpThreshold"], 9.95m);
        var pullbackRange = StrategyParameterHelper.GetDecimalValue(Parameters["PullbackRange"], 0.03m);
        if (!Parameters.TryGetValue("MaxDeviationFromFirstBoardLowPercent", out var maxDevLowPctObj))
            maxDevLowPctObj = 3m;
        var maxDevLowPctPoints = StrategyParameterHelper.GetDecimalValue(maxDevLowPctObj, 3m);
        if (maxDevLowPctPoints <= 0)
            maxDevLowPctPoints = 3m;
        var maxDeviationFromLowRatio = maxDevLowPctPoints / 100m;
        if (pullbackRange <= 0)
            pullbackRange = maxDeviationFromLowRatio;
        // 相对首板最低价：取 PullbackRange（比例）与「百分点上限」中更严者
        var effectiveMaxDeviationFromFirstBoardLow = Math.Min(pullbackRange, maxDeviationFromLowRatio);
        var maxDaysAfterLimitUp = StrategyParameterHelper.GetIntValue(Parameters["MaxDaysAfterLimitUp"], 10);
        var minDaysAfterLimitUp = StrategyParameterHelper.GetIntValue(Parameters["MinDaysAfterLimitUp"], 1);
        var lookbackDays = StrategyParameterHelper.GetIntValue(Parameters["FirstBoardLookbackDays"], 30);
        if (lookbackDays < 1)
            lookbackDays = 1;

        if (!Parameters.TryGetValue("MaxDailyDropPercent", out var maxDropObj))
            maxDropObj = 9m;
        var maxDailyDropPct = StrategyParameterHelper.GetDecimalValue(maxDropObj, 9m);
        if (maxDailyDropPct <= 0)
            maxDailyDropPct = 9m;

        bool IsLimitUpDay(StockDailyData bar) =>
            bar.ChangePercent.HasValue && bar.ChangePercent.Value >= limitUpThreshold;

        bool IsFirstBoardAt(int idx)
        {
            if (idx < 0 || idx >= ordered.Count)
                return false;
            if (!IsLimitUpDay(ordered[idx]))
                return false;
            if (idx == 0)
                return true;
            return !IsLimitUpDay(ordered[idx - 1]);
        }

        // 自 signalIndex-1 起向前，在 [signalDate - lookbackDays, signalDate) 内找最近一次首板下标；无则 -1。
        int FindRecentFirstBoardIndex(int signalIndex)
        {
            if (signalIndex <= 0)
                return -1;

            var signalDate = ordered[signalIndex].TradeDate.Date;
            var oldestAnchorDate = signalDate.AddDays(-lookbackDays);

            for (int j = signalIndex - 1; j >= 0; j--)
            {
                var d = ordered[j].TradeDate.Date;
                if (d < oldestAnchorDate)
                    break;
                if (IsFirstBoardAt(j))
                    return j;
            }

            return -1;
        }

        // 选股口径：一律用日线收盘价（与盘中 CurrentPrice 解耦）
        decimal? GetSettlementPrice(StockDailyData bar) =>
            bar.ClosePrice > 0 ? bar.ClosePrice : null;

        for (int i = 0; i < ordered.Count; i++)
        {
            var data = ordered[i];
            var signalDate = data.TradeDate.Date;

            var anchorIdx = FindRecentFirstBoardIndex(i);
            if (anchorIdx < 0)
                continue;

            var anchorDate = ordered[anchorIdx].TradeDate.Date;
            var firstLimitUpLowPrice = ordered[anchorIdx].LowPrice;
            if (firstLimitUpLowPrice <= 0)
                continue;

            var daysAfterLimitUp = (signalDate - anchorDate).Days;
            if (daysAfterLimitUp < minDaysAfterLimitUp || daysAfterLimitUp > maxDaysAfterLimitUp)
                continue;

            // 首板后至 signal 日（含）的最低收盘价，不使用 T 之后数据
            decimal? lowestCloseAfterBoard = null;
            for (int k = anchorIdx + 1; k <= i; k++)
            {
                var c = GetSettlementPrice(ordered[k]);
                if (!c.HasValue)
                    continue;
                if (!lowestCloseAfterBoard.HasValue || c < lowestCloseAfterBoard.Value)
                    lowestCloseAfterBoard = c;
            }

            if (!lowestCloseAfterBoard.HasValue)
                continue;

            // 条件1：当天为跌（涨幅 < 0）
            if (!data.ChangePercent.HasValue || data.ChangePercent.Value >= 0)
                continue;

            // 条件1b：当日跌幅不超过上限（默认 9%，即 ChangePercent >= -9）
            if (data.ChangePercent.Value < -maxDailyDropPct)
                continue;

            // 条件2：当日最低价不破首板当日最低价
            if (data.LowPrice < firstLimitUpLowPrice)
                continue;

            // 条件3：收盘价为「首板后～当日」区间内的最低收盘之一（与旧版一致：收盘价不得高于该区间最低收盘）
            var currentSettle = GetSettlementPrice(data);
            if (!currentSettle.HasValue)
                continue;
            if (currentSettle.Value > lowestCloseAfterBoard.Value)
                continue;

            var deviation = (currentSettle.Value - firstLimitUpLowPrice) / firstLimitUpLowPrice;
            if (deviation < 0 || deviation > effectiveMaxDeviationFromFirstBoardLow)
                continue;

            var daysFactor = 1.0m - ((decimal)daysAfterLimitUp / (decimal)maxDaysAfterLimitUp) * 0.5m;
            var strength = Math.Max(0.5m, daysFactor);

            result.Add(new Signal
            {
                Date = data.TradeDate,
                StockId = stockId,
                Type = SignalType.Buy,
                Reason =
                    $"首板后回落(近{lookbackDays}日内首板), 首板日期:{anchorDate:yyyy-MM-dd}, 首板最低价:{firstLimitUpLowPrice:F2}, 距首板{daysAfterLimitUp}天, 当日涨幅:{data.ChangePercent.Value:F2}%, 收盘价:{currentSettle.Value:F2}, 偏差:{deviation * 100:F2}%(上限{effectiveMaxDeviationFromFirstBoardLow * 100:F2}%), 收盘价为首板后至当日最低收盘",
                Strength = strength
            });
        }

        return result;
    }
}

