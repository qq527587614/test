using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core.Common;
using StockAnalysisSystem.Core.Repositories;

namespace StockAnalysisSystem.Core;

/// <summary>
/// 服务集合扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStockAnalysisServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册数据库上下文
        var connectionString = configuration.GetConnectionString("MySql");
        
        // 配置 DbContext 连接池和重试
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), 
                mySqlOptions =>
                {
                    mySqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                    mySqlOptions.CommandTimeout(60);
                });
            options.EnableSensitiveDataLogging(false);
            options.EnableDetailedErrors(true);
        });

        // 注册设置
        services.Configure<DeepSeekSettings>(options => 
            configuration.GetSection("DeepSeek").Bind(options));
        services.Configure<BacktestSettings>(options => 
            configuration.GetSection("Backtest").Bind(options));

        // 注册仓储
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<IStockDailyDataRepository, StockDailyDataRepository>();
        services.AddScoped<IIndicatorRepository, IndicatorRepository>();
        services.AddScoped<IStrategyRepository, StrategyRepository>();
        services.AddScoped<IBacktestTaskRepository, BacktestTaskRepository>();
        services.AddScoped<IOptimizationTaskRepository, OptimizationTaskRepository>();
        services.AddScoped<IDailyPickRepository, DailyPickRepository>();
        services.AddScoped<IDeepSeekLogRepository, DeepSeekLogRepository>();

        // 注册服务
        services.AddScoped<Indicators.IndicatorCalculator>();
        services.AddScoped<Strategies.StrategyFactory>();
        services.AddScoped<Backtest.BacktestEngine>();
        services.AddScoped<Optimization.ParameterOptimizer>();
        services.AddScoped<DailyPick.DailyPicker>();
        services.AddScoped<DeepSeek.DeepSeekClient>();

        return services;
    }
}
