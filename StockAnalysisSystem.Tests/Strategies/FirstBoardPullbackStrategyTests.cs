using FluentAssertions;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Strategies;
using Xunit;

namespace StockAnalysisSystem.Tests.Strategies;

public class FirstBoardPullbackStrategyTests
{
    private static StockDailyData Bar(
        DateTime tradeDate,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal? changePercent,
        string stockId = "sid-1",
        string stockCode = "000001",
        decimal? currentPrice = null)
    {
        return new StockDailyData
        {
            StockID = stockId,
            StockCode = stockCode,
            TradeDate = tradeDate,
            OpenPrice = open,
            HighPrice = high,
            LowPrice = low,
            ClosePrice = close,
            CurrentPrice = currentPrice ?? close,
            Volume = 1_000_000m,
            Amount = close * 1_000_000m,
            ChangePercent = changePercent
        };
    }

    [Fact]
    public void GenerateSignals_UsesMostRecentFirstBoardInsideLookback_NotEarliestInSeries()
    {
        var day = new DateTime(2024, 6, 3);
        var data = new List<StockDailyData>
        {
            Bar(day.AddDays(0), 10, 10.2m, 9.9m, 10m, 1m),
            Bar(day.AddDays(1), 10, 10.2m, 9.9m, 10.1m, 1m),
            Bar(day.AddDays(2), 10, 10.2m, 9.9m, 10.1m, 1m),
            // 较早首板：前一日非涨停
            Bar(day.AddDays(3), 10, 11m, 10m, 10.9m, 10m),
            Bar(day.AddDays(4), 10.9m, 11m, 10.8m, 10.95m, 0.5m),
            Bar(day.AddDays(5), 10.95m, 11m, 10.9m, 10.98m, 0.3m),
            Bar(day.AddDays(6), 10.98m, 11m, 10.95m, 11m, 0.2m),
            // 更近的首板
            Bar(day.AddDays(7), 11m, 12.2m, 11m, 12m, 10m),
            Bar(day.AddDays(8), 12m, 12m, 11.5m, 11.6m, -3.33m),
            Bar(day.AddDays(9), 11.6m, 11.7m, 11.45m, 11.5m, -0.86m),
            Bar(day.AddDays(10), 11.5m, 11.55m, 11.35m, 11.4m, -0.87m),
            // 回落信号日：收跌、收盘为「首板后～当日」最低收盘、贴近首板最低价 11
            Bar(day.AddDays(11), 11.4m, 11.42m, 11.05m, 11.08m, -2.8m)
        };

        var strategy = new FirstBoardPullbackStrategy
        {
            Parameters = new Dictionary<string, object>
            {
                ["LimitUpThreshold"] = 9.95m,
                ["PullbackRange"] = 0.025m,
                ["MaxDaysAfterLimitUp"] = 10,
                ["MinDaysAfterLimitUp"] = 1,
                ["FirstBoardLookbackDays"] = 30
            }
        };

        var signals = strategy.GenerateSignals("sid-1", data, new List<StockDailyIndicator>());
        var last = signals.Should().ContainSingle().Subject;
        last.Date.Should().Be(day.AddDays(11));
        last.Reason.Should().Contain(day.AddDays(7).ToString("yyyy-MM-dd"));
        last.Reason.Should().NotContain(day.AddDays(3).ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void GenerateSignals_WhenOnlyFirstBoardOutsideLookback_ReturnsNoSignals()
    {
        var day = new DateTime(2024, 1, 1);
        var data = new List<StockDailyData>
        {
            Bar(day.AddDays(0), 10, 11m, 10m, 10.9m, 10m),
            Bar(day.AddDays(1), 10.9m, 11m, 10.8m, 10.95m, 0.5m)
        };
        for (int i = 2; i <= 20; i++)
        {
            data.Add(Bar(day.AddDays(i), 10.95m, 11m, 10.9m, 10.98m, 0.1m * (i % 3)));
        }

        // 仅在很短的窗口内找首板：唯一首板在 day0，对末日而言已落在窗口外
        var strategy = new FirstBoardPullbackStrategy
        {
            Parameters = new Dictionary<string, object>
            {
                ["LimitUpThreshold"] = 9.95m,
                ["PullbackRange"] = 0.05m,
                ["MaxDaysAfterLimitUp"] = 25,
                ["MinDaysAfterLimitUp"] = 1,
                ["FirstBoardLookbackDays"] = 5
            }
        };

        var signals = strategy.GenerateSignals("sid-1", data, new List<StockDailyIndicator>());
        signals.Should().BeEmpty();
    }

    [Fact]
    public void GenerateSignals_RespectsMinDaysAfterLimitUp()
    {
        var day = new DateTime(2024, 7, 1);
        var data = new List<StockDailyData>
        {
            Bar(day.AddDays(0), 10, 10.2m, 9.9m, 10m, 1m),
            Bar(day.AddDays(1), 10, 11m, 10m, 10.9m, 10m),
            // 首板次日即尝试贴近（min=2 时应被过滤）
            Bar(day.AddDays(2), 10.9m, 10.95m, 10.85m, 10.88m, -0.2m)
        };

        var strategy = new FirstBoardPullbackStrategy
        {
            Parameters = new Dictionary<string, object>
            {
                ["LimitUpThreshold"] = 9.95m,
                ["PullbackRange"] = 0.05m,
                ["MaxDaysAfterLimitUp"] = 10,
                ["MinDaysAfterLimitUp"] = 2,
                ["FirstBoardLookbackDays"] = 30
            }
        };

        var signals = strategy.GenerateSignals("sid-1", data, new List<StockDailyIndicator>());
        signals.Should().BeEmpty();
    }

    [Fact]
    public void GenerateSignals_DailyDropMustNotExceedMaxDailyDropPercent()
    {
        var day = new DateTime(2024, 6, 3);
        var data = new List<StockDailyData>
        {
            Bar(day.AddDays(0), 10, 10.2m, 9.9m, 10m, 1m),
            Bar(day.AddDays(1), 10, 10.2m, 9.9m, 10.1m, 1m),
            Bar(day.AddDays(2), 10, 10.2m, 9.9m, 10.1m, 1m),
            Bar(day.AddDays(3), 10, 11m, 10m, 10.9m, 10m),
            Bar(day.AddDays(4), 10.9m, 11m, 10.8m, 10.95m, 0.5m),
            Bar(day.AddDays(5), 10.95m, 11m, 10.9m, 10.98m, 0.3m),
            Bar(day.AddDays(6), 10.98m, 11m, 10.95m, 11m, 0.2m),
            Bar(day.AddDays(7), 11m, 12.2m, 11m, 12m, 10m),
            Bar(day.AddDays(8), 12m, 12m, 11.5m, 11.6m, -3.33m),
            Bar(day.AddDays(9), 11.6m, 11.7m, 11.45m, 11.5m, -0.86m),
            Bar(day.AddDays(10), 11.5m, 11.55m, 11.35m, 11.4m, -0.87m),
            Bar(day.AddDays(11), 11.4m, 11.42m, 11.05m, 11.08m, -2.8m)
        };

        var baseParams = new Dictionary<string, object>
        {
            ["LimitUpThreshold"] = 9.95m,
            ["PullbackRange"] = 0.025m,
            ["MaxDaysAfterLimitUp"] = 10,
            ["MinDaysAfterLimitUp"] = 1,
            ["FirstBoardLookbackDays"] = 30
        };

        var strict = new FirstBoardPullbackStrategy
        {
            Parameters = new Dictionary<string, object>(baseParams) { ["MaxDailyDropPercent"] = 2m }
        };
        strict.GenerateSignals("sid-1", data, new List<StockDailyIndicator>()).Should().BeEmpty();

        var loose = new FirstBoardPullbackStrategy
        {
            Parameters = new Dictionary<string, object>(baseParams) { ["MaxDailyDropPercent"] = 3m }
        };
        loose.GenerateSignals("sid-1", data, new List<StockDailyIndicator>()).Should().ContainSingle();
    }

    [Fact]
    public void GenerateSignals_ExcludesWhenCloseTooFarAboveFirstBoardLow_PercentCap()
    {
        var day = new DateTime(2024, 6, 3);
        var data = new List<StockDailyData>
        {
            Bar(day.AddDays(0), 10, 10.2m, 9.9m, 10m, 1m),
            Bar(day.AddDays(1), 10, 10.2m, 9.9m, 10.1m, 1m),
            Bar(day.AddDays(2), 10, 10.2m, 9.9m, 10.1m, 1m),
            Bar(day.AddDays(3), 10, 11m, 10m, 10.9m, 10m),
            Bar(day.AddDays(4), 10.9m, 11m, 10.8m, 10.95m, 0.5m),
            Bar(day.AddDays(5), 10.95m, 11m, 10.9m, 10.98m, 0.3m),
            Bar(day.AddDays(6), 10.98m, 11m, 10.95m, 11m, 0.2m),
            Bar(day.AddDays(7), 11m, 12.2m, 11m, 12m, 10m),
            Bar(day.AddDays(8), 12m, 12m, 11.5m, 11.6m, -3.33m),
            Bar(day.AddDays(9), 11.6m, 11.7m, 11.45m, 11.5m, -0.86m),
            Bar(day.AddDays(10), 11.5m, 11.55m, 11.35m, 11.4m, -0.87m),
            // 仍为首板后区间最低收盘，但收盘相对首板低 11 偏离约 3.09% > 3%
            Bar(day.AddDays(11), 11.4m, 11.42m, 11.05m, 11.34m, -0.53m)
        };

        var baseParams = new Dictionary<string, object>
        {
            ["LimitUpThreshold"] = 9.95m,
            ["PullbackRange"] = 0.05m,
            ["MaxDeviationFromFirstBoardLowPercent"] = 3m,
            ["MaxDaysAfterLimitUp"] = 10,
            ["MinDaysAfterLimitUp"] = 1,
            ["FirstBoardLookbackDays"] = 30
        };

        new FirstBoardPullbackStrategy { Parameters = new Dictionary<string, object>(baseParams) }
            .GenerateSignals("sid-1", data, new List<StockDailyIndicator>())
            .Should().BeEmpty();

        // 有效上限 = min(PullbackRange, MaxDeviation%)，取 PullbackRange=3.5% 以免 signal 日出现在 day+10（偏离约 3.64%）
        var looser = new FirstBoardPullbackStrategy
        {
            Parameters = new Dictionary<string, object>(baseParams)
            {
                ["MaxDeviationFromFirstBoardLowPercent"] = 4m,
                ["PullbackRange"] = 0.035m
            }
        };
        looser.GenerateSignals("sid-1", data, new List<StockDailyIndicator>()).Should().ContainSingle();
    }

    /// <summary>
    /// 回落判断与「最低收盘」比较均以 ClosePrice 为准；CurrentPrice 偏高时不应误杀信号。
    /// </summary>
    [Fact]
    public void GenerateSignals_UsesClosePrice_NotCurrentPriceForPullbackComparison()
    {
        var day = new DateTime(2024, 6, 3);
        var data = new List<StockDailyData>
        {
            Bar(day.AddDays(0), 10, 10.2m, 9.9m, 10m, 1m),
            Bar(day.AddDays(1), 10, 10.2m, 9.9m, 10.1m, 1m),
            Bar(day.AddDays(2), 10, 10.2m, 9.9m, 10.1m, 1m),
            Bar(day.AddDays(3), 10, 11m, 10m, 10.9m, 10m),
            Bar(day.AddDays(4), 10.9m, 11m, 10.8m, 10.95m, 0.5m),
            Bar(day.AddDays(5), 10.95m, 11m, 10.9m, 10.98m, 0.3m),
            Bar(day.AddDays(6), 10.98m, 11m, 10.95m, 11m, 0.2m),
            Bar(day.AddDays(7), 11m, 12.2m, 11m, 12m, 10m),
            Bar(day.AddDays(8), 12m, 12m, 11.5m, 11.6m, -3.33m),
            Bar(day.AddDays(9), 11.6m, 11.7m, 11.45m, 11.5m, -0.86m),
            Bar(day.AddDays(10), 11.5m, 11.55m, 11.35m, 11.4m, -0.87m),
            // 收盘价仍为区间最低且满足回落；CurrentPrice 若参与比较会高于区间最低收盘而被误过滤
            Bar(day.AddDays(11), 11.4m, 11.42m, 11.05m, 11.0m, -2.8m, currentPrice: 11.5m)
        };

        var strategy = new FirstBoardPullbackStrategy
        {
            Parameters = new Dictionary<string, object>
            {
                ["LimitUpThreshold"] = 9.95m,
                ["PullbackRange"] = 0.025m,
                ["MaxDaysAfterLimitUp"] = 10,
                ["MinDaysAfterLimitUp"] = 1,
                ["FirstBoardLookbackDays"] = 30
            }
        };

        var signals = strategy.GenerateSignals("sid-1", data, new List<StockDailyIndicator>());
        signals.Should().ContainSingle();
        signals[0].Reason.Should().Contain("收盘价:11.00");
    }
}
