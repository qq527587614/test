using StockAnalysisSystem.Core.RealtimeData;

namespace StockAnalysisSystem.Tests;

public sealed class SinaMinuteChartServiceTests
{
    [Fact]
    public async Task GetMinuteChartDataAsync_Shanghai_ShouldReturnBars()
    {
        var svc = new SinaMinuteChartService();
        var (data, err) = await svc.GetMinuteChartDataAsync("600000", 1, 10);
        Assert.True(string.IsNullOrEmpty(err), err);
        Assert.NotEmpty(data);
    }

    [Fact]
    public async Task GetMinuteChartDataAsync_Shenzhen_ShouldReturnBars()
    {
        var svc = new SinaMinuteChartService();
        var (data, err) = await svc.GetMinuteChartDataAsync("000001", 1, 10);
        Assert.True(string.IsNullOrEmpty(err), err);
        Assert.NotEmpty(data);
    }
}
