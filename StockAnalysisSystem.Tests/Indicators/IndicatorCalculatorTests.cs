using FluentAssertions;
using StockAnalysisSystem.Core.Indicators;
using StockAnalysisSystem.Core.Entities;
using Xunit;

namespace StockAnalysisSystem.Tests.Indicators;

/// <summary>
/// 技术指标计算器单元测试
/// </summary>
public class IndicatorCalculatorTests
{
    #region CalculateMA Tests

    [Fact]
    public void CalculateMA_ValidData_ReturnsCorrectResult()
    {
        // Arrange
        var prices = new List<decimal> { 10m, 20m, 30m, 40m, 50m };
        int period = 3;

        // Act
        var result = IndicatorCalculator.CalculateMA(prices, period);

        // Assert
        result.Should().HaveCount(5);
        result[0].Should().BeNull();
        result[1].Should().BeNull();
        result[2].Should().Be(20m); // (10+20+30)/3
        result[3].Should().Be(30m); // (20+30+40)/3
        result[4].Should().Be(40m); // (30+40+50)/3
    }

    [Fact]
    public void CalculateMA_EmptyData_ReturnsEmpty()
    {
        // Arrange
        var prices = new List<decimal>();
        int period = 5;

        // Act
        var result = IndicatorCalculator.CalculateMA(prices, period);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void CalculateMA_PeriodEqualsDataLength_ReturnsSingleValue()
    {
        // Arrange
        var prices = new List<decimal> { 10m, 20m, 30m };
        int period = 3;

        // Act
        var result = IndicatorCalculator.CalculateMA(prices, period);

        // Assert
        result.Should().HaveCount(3);
        result[0].Should().BeNull();
        result[1].Should().BeNull();
        result[2].Should().Be(20m);
    }

    [Fact]
    public void CalculateMA_PeriodOne_ReturnsSameValues()
    {
        // Arrange
        var prices = new List<decimal> { 10m, 20m, 30m, 40m };
        int period = 1;

        // Act
        var result = IndicatorCalculator.CalculateMA(prices, period);

        // Assert
        result.Should().HaveCount(4);
        result.Should().BeEquivalentTo(new List<decimal?> { 10m, 20m, 30m, 40m });
    }

    #endregion

    #region CalculateEMA Tests

    [Fact]
    public void CalculateEMA_ValidData_ReturnsCorrectResult()
    {
        // Arrange
        var prices = new List<decimal> { 10m, 20m, 30m, 40m, 50m, 60m };
        int period = 3;

        // Act
        var result = IndicatorCalculator.CalculateEMA(prices, period);

        // Assert
        result.Should().HaveCount(6);
        result[0].Should().BeNull();
        result[1].Should().BeNull();
        // 第一个EMA是简单平均
        result[2].Should().Be(20m); // (10+20+30)/3
        // 后续EMA使用指数平滑公式
        result[3].Should().NotBeNull();
        result[3].Should().BeGreaterThan(result[2]!.Value); // 价格上涨，EMA应该上涨
    }

    [Fact]
    public void CalculateEMA_EmptyData_ReturnsEmpty()
    {
        // Arrange
        var prices = new List<decimal>();
        int period = 12;

        // Act
        var result = IndicatorCalculator.CalculateEMA(prices, period);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void CalculateEMA_PeriodOne_ReturnsSameValues()
    {
        // Arrange
        var prices = new List<decimal> { 10m, 20m, 30m };
        int period = 1;

        // Act
        var result = IndicatorCalculator.CalculateEMA(prices, period);

        // Assert
        result.Should().HaveCount(3);
        result.Should().BeEquivalentTo(new List<decimal?> { 10m, 20m, 30m });
    }

    #endregion

    #region CalculateMACD Tests

    [Fact]
    public void CalculateMACD_ValidData_ReturnsCorrectResult()
    {
        // Arrange
        var prices = TestDataBuilder.CreateDailyDataFromPrices(
            new decimal[] { 100m, 102m, 104m, 103m, 105m, 107m, 106m, 108m, 110m, 112m, 
                           111m, 113m, 115m, 114m, 116m, 118m, 117m, 119m, 121m, 120m,
                           122m, 124m, 123m, 125m, 127m, 126m, 128m, 130m, 129m, 131m,
                           133m, 135m, 134m, 136m, 138m, 137m, 139m, 141m, 140m, 142m });
        var priceList = prices.Select(p => p.ClosePrice).ToList();

        // Act
        var (dif, dea, macd) = IndicatorCalculator.CalculateMACD(priceList);

        // Assert
        dif.Should().HaveCount(40);
        dea.Should().HaveCount(40);
        macd.Should().HaveCount(40);

        // DIF = 快线EMA(12) - 慢线EMA(26)，前25个为null
        dif.Take(25).All(d => d == null).Should().BeTrue();
        dif[25].Should().NotBeNull();

        // DEA = DIF的EMA(9)，需要DIF有值后再计算，前33个为null (25 + 9 - 1)
        dea.Take(33).All(d => d == null).Should().BeTrue();
        dea[33].Should().NotBeNull();

        // MACD = 2*(DIF-DEA)，前33个为null
        macd.Take(33).All(m => m == null).Should().BeTrue();
        macd[33].Should().NotBeNull();
    }

    [Fact]
    public void CalculateMACD_EmptyData_ReturnsEmpty()
    {
        // Arrange
        var prices = new List<decimal>();

        // Act
        var (dif, dea, macd) = IndicatorCalculator.CalculateMACD(prices);

        // Assert
        dif.Should().BeEmpty();
        dea.Should().BeEmpty();
        macd.Should().BeEmpty();
    }

    [Fact]
    public void CalculateMACD_InsufficientData_ReturnsNulls()
    {
        // Arrange
        var prices = new List<decimal> { 100m, 101m, 102m };

        // Act
        var (dif, dea, macd) = IndicatorCalculator.CalculateMACD(prices);

        // Assert
        dif.Should().HaveCount(3);
        dea.Should().HaveCount(3);
        macd.Should().HaveCount(3);

        dif.All(d => d == null).Should().BeTrue();
        dea.All(d => d == null).Should().BeTrue();
        macd.All(m => m == null).Should().BeTrue();
    }

    [Theory]
    [InlineData(5, 10, 5)]
    [InlineData(12, 26, 9)]
    [InlineData(10, 20, 8)]
    public void CalculateMACD_CustomParameters_ReturnsCorrectStructure(int fast, int slow, int signal)
    {
        // Arrange
        var prices = TestDataBuilder.CreateDailyDataFromPrices(
            Enumerable.Range(1, 100).Select(x => (decimal)x * 10).ToArray());
        var priceList = prices.Select(p => p.ClosePrice).ToList();

        // Act
        var (dif, dea, macd) = IndicatorCalculator.CalculateMACD(priceList, fast, slow, signal);

        // Assert
        dif.Should().HaveCount(100);
        dea.Should().HaveCount(100);
        macd.Should().HaveCount(100);
    }

    #endregion

    #region CalculateRSI Tests

    [Fact]
    public void CalculateRSI_ValidData_ReturnsCorrectResult()
    {
        // Arrange
        var prices = TestDataBuilder.CreateDailyDataFromPrices(
            new decimal[] { 100m, 102m, 104m, 103m, 105m, 107m, 106m, 108m, 110m, 112m,
                           111m, 113m, 115m, 114m, 116m, 118m });
        var priceList = prices.Select(p => p.ClosePrice).ToList();
        int period = 14;

        // Act
        var result = IndicatorCalculator.CalculateRSI(priceList, period);

        // Assert
        result.Should().HaveCount(16);
        // 前period个应该是null
        result.Take(14).All(r => r == null).Should().BeTrue();
        result[14].Should().NotBeNull();
        result[14].Should().BeInRange(0, 100);
    }

    [Fact]
    public void CalculateRSI_AllGains_Returns100()
    {
        // Arrange
        var prices = new List<decimal> { 100m, 101m, 102m, 103m, 104m, 105m, 106m, 107m, 108m, 109m,
                                       110m, 111m, 112m, 113m, 114m, 115m };
        int period = 14;

        // Act
        var result = IndicatorCalculator.CalculateRSI(prices, period);

        // Assert
        result[14].Should().Be(100m);
    }

    [Fact]
    public void CalculateRSI_AllLosses_Returns0()
    {
        // Arrange
        var prices = new List<decimal> { 115m, 114m, 113m, 112m, 111m, 110m, 109m, 108m, 107m, 106m,
                                       105m, 104m, 103m, 102m, 101m, 100m };
        int period = 14;

        // Act
        var result = IndicatorCalculator.CalculateRSI(prices, period);

        // Assert
        result[14].Should().Be(0m);
    }

    [Fact]
    public void CalculateRSI_InsufficientData_ReturnsNulls()
    {
        // Arrange
        var prices = new List<decimal> { 100m, 101m, 102m };
        int period = 14;

        // Act
        var result = IndicatorCalculator.CalculateRSI(prices, period);

        // Assert
        result.Should().HaveCount(3);
        result.All(r => r == null).Should().BeTrue();
    }

    [Fact]
    public void CalculateRSI_EmptyData_ReturnsEmpty()
    {
        // Arrange
        var prices = new List<decimal>();
        int period = 14;

        // Act
        var result = IndicatorCalculator.CalculateRSI(prices, period);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region CalculateKDJ Tests

    [Fact]
    public void CalculateKDJ_ValidData_ReturnsCorrectResult()
    {
        // Arrange
        var data = TestDataBuilder.CreateDailyData(20);

        // Act
        var (k, d, j) = IndicatorCalculator.CalculateKDJ(data);

        // Assert
        k.Should().HaveCount(20);
        d.Should().HaveCount(20);
        j.Should().HaveCount(20);

        // 前8个应该为null（n-1 = 9-1 = 8）
        k.Take(8).All(val => val == null).Should().BeTrue();
        d.Take(8).All(val => val == null).Should().BeTrue();
        j.Take(8).All(val => val == null).Should().BeTrue();

        // 从第9个开始应该有值
        k[8].Should().NotBeNull();
        d[8].Should().NotBeNull();
        j[8].Should().NotBeNull();
    }

    [Fact]
    public void CalculateKDJ_EmptyData_ReturnsEmpty()
    {
        // Arrange
        var data = new List<StockDailyData>();

        // Act
        var (k, d, j) = IndicatorCalculator.CalculateKDJ(data);

        // Assert
        k.Should().BeEmpty();
        d.Should().BeEmpty();
        j.Should().BeEmpty();
    }

    [Fact]
    public void CalculateKDJ_InsufficientData_ReturnsNulls()
    {
        // Arrange
        var data = TestDataBuilder.CreateDailyData(5);

        // Act
        var (k, d, j) = IndicatorCalculator.CalculateKDJ(data);

        // Assert
        k.Should().HaveCount(5);
        d.Should().HaveCount(5);
        j.Should().HaveCount(5);

        k.All(val => val == null).Should().BeTrue();
        d.All(val => val == null).Should().BeTrue();
        j.All(val => val == null).Should().BeTrue();
    }

    [Fact]
    public void CalculateKDJ_CustomParameters_ReturnsCorrectStructure()
    {
        // Arrange
        var data = TestDataBuilder.CreateDailyData(15);

        // Act
        var (k, d, j) = IndicatorCalculator.CalculateKDJ(data, n: 5, m1: 3, m2: 3);

        // Assert
        k.Should().HaveCount(15);
        d.Should().HaveCount(15);
        j.Should().HaveCount(15);

        // 前4个应该为null（n-1 = 5-1 = 4）
        k.Take(4).All(val => val == null).Should().BeTrue();
    }

    #endregion

    #region CalculateBOLL Tests

    [Fact]
    public void CalculateBOLL_ValidData_ReturnsCorrectResult()
    {
        // Arrange
        var prices = TestDataBuilder.CreateDailyDataFromPrices(
            new decimal[] { 100m, 101m, 102m, 103m, 104m, 105m, 106m, 107m, 108m, 109m,
                           110m, 111m, 112m, 113m, 114m, 115m, 116m, 117m, 118m, 119m,
                           120m });
        var priceList = prices.Select(p => p.ClosePrice).ToList();
        int period = 20;

        // Act
        var (upper, middle, lower) = IndicatorCalculator.CalculateBOLL(priceList, period);

        // Assert
        upper.Should().HaveCount(21);
        middle.Should().HaveCount(21);
        lower.Should().HaveCount(21);

        // 前19个应该为null（period-1 = 20-1 = 19）
        upper.Take(19).All(val => val == null).Should().BeTrue();
        middle.Take(19).All(val => val == null).Should().BeTrue();
        lower.Take(19).All(val => val == null).Should().BeTrue();

        // 从第20个开始应该有值
        upper[19].Should().NotBeNull();
        middle[19].Should().NotBeNull();
        lower[19].Should().NotBeNull();

        // 上轨应该大于中轨
        upper[19].Should().BeGreaterThan(middle[19]!.Value);

        // 下轨应该小于中轨
        lower[19].Should().BeLessThan(middle[19]!.Value);
    }

    [Fact]
    public void CalculateBOLL_EmptyData_ReturnsEmpty()
    {
        // Arrange
        var prices = new List<decimal>();

        // Act
        var (upper, middle, lower) = IndicatorCalculator.CalculateBOLL(prices);

        // Assert
        upper.Should().BeEmpty();
        middle.Should().BeEmpty();
        lower.Should().BeEmpty();
    }

    [Fact]
    public void CalculateBOLL_InsufficientData_ReturnsNulls()
    {
        // Arrange
        var prices = new List<decimal> { 100m, 101m, 102m };

        // Act
        var (upper, middle, lower) = IndicatorCalculator.CalculateBOLL(prices, period: 20);

        // Assert
        upper.Should().HaveCount(3);
        middle.Should().HaveCount(3);
        lower.Should().HaveCount(3);

        upper.All(val => val == null).Should().BeTrue();
        middle.All(val => val == null).Should().BeTrue();
        lower.All(val => val == null).Should().BeTrue();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    public void CalculateBOLL_CustomPeriod_ReturnsCorrectStructure(int period)
    {
        // Arrange
        var prices = TestDataBuilder.CreateDailyDataFromPrices(
            Enumerable.Range(1, 50).Select(x => (decimal)x * 10).ToArray());
        var priceList = prices.Select(p => p.ClosePrice).ToList();
        decimal k = 2m;

        // Act
        var (upper, middle, lower) = IndicatorCalculator.CalculateBOLL(priceList, period, k);

        // Assert
        upper.Should().HaveCount(50);
        middle.Should().HaveCount(50);
        lower.Should().HaveCount(50);
    }

    #endregion

    #region CalculateAll Tests

    [Fact]
    public void CalculateAll_ValidData_ReturnsAllIndicators()
    {
        // Arrange
        var stockId = "test-stock-001";
        var data = TestDataBuilder.CreateDailyData(100);

        // Act
        var result = IndicatorCalculator.CalculateAll(stockId, data);

        // 打印前10个MA5和MA10值用于调试
        var ma5Info = string.Join(", ", result.Take(10).Select((r, i) => $"MA5[{i}]={r.MA5}"));
        var ma10Info = string.Join(", ", result.Take(10).Select((r, i) => $"MA10[{i}]={r.MA10}"));

        // Assert
        result.Should().HaveCount(100);

        // 验证第一条记录 - 前4天的MA5为null，前9天的MA10为null，前19天的MA20为null
        result[0].StockId.Should().Be(stockId);
        result[0].TradeDate.Should().Be(data[0].TradeDate);
        result[0].MA5.Should().BeNull("First MA5 should be null because period=5");
        result[0].MA10.Should().BeNull("First MA10 should be null because period=10");
        result[0].MA20.Should().BeNull("First MA20 should be null because period=20");

        // 验证第5条记录（索引4）- MA5应该有值（第5天开始有值），MA10和MA20还是null
        result[4].MA5.Should().NotBeNull("MA5 at index 4 should have value");
        result[4].MA10.Should().BeNull($"MA10 at index 4 should be null (need 10 days). Actual values: {ma10Info}");
        result[4].MA20.Should().BeNull("MA20 at index 4 should be null (need 20 days)");

        // 验证第10条记录（索引9）- MA5和MA10应该有值，MA20还是null
        result[9].MA5.Should().NotBeNull("MA5 at index 9 should have value");
        result[9].MA10.Should().NotBeNull("MA10 at index 9 should have value (10th day)");
        result[9].MA20.Should().BeNull("MA20 at index 9 should be null (need 20 days)");

        // 验证第20条记录（索引19）
        result[19].MA5.Should().NotBeNull();
        result[19].MA10.Should().NotBeNull();
        result[19].MA20.Should().NotBeNull("MA20 at index 19 should have value (20th day)");

        // 验证第30条记录（索引29）
        result[29].MA5.Should().NotBeNull();
        result[29].MA10.Should().NotBeNull();
        result[29].MA20.Should().NotBeNull();
    }

    [Fact]
    public void CalculateAll_EmptyData_ReturnsEmpty()
    {
        // Arrange
        var stockId = "test-stock-001";
        var data = new List<StockDailyData>();

        // Act
        var result = IndicatorCalculator.CalculateAll(stockId, data);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void CalculateAll_VerifyJSONFields()
    {
        // Arrange
        var stockId = "test-stock-001";
        var data = TestDataBuilder.CreateDailyData(40);

        // Act
        var result = IndicatorCalculator.CalculateAll(stockId, data);

        // Assert
        // 验证MACD JSON字段
        result[29].MACD.Should().NotBeNullOrEmpty(); // 第30条应该有MACD数据

        // 验证KDJ JSON字段
        result[8].KDJ.Should().NotBeNullOrEmpty(); // 第9条应该有KDJ数据

        // 验证BOLL JSON字段
        result[19].BOLL.Should().NotBeNullOrEmpty(); // 第20条应该有BOLL数据
    }

    [Fact]
    public void CalculateAll_VerifyRSIFields()
    {
        // Arrange
        var stockId = "test-stock-001";
        var data = TestDataBuilder.CreateDailyData(20);

        // Act
        var result = IndicatorCalculator.CalculateAll(stockId, data);

        // Assert
        // RSI6从第7条开始有值
        result[5].RSI6.Should().BeNull();
        result[6].RSI6.Should().NotBeNull();

        // RSI12从第13条开始有值
        result[11].RSI12.Should().BeNull();
        result[12].RSI12.Should().NotBeNull();
    }

    #endregion
}
