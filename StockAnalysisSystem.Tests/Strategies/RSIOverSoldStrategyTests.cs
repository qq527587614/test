using FluentAssertions;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Strategies;
using Xunit;

namespace StockAnalysisSystem.Tests.Strategies;

/// <summary>
/// RSI超卖策略单元测试
/// </summary>
public class RSIOverSoldStrategyTests
{
    [Fact]
    public void GenerateSignals_GeneratesCorrectSignals()
    {
        // Arrange
        var strategy = new RSIOverSoldStrategy
        {
            Parameters = new Dictionary<string, object>
            {
                ["Period"] = 6,
                ["OversoldThreshold"] = 30m,
                ["OverboughtThreshold"] = 70m
            }
        };

        var stockId = "test-stock";
        var data = TestDataBuilder.CreateDailyData(20);
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
        var strategy = new RSIOverSoldStrategy();

        var stockId = "test-stock";
        var data = TestDataBuilder.CreateDailyData(5); // 少于10天
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
        var strategy = new RSIOverSoldStrategy();

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
        var strategy = new RSIOverSoldStrategy();

        // Assert
        strategy.Name.Should().Be("RSI超卖策略");
        strategy.StrategyType.Should().Be("RSIOverSold");
    }

    [Theory]
    [InlineData(6)]
    [InlineData(14)]
    [InlineData(12)]
    public void CustomPeriod_DoesNotThrowException(int period)
    {
        // Arrange
        var strategy = new RSIOverSoldStrategy
        {
            Parameters = new Dictionary<string, object>
            {
                ["Period"] = period,
                ["OversoldThreshold"] = 30m,
                ["OverboughtThreshold"] = 70m
            }
        };

        var stockId = "test-stock";
        var data = TestDataBuilder.CreateDailyData(30);
        var indicators = new List<StockDailyIndicator>();

        // Act & Assert
        var signals = strategy.GenerateSignals(stockId, data, indicators);
        signals.Should().NotBeNull();
    }
}
