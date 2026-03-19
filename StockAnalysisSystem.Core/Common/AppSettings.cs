namespace StockAnalysisSystem.Core.Common;

/// <summary>
/// DeepSeek API配置
/// </summary>
public class DeepSeekSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://api.deepseek.com/v1/chat/completions";
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// 回测配置
/// </summary>
public class BacktestSettings
{
    public decimal DefaultInitialCapital { get; set; } = 1000000m;
    public decimal DefaultCommission { get; set; } = 0.00025m;
    public decimal DefaultSlippage { get; set; } = 0.001m;
    public int MaxPositions { get; set; } = 10;
}

/// <summary>
/// 数据库配置
/// </summary>
public class ConnectionStrings
{
    public string MySql { get; set; } = string.Empty;
}
