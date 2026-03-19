using System.Text.Json;
using Microsoft.Extensions.Logging;
using StockAnalysisSystem.Core.Backtest;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.Strategies;

namespace StockAnalysisSystem.Core.Optimization;

/// <summary>
/// 参数优化器
/// </summary>
public class ParameterOptimizer
{
    private readonly BacktestEngine _backtestEngine;
    private readonly IStockRepository _stockRepo;
    private readonly IOptimizationTaskRepository _optimizationTaskRepo;
    private readonly ILogger<ParameterOptimizer>? _logger;

    public ParameterOptimizer(
        BacktestEngine backtestEngine,
        IStockRepository stockRepo,
        IOptimizationTaskRepository optimizationTaskRepo,
        ILogger<ParameterOptimizer>? logger = null)
    {
        _backtestEngine = backtestEngine;
        _stockRepo = stockRepo;
        _optimizationTaskRepo = optimizationTaskRepo;
        _logger = logger;
    }

    /// <summary>
    /// 网格搜索优化
    /// </summary>
    public async Task<OptimizationResult> GridSearchAsync(
        string strategyType,
        Dictionary<string, ParameterRange> parameterRanges,
        DateTime startDate,
        DateTime endDate,
        FitnessFunction fitnessFunction = FitnessFunction.AnnualReturn,
        BacktestSettings? backtestSettings = null,
        IProgress<OptimizationProgress>? progress = null)
    {
        var result = new OptimizationResult
        {
            StartTime = DateTime.Now
        };

        try
        {
            // 生成所有参数组合
            var parameterCombinations = GenerateParameterCombinations(parameterRanges);
            result.TotalIterations = parameterCombinations.Count;

            _logger?.LogInformation("开始网格搜索，共{Count}组参数", parameterCombinations.Count);

            var iteration = 0;
            foreach (var parameters in parameterCombinations)
            {
                iteration++;
                
                try
                {
                    // 创建策略实例
                    var strategy = StrategyFactory.Create(strategyType, parameters);
                    if (strategy == null)
                    {
                        _logger?.LogWarning("无法创建策略: {StrategyType}", strategyType);
                        continue;
                    }

                    // 执行回测
                    var backtestResult = await _backtestEngine.RunAsync(
                        strategy, startDate, endDate, backtestSettings);

                    // 计算适应度
                    var fitness = CalculateFitness(backtestResult, fitnessFunction);

                    // 记录迭代结果
                    var record = new IterationRecord
                    {
                        Iteration = iteration,
                        Parameters = new Dictionary<string, object>(parameters),
                        Fitness = fitness,
                        TotalReturn = backtestResult.TotalReturn,
                        MaxDrawdown = backtestResult.MaxDrawdown,
                        SharpeRatio = backtestResult.SharpeRatio,
                        WinRate = backtestResult.WinRate
                    };
                    result.Iterations.Add(record);

                    // 更新最优结果
                    if (fitness > result.BestFitness)
                    {
                        result.BestFitness = fitness;
                        result.BestParameters = new Dictionary<string, object>(parameters);
                        result.BestBacktestResult = backtestResult;
                    }

                    progress?.Report(new OptimizationProgress
                    {
                        CurrentIteration = iteration,
                        TotalIterations = parameterCombinations.Count,
                        BestFitness = (double)result.BestFitness,
                        Message = $"迭代 {iteration}/{parameterCombinations.Count}, 最优: {result.BestFitness:F4}"
                    });
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "参数组合迭代失败: {Params}", JsonSerializer.Serialize(parameters));
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "网格搜索优化失败");
        }
        finally
        {
            result.EndTime = DateTime.Now;
        }

        return result;
    }

