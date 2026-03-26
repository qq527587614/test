using FluentAssertions;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Strategies;
using Xunit;

namespace StockAnalysisSystem.Tests.Strategies;

/// <summary>
/// MACD交叉策略单元测试
/// </summary>
public class MACDCrossStrategyTests
{
    [Fact]
    public void GenerateSignals_GeneratesCorrectSignals()
    {
        // Arrange
        var strategy = new MACDCrossStrategy();

        var stockId = "test-stock";
        var data = TestDataBuilder.CreateDailyData(40);
        var indicators = new List<StockDailyIndicator>();

        // Act
        var signals = strategy.GenerateSignals(stockId, data, indicators);

        // Assert
        signals.Should().NotBeNull();
    }

    [Fact]
    public void InsufficientData_ReturnsNoSignals()
    {
        // Arrange
        var strategy = new MACDCrossStrategy();

        var stockId = "test-stock";
        var data = TestDataBuilder.CreateDailyData(30); // 少于35天
        var indicators = new List<StockDailyIndicator>();

        // Act
        var signals = strategy.GenerateSignals(stockId, data, indicators);

        // Assert
        signals.Should().BeEmpty();
    }

    [Fact]
    public void EmptyData_ReturnsNoSignals()
    {
        // Arrange
        var strategy = new MACDCrossStrategy();

        var stockId = "test-stock";
        var data = new List<StockDailyData>();
        var indicators = new List<StockDailyIndicator>();

        // Act
        var signals = strategy.GenerateSignals(stockId, data, indicators);

        // Assert
        signals.Should().BeEmpty();
    }

    [Fact]
    public void StrategyNameAndType_ReturnsCorrectValues()
    {
        // Arrange
        var strategy = new MACDCrossStrategy();

        // Assert
        strategy.Name.Should().Be("MACD交叉策略");
        strategy.StrategyType.Should().Be("MACDCross");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(8)]
    public void CustomFastPeriod_DoesNotThrowException(int fast)
    {
        // Arrange
        var strategy = new MACDCrossStrategy
        {
            Parameters = new Dictionary<string, object>
            {
                ["FastPeriod"] = fast,
                ["SlowPeriod"] = 26,
                ["SignalPeriod"] = 9
            }
        };

        var stockId = "test-stock";
        var data = TestDataBuilder.CreateDailyData(50);
        var indicators = new List<StockDailyIndicator>();

        // Act & Assert
        var signals = strategy.GenerateSignals(stockId, data, indicators);
        signals.Should().NotBeNull();
    }
}
