using System.Text.Json;

namespace StockAnalysisSystem.Core.Strategies;

/// <summary>
/// 策略工厂
/// </summary>
public class StrategyFactory
{
    private static readonly Dictionary<string, Type> _strategyTypes = new()
    {
        ["MovingAverageCross"] = typeof(MovingAverageCrossStrategy),
        ["MACDCross"] = typeof(MACDCrossStrategy),
        ["RSIOverSold"] = typeof(RSIOverSoldStrategy),
        ["Combined"] = typeof(CombinedStrategy),
        ["MultiMovingAverage"] = typeof(MultiMovingAverageStrategy),
        ["FirstBoardPullback"] = typeof(FirstBoardPullbackStrategy)
    };

    /// <summary>
    /// 创建策略实例
    /// </summary>
    public static IStrategy? Create(string strategyType, Dictionary<string, object> parameters)
    {
        if (!_strategyTypes.TryGetValue(strategyType, out var type))
            return null;

        var strategy = Activator.CreateInstance(type) as IStrategy;
        if (strategy != null && parameters.Count > 0)
        {
            strategy.Parameters = parameters;
        }
        return strategy;
    }

    /// <summary>
    /// 从JSON参数创建策略实例
    /// </summary>
    public static IStrategy? CreateFromJson(string strategyType, string jsonParameters)
    {
        try
        {
            // 先反序列化为 JsonElement 字典
            var jsonElements = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonParameters);
            if (jsonElements == null)
                return Create(strategyType, new Dictionary<string, object>());

            // 获取默认参数
            var defaultParams = GetDefaultParameters(strategyType);

            // 将 JsonElement 转换为相应的 CLR 类型，并合并默认参数
            var parameters = new Dictionary<string, object>();
            foreach (var kvp in jsonElements)
            {
                parameters[kvp.Key] = ConvertJsonElement(kvp.Value);
            }

            // 合并默认参数和数据库参数（数据库参数覆盖默认参数）
            foreach (var defaultKvp in defaultParams)
            {
                if (!parameters.ContainsKey(defaultKvp.Key))
                {
                    parameters[defaultKvp.Key] = defaultKvp.Value;
                }
            }

            return Create(strategyType, parameters);
        }
        catch
        {
            return Create(strategyType, new Dictionary<string, object>());
        }
    }

    /// <summary>
    /// 将 JsonElement 转换为相应的 CLR 类型
    /// </summary>
    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.TryGetInt32(out var intValue) ? (object)intValue : element.GetDouble(),
            JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText()) ?? new Dictionary<string, object>(),
            JsonValueKind.Array => JsonSerializer.Deserialize<List<object>>(element.GetRawText()) ?? new List<object>(),
            JsonValueKind.Null => null!,
            _ => element.ToString()
        };
    }

    /// <summary>
    /// 获取所有支持的策略类型
    /// </summary>
    public static List<string> GetSupportedTypes()
    {
        return _strategyTypes.Keys.ToList();
    }

    /// <summary>
    /// 获取策略类型的默认参数
    /// </summary>
    public static Dictionary<string, object> GetDefaultParameters(string strategyType)
    {
        var strategy = Create(strategyType, new Dictionary<string, object>());
        return strategy?.Parameters ?? new Dictionary<string, object>();
    }
}