    /// <summary>
    /// 随机搜索优化
    /// </summary>
    public async Task<OptimizationResult> RandomSearchAsync(
        string strategyType,
        Dictionary<string, ParameterRange> parameterRanges,
        DateTime startDate,
        DateTime endDate,
        int iterations = 100,
        FitnessFunction fitnessFunction = FitnessFunction.AnnualReturn,
        BacktestSettings? backtestSettings = null,
        IProgress<OptimizationProgress>? progress = null)
    {
        var result = new OptimizationResult
        {
            StartTime = DateTime.Now,
            TotalIterations = iterations
        };

        var random = new Random();

        for (int i = 1; i <= iterations; i++)
        {
            try
            {
                // 随机生成参数
                var parameters = GenerateRandomParameters(parameterRanges, random);

                var strategy = StrategyFactory.Create(strategyType, parameters);
                if (strategy == null) continue;

                var backtestResult = await _backtestEngine.RunAsync(
                    strategy, startDate, endDate, backtestSettings);

                var fitness = CalculateFitness(backtestResult, fitnessFunction);

                var record = new IterationRecord
                {
                    Iteration = i,
                    Parameters = new Dictionary<string, object>(parameters),
                    Fitness = fitness,
                    TotalReturn = backtestResult.TotalReturn,
                    MaxDrawdown = backtestResult.MaxDrawdown,
                    SharpeRatio = backtestResult.SharpeRatio,
                    WinRate = backtestResult.WinRate
                };
                result.Iterations.Add(record);

                if (fitness > result.BestFitness)
                {
                    result.BestFitness = fitness;
                    result.BestParameters = new Dictionary<string, object>(parameters);
                    result.BestBacktestResult = backtestResult;
                }

                progress?.Report(new OptimizationProgress
                {
                    CurrentIteration = i,
                    TotalIterations = iterations,
                    BestFitness = (double)result.BestFitness,
                    Message = $"随机迭代 {i}/{iterations}, 最优: {result.BestFitness:F4}"
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "随机搜索迭代失败: {Iteration}", i);
            }
        }

        result.EndTime = DateTime.Now;
        return result;
    }

    /// <summary>
    /// 生成所有参数组合
    /// </summary>
    private List<Dictionary<string, object>> GenerateParameterCombinations(
        Dictionary<string, ParameterRange> parameterRanges)
    {
        var result = new List<Dictionary<string, object>>();
        var keys = parameterRanges.Keys.ToList();
        
        if (keys.Count == 0)
            return result;

        var values = keys.Select(k => parameterRanges[k].GenerateValues()).ToList();
        
        GenerateCombinations(result, new Dictionary<string, object>(), keys, values, 0);
        
        return result;
    }

    private void GenerateCombinations(
        List<Dictionary<string, object>> result,
        Dictionary<string, object> current,
        List<string> keys,
        List<List<object>> values,
        int depth)
    {
        if (depth == keys.Count)
        {
            result.Add(new Dictionary<string, object>(current));
            return;
        }

        foreach (var value in values[depth])
        {
            current[keys[depth]] = value;
            GenerateCombinations(result, current, keys, values, depth + 1);
        }
    }

    /// <summary>
    /// 生成随机参数
    /// </summary>
    private Dictionary<string, object> GenerateRandomParameters(
        Dictionary<string, ParameterRange> parameterRanges,
        Random random)
    {
        var parameters = new Dictionary<string, object>();

        foreach (var kvp in parameterRanges)
        {
            var range = kvp.Value;
            
            if (range.Min is int minInt && range.Max is int maxInt)
            {
                var step = range.Step is int s ? s : 1;
                var steps = (maxInt - minInt) / step;
                parameters[kvp.Key] = minInt + random.Next(steps + 1) * step;
            }
            else if (range.Min is decimal minDec && range.Max is decimal maxDec)
            {
                parameters[kvp.Key] = minDec + (decimal)random.NextDouble() * (maxDec - minDec);
            }
        }

        return parameters;
    }

    /// <summary>
    /// 计算适应度
    /// </summary>
    private decimal CalculateFitness(BacktestResult result, FitnessFunction function)
    {
        return function switch
        {
            FitnessFunction.AnnualReturn => result.AnnualReturn,
            FitnessFunction.SharpeRatio => result.SharpeRatio,
            FitnessFunction.CalmarRatio => result.MaxDrawdown > 0 
                ? result.AnnualReturn / result.MaxDrawdown : 0,
            FitnessFunction.WinRate => result.WinRate,
            FitnessFunction.ProfitFactor => result.ProfitFactor,
            FitnessFunction.Composite => 
                result.AnnualReturn * 0.4m + 
                result.SharpeRatio * 10 * 0.3m + 
                result.WinRate * 0.2m - 
                result.MaxDrawdown * 0.1m,
            _ => result.AnnualReturn
        };
    }
}
