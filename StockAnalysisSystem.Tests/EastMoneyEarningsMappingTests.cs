using System.Text.Json;
using FluentAssertions;
using StockAnalysisSystem.Core.Services;

namespace StockAnalysisSystem.Tests;

public sealed class EastMoneyEarningsMappingTests
{
    private const string SampleRow = """
        {
          "SECURITY_CODE": "603266",
          "SECURITY_NAME_ABBR": "天龙股份",
          "TRADE_MARKET": "上交所主板",
          "SECURITY_TYPE": "A股",
          "UPDATE_DATE": "2026-04-30 12:34:56",
          "REPORTDATE": "2026-03-31 00:00:00",
          "BASIC_EPS": 0.15,
          "DEDUCT_BASIC_EPS": 0.14,
          "TOTAL_OPERATE_INCOME": 320137642.84,
          "PARENT_NETPROFIT": 29807209.27,
          "WEIGHTAVG_ROE": 1.85,
          "YSTZ": 5.5047916186,
          "SJLTZ": 16.53,
          "BPS": 8.159043845857,
          "MGJYXJJE": 0.330557535281,
          "XSMLL": 22.6320907742,
          "YSHZ": -14.9519,
          "SJLHZ": 27.4474,
          "NOTICE_DATE": "2026-04-30 00:00:00",
          "ORG_CODE": "10278332",
          "QDATE": "2026Q1",
          "DATATYPE": "2026年 一季报",
          "DATAYEAR": "2026",
          "SECUCODE": "603266.SH",
          "BOARD_NAME": "汽车零部件",
          "BOARD_CODE": "BK0481",
          "PUBLISHNAME": "汽车零部件"
        }
        """;

    [Fact]
    public void TryMapEastMoneyRow_maps_extended_fields()
    {
        using var doc = JsonDocument.Parse(SampleRow);
        var row = doc.RootElement;
        var r = EarningsSyncService.TryMapEastMoneyRow(row, new DateTime(2026, 3, 31));

        r.Should().NotBeNull();
        r!.stock_code.Should().Be("603266");
        r.revenue_yoy.Should().BeApproximately(5.5047916186m, 0.0000001m);
        r.net_profit_yoy.Should().Be(16.53m);
        r.roe.Should().Be(1.85m);
        r.revenue_qoq.Should().BeApproximately(-14.9519m, 0.0001m);
        r.net_profit_qoq.Should().BeApproximately(27.4474m, 0.0001m);
        r.deduct_basic_eps.Should().Be(0.14m);
        r.bps.Should().BeApproximately(8.159043845857m, 0.0000001m);
        r.eps_operating_cf.Should().BeApproximately(0.330557535281m, 0.0000001m);
        r.gross_margin.Should().BeApproximately(22.6320907742m, 0.0000001m);
        r.board_name.Should().Be("汽车零部件");
        r.qdate.Should().Be("2026Q1");
        r.period_label.Should().Contain("一季报");
        r.update_date_api.Should().Be(new DateTime(2026, 4, 30, 12, 34, 56));
    }
}
