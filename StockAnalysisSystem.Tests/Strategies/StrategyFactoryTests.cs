using FluentAssertions;
using StockAnalysisSystem.Core.Strategies;
using System.Text.Json;
using Xunit;

namespace StockAnalysisSystem.Tests.Strategies;

/// <summary>
/// 策略工厂单元测试
/// </summary>
public class StrategyFactoryTests
{
    [Fact]
    public void Create_MovingAverageCross_ReturnsCorrectType()
    {
        // Arrange
        var strategyType = "MovingAverageCross";
        var parameters = new Dictionary<string, object>
        {
            ["ShortPeriod"] = 5,
            ["LongPeriod"] = 20
        };

        // Act
        var strategy = StrategyFactory.Create(strategyType, parameters);

        // Assert
        strategy.Should().NotBeNull();
        strategy.Should().BeOfType<MovingAverageCrossStrategy>();
        strategy!.StrategyType.Should().Be("MovingAverageCross");
        strategy.Parameters["ShortPeriod"].Should().Be(5);
        strategy.Parameters["LongPeriod"].Should().Be(20);
    }

    [Fact]
    public void Create_MACDCross_ReturnsCorrectType()
    {
        // Arrange
        var strategyType = "MACDCross";
        var parameters = new Dictionary<string, object>();

        // Act
        var strategy = StrategyFactory.Create(strategyType, parameters);

        // Assert
        strategy.Should().NotBeNull();
        strategy.Should().BeOfType<MACDCrossStrategy>();
        strategy!.StrategyType.Should().Be("MACDCross");
    }

    [Fact]
    public void Create_RSIOverSold_ReturnsCorrectType()
    {
        // Arrange
        var strategyType = "RSIOverSold";
        var parameters = new Dictionary<string, object>
        {
            ["Period"] = 6,
            ["OversoldThreshold"] = 30m
        };

        // Act
        var strategy = StrategyFactory.Create(strategyType, parameters);

        // Assert
        strategy.Should().NotBeNull();
        strategy.Should().BeOfType<RSIOverSoldStrategy>();
        strategy!.StrategyType.Should().Be("RSIOverSold");
    }

    [Fact]
    public void Create_Combined_ReturnsCorrectType()
    {
        // Arrange
        var strategyType = "Combined";
        var parameters = new Dictionary<string, object>();

        // Act
        var strategy = StrategyFactory.Create(strategyType, parameters);

        // Assert
        strategy.Should().NotBeNull();
        strategy.Should().BeOfType<CombinedStrategy>();
        strategy!.StrategyType.Should().Be("Combined");
    }

    [Fact]
    public void Create_UnknownType_ReturnsNull()
    {
        // Arrange
        var strategyType = "UnknownStrategy";
        var parameters = new Dictionary<string, object>();

        // Act
        var strategy = StrategyFactory.Create(strategyType, parameters);

        // Assert
        strategy.Should().BeNull();
    }

    [Fact]
    public void CreateFromJson_ValidJson_ReturnsStrategyWithParameters()
    {
        // Arrange
        var strategyType = "MovingAverageCross";
        var jsonParameters = JsonSerializer.Serialize(new
        {
            ShortPeriod = 5,
            LongPeriod = 20
        });

        // Act
        var strategy = StrategyFactory.CreateFromJson(strategyType, jsonParameters);

        // Assert
        strategy.Should().NotBeNull();
        strategy.Should().BeOfType<MovingAverageCrossStrategy>();
        strategy!.Parameters["ShortPeriod"].Should().Be(5);
        strategy.Parameters["LongPeriod"].Should().Be(20);
    }

    [Fact]
    public void CreateFromJson_InvalidJson_ReturnsStrategyWithDefaultParameters()
    {
        // Arrange
        var strategyType = "MovingAverageCross";
        var jsonParameters = "invalid json";

        // Act
        var strategy = StrategyFactory.CreateFromJson(strategyType, jsonParameters);

        // Assert
        strategy.Should().NotBeNull();
        strategy.Should().BeOfType<MovingAverageCrossStrategy>();
        // 应该使用默认参数
        strategy!.Parameters.Should().NotBeEmpty();
    }

    [Fact]
    public void GetSupportedTypes_ReturnsAllSupportedStrategyTypes()
    {
        // Act
        var supportedTypes = StrategyFactory.GetSupportedTypes();

        // Assert
        supportedTypes.Should().NotBeEmpty();
        supportedTypes.Should().Contain("MovingAverageCross");
        supportedTypes.Should().Contain("MACDCross");
        supportedTypes.Should().Contain("RSIOverSold");
        supportedTypes.Should().Contain("Combined");
    }

    [Fact]
    public void GetDefaultParameters_ValidType_ReturnsDefaultParameters()
    {
        // Arrange
        var strategyType = "MovingAverageCross";

        // Act
        var defaultParams = StrategyFactory.GetDefaultParameters(strategyType);

        // Assert
        defaultParams.Should().NotBeNull();
        defaultParams.Should().NotBeEmpty();
        defaultParams.Should().ContainKey("ShortPeriod");
        defaultParams.Should().ContainKey("LongPeriod");
    }

    [Fact]
    public void GetDefaultParameters_UnknownType_ReturnsEmptyDictionary()
    {
        // Arrange
        var strategyType = "UnknownStrategy";

        // Act
        var defaultParams = StrategyFactory.GetDefaultParameters(strategyType);

        // Assert
        defaultParams.Should().NotBeNull();
        defaultParams.Should().BeEmpty();
    }

    [Theory]
    [InlineData("MovingAverageCross", typeof(MovingAverageCrossStrategy))]
    [InlineData("MACDCross", typeof(MACDCrossStrategy))]
    [InlineData("RSIOverSold", typeof(RSIOverSoldStrategy))]
    [InlineData("Combined", typeof(CombinedStrategy))]
    public void Create_AllSupportedTypes_ReturnsCorrectInstances(string strategyType, Type expectedType)
    {
        // Arrange
        var parameters = new Dictionary<string, object>();

        // Act
        var strategy = StrategyFactory.Create(strategyType, parameters);

        // Assert
        strategy.Should().NotBeNull();
        strategy.Should().BeOfType(expectedType);
    }
}
