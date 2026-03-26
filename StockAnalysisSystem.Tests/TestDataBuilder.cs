using StockAnalysisSystem.Core.Entities;

namespace StockAnalysisSystem.Tests;

/// <summary>
/// 测试数据构建辅助类
/// </summary>
public static class TestDataBuilder
{
    /// <summary>
    /// 创建股票日线数据
    /// </summary>
    /// <param name="count">数据条数</param>
    /// <param name="stockCode">股票代码</param>
    /// <param name="startDate">起始日期</param>
    /// <param name="basePrice">基准价格</param>
    /// <returns>日线数据列表</returns>
    public static List<StockDailyData> CreateDailyData(
        int count,
        string stockCode = "000001",
        DateTime? startDate = null,
        decimal basePrice = 100.0m)
    {
        var data = new List<StockDailyData>();
        var currentDate = startDate ?? DateTime.Now.AddDays(-count);

        for (int i = 0; i < count; i++)
        {
            var random = new Random(i);
            var price = basePrice + (decimal)(random.NextDouble() - 0.5) * 10m;
            var high = price * (1 + (decimal)(random.NextDouble() * 0.05));
            var low = price * (1 - (decimal)(random.NextDouble() * 0.05));
            var open = low + (high - low) * (decimal)random.NextDouble();
            var volume = 1000000 + random.Next(0, 9000000);

            data.Add(new StockDailyData
            {
                StockID = Guid.NewGuid().ToString(),
                StockCode = stockCode,
                TradeDate = currentDate.AddDays(i),
                OpenPrice = Math.Round(open, 2),
                ClosePrice = Math.Round(price, 2),
                HighPrice = Math.Round(high, 2),
                LowPrice = Math.Round(low, 2),
                Volume = volume,
                Amount = Math.Round(volume * price, 2),
                ChangePercent = Math.Round((decimal)(random.NextDouble() - 0.5) * 10, 4)
            });
        }

        return data;
    }

    /// <summary>
    /// 创建技术指标数据
    /// </summary>
    /// <param name="count">数据条数</param>
    /// <param name="stockCode">股票代码</param>
    /// <param name="startDate">起始日期</param>
    /// <returns>指标数据列表</returns>
    public static List<StockDailyIndicator> CreateIndicators(
        int count,
        string stockCode = "000001",
        DateTime? startDate = null)
    {
        var indicators = new List<StockDailyIndicator>();
        var currentDate = startDate ?? DateTime.Now.AddDays(-count);
        var random = new Random();

        for (int i = 0; i < count; i++)
        {
            indicators.Add(new StockDailyIndicator
            {
                StockId = Guid.NewGuid().ToString(),
                TradeDate = currentDate.AddDays(i),
                MA5 = 100 + (decimal)(random.NextDouble() * 10),
                MA10 = 100 + (decimal)(random.NextDouble() * 10),
                MA20 = 100 + (decimal)(random.NextDouble() * 10),
                RSI6 = 30 + (decimal)(random.NextDouble() * 40),
                RSI12 = 30 + (decimal)(random.NextDouble() * 40),
                VolumeMA5 = 5000000 + (decimal)(random.NextDouble() * 10000000),
                VolumeMA10 = 5000000 + (decimal)(random.NextDouble() * 10000000)
            });
        }

        return indicators;
    }

    /// <summary>
    /// 创建测试策略
    /// </summary>
    /// <param name="name">策略名称</param>
    /// <param name="strategyType">策略类型</param>
    /// <param name="parameters">策略参数</param>
    /// <returns>策略实体</returns>
    public static Strategy CreateTestStrategy(
        string name = "TestStrategy",
        string strategyType = "MovingAverageCross",
        Dictionary<string, object>? parameters = null)
    {
        return new Strategy
        {
            Name = name,
            Description = "测试策略",
            StrategyType = strategyType,
            Parameters = System.Text.Json.JsonSerializer.Serialize(parameters ?? new Dictionary<string, object>
            {
                { "ShortPeriod", 5 },
                { "LongPeriod", 20 }
            }),
            IsActive = true
        };
    }

    /// <summary>
    ///创建回测任务
    /// </summary>
    /// <param name="strategyId">策略ID</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <returns>回测任务实体</returns>
    public static BacktestTask CreateBacktestTask(
        int strategyId = 1,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        return new BacktestTask
        {
            StrategyId = strategyId,
            StartDate = startDate ?? DateTime.Now.AddMonths(-6),
            EndDate = endDate ?? DateTime.Now,
            InitialCapital = 100000m,
            Status = "Pending"
        };
    }

    /// <summary>
    /// 创建已知价格序列的数据（用于验证计算结果）
    /// </summary>
    /// <param name="prices">价格序列</param>
    /// <param name="stockCode">股票代码</param>
    /// <returns>日线数据列表</returns>
    public static List<StockDailyData> CreateDailyDataFromPrices(
        decimal[] prices,
        string stockCode = "000001")
    {
        var data = new List<StockDailyData>();
        var startDate = DateTime.Now.AddDays(-prices.Length);

        for (int i = 0; i < prices.Length; i++)
        {
            var random = new Random(i);
            var price = prices[i];
            var high = price * (1 + (decimal)(random.NextDouble() * 0.03));
            var low = price * (1 - (decimal)(random.NextDouble() * 0.03));
            var open = low + (high - low) * (decimal)random.NextDouble();

            data.Add(new StockDailyData
            {
                StockID = Guid.NewGuid().ToString(),
                StockCode = stockCode,
                TradeDate = startDate.AddDays(i),
                OpenPrice = Math.Round(open, 2),
                ClosePrice = Math.Round(price, 2),
                HighPrice = Math.Round(high, 2),
                LowPrice = Math.Round(low, 2),
                Volume = 1000000,
                Amount = Math.Round(1000000 * price, 2)
            });
        }

        return data;
    }
}
