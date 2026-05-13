using System.Globalization;
using StockAnalysisSystem.Core.RealtimeData;
using StockAnalysisSystem.Core.Services;

namespace StockAnalysisSystem.Tests;

public sealed class LimitUpMinuteQualityAnalyzerTests
{
    [Fact]
    public void Score_WithSyntheticTrendingBars_ReturnsReasonableRange()
    {
        var list = new List<MinuteChartData>();
        var baseP = 10m;
        var t0 = new DateTime(2026, 1, 2, 9, 30, 0);
        for (var i = 0; i < 40; i++)
        {
            var c = baseP + i * 0.01m;
            var avg = c - 0.005m;
            var dt = t0.AddMinutes(i);
            list.Add(new MinuteChartData
            {
                Time = dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Open = baseP,
                High = c + 0.02m,
                Low = baseP - 0.02m,
                Close = c,
                Volume = 1000 + i * 10,
                Amount = (1000 + i * 10m) * c,
                AvgPrice = avg,
                MinutesFromStart = i
            });
        }

        var (score, summary) = LimitUpMinuteQualityAnalyzer.Score(list, 10m, 10.5m, 9.9m, new DateTime(2026, 1, 2));
        Assert.InRange(score, 40m, 100m);
        Assert.Contains("MA5", summary);
        Assert.Contains("VWAP", summary);
        Assert.Contains("有量阳", summary);
    }
}
