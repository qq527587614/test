using FluentAssertions;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Strategies.Rules;
using Xunit;

namespace StockAnalysisSystem.Tests.Strategies.Rules;

public class RuleNodesTests
{
    private static RuleContext Ctx(
        decimal close,
        decimal? changePct = null,
        decimal? ma5 = null,
        decimal? ma10 = null,
        decimal? prevClose = null,
        decimal? prevMa5 = null,
        decimal? prevMa10 = null)
    {
        var bar = new StockDailyData
        {
            StockID = "sid",
            StockCode = "000001",
            TradeDate = new DateTime(2024, 1, 2),
            OpenPrice = close,
            ClosePrice = close,
            HighPrice = close,
            LowPrice = close,
            Volume = 1,
            Amount = 1,
            ChangePercent = changePct
        };

        StockDailyData? prevBar = null;
        if (prevClose.HasValue)
        {
            prevBar = new StockDailyData
            {
                StockID = "sid",
                StockCode = "000001",
                TradeDate = new DateTime(2024, 1, 1),
                OpenPrice = prevClose.Value,
                ClosePrice = prevClose.Value,
                HighPrice = prevClose.Value,
                LowPrice = prevClose.Value,
                Volume = 1,
                Amount = 1,
                ChangePercent = null
            };
        }

        StockDailyIndicator? ind = null;
        if (ma5.HasValue || ma10.HasValue)
        {
            ind = new StockDailyIndicator
            {
                StockId = "sid",
                TradeDate = bar.TradeDate,
                MA5 = ma5,
                MA10 = ma10
            };
        }

        StockDailyIndicator? prevInd = null;
        if (prevMa5.HasValue || prevMa10.HasValue)
        {
            prevInd = new StockDailyIndicator
            {
                StockId = "sid",
                TradeDate = prevBar?.TradeDate ?? bar.TradeDate.AddDays(-1),
                MA5 = prevMa5,
                MA10 = prevMa10
            };
        }

        var bars = prevBar != null
            ? new List<StockDailyData> { prevBar, bar }
            : new List<StockDailyData> { bar };
        var barIndex = bars.Count - 1;

        return new RuleContext
        {
            StockId = "sid",
            TradeDate = bar.TradeDate.Date,
            Bars = bars,
            BarIndex = barIndex,
            Bar = bar,
            PrevBar = prevBar,
            Indicator = ind,
            PrevIndicator = prevInd
        };
    }

    [Fact]
    public void CompareNode_WorksOnPriceField()
    {
        var node = new CompareNode { Left = ValueSource.ClosePrice, Op = CompareOp.GreaterThan, RightValue = 10m };
        node.Evaluate(Ctx(close: 10.1m)).Should().BeTrue();
        node.Evaluate(Ctx(close: 10m)).Should().BeFalse();
    }

    [Fact]
    public void CrossOverNode_DetectsCrossover()
    {
        var node = new CrossOverNode { Fast = ValueSource.MA5, Slow = ValueSource.MA10 };

        // prev fast <= prev slow, now fast > slow
        node.Evaluate(Ctx(close: 1m, ma5: 10.1m, ma10: 10m, prevClose: 1m, prevMa5: 9.9m, prevMa10: 10m))
            .Should().BeTrue();

        node.Evaluate(Ctx(close: 1m, ma5: 10m, ma10: 10m, prevClose: 1m, prevMa5: 9.9m, prevMa10: 10m))
            .Should().BeFalse();
    }
}

