using FluentAssertions;
using StockAnalysisSystem.Core.Optimization;
using Xunit;

namespace StockAnalysisSystem.Tests.Optimization;

/// <summary>
/// 参数范围单元测试
/// </summary>
public class ParameterRangeTests
{
    [Fact]
    public void GenerateValues_IntegerRange_ReturnsCorrectValues()
    {
        // Arrange
        var range = new ParameterRange
        {
            Min = 5,
            Max = 10,
            Step = 1
        };

        // Act
        var values = range.GenerateValues();

        // Assert
        values.Should().HaveCount(6);
        values.Should().ContainInOrder(new object[] { 5, 6, 7, 8, 9, 10 });
    }

    [Fact]
    public void GenerateValues_IntegerRangeWithStep_ReturnsCorrectValues()
    {
        // Arrange
        var range = new ParameterRange
        {
            Min = 5,
            Max = 15,
            Step = 5
        };

        // Act
        var values = range.GenerateValues();

        // Assert
        values.Should().HaveCount(3);
        values.Should().ContainInOrder(new object[] { 5, 10, 15 });
    }

    [Fact]
    public void GenerateValues_DecimalRange_ReturnsCorrectValues()
    {
        // Arrange
        var range = new ParameterRange
        {
            Min = 0.5m,
            Max = 1.0m,
            Step = 0.1m
        };

        // Act
        var values = range.GenerateValues();

        // Assert
        values.Should().HaveCount(6);
        values.Should().ContainInOrder(new object[] { 0.5m, 0.6m, 0.7m, 0.8m, 0.9m, 1.0m });
    }

    [Fact]
    public void GenerateValues_SingleValue_ReturnsSingleValue()
    {
        // Arrange
        var range = new ParameterRange
        {
            Min = 5,
            Max = 5,
            Step = 1
        };

        // Act
        var values = range.GenerateValues();

        // Assert
        values.Should().HaveCount(1);
        values[0].Should().Be(5);
    }

    [Fact]
    public void GenerateValues_MinGreaterThanMax_ReturnsEmpty()
    {
        // Arrange
        var range = new ParameterRange
        {
            Min = 10,
            Max = 5,
            Step = 1
        };

        // Act
        var values = range.GenerateValues();

        // Assert
        values.Should().BeEmpty();
    }

    [Fact]
    public void GenerateValues_NegativeRange_ReturnsCorrectValues()
    {
        // Arrange
        var range = new ParameterRange
        {
            Min = -5,
            Max = 5,
            Step = 2
        };

        // Act
        var values = range.GenerateValues();

        // Assert
        values.Should().HaveCount(6);
        values.Should().ContainInOrder(new object[] { -5, -3, -1, 1, 3, 5 });
    }

    [Theory]
    [InlineData(1, 10, 1)]
    [InlineData(0, 100, 10)]
    [InlineData(5, 50, 5)]
    public void GenerateValues_VariousIntegerRanges_ReturnsCorrectCounts(int min, int max, int step)
    {
        // Arrange
        var range = new ParameterRange
        {
            Min = min,
            Max = max,
            Step = step
        };

        // Act
        var values = range.GenerateValues();

        // Assert
        var expectedCount = (max - min) / step + 1;
        values.Should().HaveCount(expectedCount);
    }

    [Fact]
    public void GenerateValues_DefaultValues_ReturnsDefaultRange()
    {
        // Arrange
        var range = new ParameterRange(); // 使用默认值: Min=0, Max=100, Step=1

        // Act
        var values = range.GenerateValues();

        // Assert
        values.Should().HaveCount(101);
        values.First().Should().Be(0);
        values.Last().Should().Be(100);
    }
}
