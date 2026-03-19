namespace StockAnalysisSystem.Core.Optimization;

/// <summary>
/// 参数范围定义
/// </summary>
public class ParameterRange
{
    public object Min { get; set; } = 0;
    public object Max { get; set; } = 100;
    public object Step { get; set; } = 1;

    /// <summary>
    /// 生成所有可能的参数值
    /// </summary>
    public List<object> GenerateValues()
    {
        var values = new List<object>();

        if (Min is int minInt && Max is int maxInt && Step is int stepInt)
        {
            for (int i = minInt; i <= maxInt; i += stepInt)
            {
                values.Add(i);
            }
        }
        else if (Min is decimal minDec && Max is decimal maxDec && Step is decimal stepDec)
        {
            for (decimal i = minDec; i <= maxDec; i += stepDec)
            {
                values.Add(i);
            }
        }
        else if (Min is double minDbl && Max is double maxDbl && Step is double stepDbl)
        {
            for (double i = minDbl; i <= maxDbl; i += stepDbl)
            {
                values.Add((decimal)i);
            }
        }

        return values;
    }
}

/// <summary>
/// 优化进度
/// </summary>
public class OptimizationProgress
{
    public int CurrentIteration { get; set; }
    public int TotalIterations { get; set; }
    public double BestFitness { get; set; }
    public string Message { get; set; } = string.Empty;
    public double Percentage => TotalIterations > 0 ? (double)CurrentIteration / TotalIterations * 100 : 0;
}

/// <summary>
/// 优化结果
/// </summary>
public class OptimizationResult
{
    public Dictionary<string, object> BestParameters { get; set; } = new();
    public decimal BestFitness { get; set; }
    public Backtest.BacktestResult? BestBacktestResult { get; set; }
    public List<IterationRecord> Iterations { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public int TotalIterations { get; set; }
}

/// <summary>
/// 迭代记录
/// </summary>
public class IterationRecord
{
    public int Iteration { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    public decimal Fitness { get; set; }
    public decimal TotalReturn { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal WinRate { get; set; }
}

/// <summary>
/// 适应度函数类型
/// </summary>
public enum FitnessFunction
{
    AnnualReturn,       // 年化收益率
    SharpeRatio,        // 夏普比率
    CalmarRatio,        // 卡玛比率
    WinRate,            // 胜率
    ProfitFactor,       // 盈亏比
    Composite           // 综合评分
}
