using FluentAssertions;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Strategies;
using Xunit;

namespace StockAnalysisSystem.Tests.Strategies;

/// <summary>
/// 组合策略单元测试
/// </summary>
public class CombinedStrategyTests
{
    [Fact]
    public void GenerateSignals_WithSubStrategies_GeneratesSignals()
    {
        // Arrange
        var strategy = new CombinedStrategy
        {
            Parameters = new Dictionary<string, object>
            {
                ["Strategies"] = "MovingAverageCross,MACDCross",
                ["RequireAll"] = true
            }
        };

        var stockId = "test-stock";
        var data = TestDataBuilder.CreateDailyData(50);
        var indicators = new List<StockDailyIndicator>();

        // Act
        var signals = strategy.GenerateSignals(stockId, data, indicators);

        // Assert
        signals.Should().NotBeNull();
    }

    [Fact]
    public void NoStrategies_ReturnsNoSignals()
    {
        // Arrange
        var strategy = new CombinedStrategy
        {
            Parameters = new Dictionary<string, object>
            {
                ["Strategies"] = "",
                ["RequireAll"] = true
            }
        };

        var stockId = "test-stock";
        var data = TestDataBuilder.CreateDailyData(50);
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
        var strategy = new CombinedStrategy();

        // Assert
        strategy.Name.Should().Be("组合策略");
        strategy.StrategyType.Should().Be("Combined");
    }

    [Theory]
    [InlineData("MovingAverageCross")]
    [InlineData("MACDCross")]
    [InlineData("RSIOverSold")]
    [InlineData("MovingAverageCross,MACDCross")]
    public void DifferentStrategyCombinations_GeneratesSignals(string strategies)
    {
        // Arrange
        var strategy = new CombinedStrategy
        {
            Parameters = new Dictionary<string, object>
            {
                ["Strategies"] = strategies,
                ["RequireAll"] = false
            }
        };

        var stockId = "test-stock";
        var data = TestDataBuilder.CreateDailyData(50);
        var indicators = new List<StockDailyIndicator>();

        // Act & Assert
        var signals = strategy.GenerateSignals(stockId, data, indicators);
        signals.Should().NotBeNull();
    }

    [Fact]
    public void EmptyData_ReturnsNoSignals()
    {
        // Arrange
        var strategy = new CombinedStrategy();

        var stockId = "test-stock";
        var data = new List<StockDailyData>();
        var indicators = new List<StockDailyIndicator>();

        // Act
        var signals = strategy.GenerateSignals(stockId, data, indicators);

        // Assert
        signals.Should().BeEmpty();
    }
}
