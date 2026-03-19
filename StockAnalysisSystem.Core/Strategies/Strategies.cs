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
