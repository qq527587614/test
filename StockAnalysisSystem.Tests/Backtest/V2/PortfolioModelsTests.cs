using FluentAssertions;
using StockAnalysisSystem.Core.Backtest.V2;
using StockAnalysisSystem.Core.Entities;
using Xunit;

namespace StockAnalysisSystem.Tests.Backtest.V2;

public class PortfolioModelsTests
{
    [Fact]
    public void EqualWeightSlotsPortfolio_BuyThenSell_CreatesTrade()
    {
        var exec = new DefaultExecutionModelV2();
        var model = new EqualWeightSlotsPortfolioModelV2(exec, maxPositions: 10);
        var cfg = new BacktestConfigV2
        {
            StartDate = new DateTime(2024, 1, 2),
            EndDate = new DateTime(2024, 1, 3),
            InitialCapital = 100_000m,
            CommissionRate = 0m,
            SlippageRate = 0m,
            PriceBasis = PriceBasis.Close,
            RoundLot = 100
        };

        var stock = new StockInfo { StockID = "sid", StockCode = "000001", StockName = "Foo", Market = "SZ" };
        var bars = new Dictionary<string, StockDailyData>
        {
            ["sid"] = new StockDailyData { StockID = "sid", StockCode = "000001", TradeDate = new DateTime(2024, 1, 2), OpenPrice = 10, ClosePrice = 10, HighPrice = 10, LowPrice = 10, Volume = 1, Amount = 1 }
        };
        var market = new MarketSnapshotV2
        {
            TradeDate = new DateTime(2024, 1, 2),
            BarsByStockId = bars,
            StockById = new Dictionary<string, StockInfo> { ["sid"] = stock }
        };

        var portfolio = new PortfolioStateV2 { Cash = cfg.InitialCapital };
        var result = new BacktestResultV2 { StartDate = cfg.StartDate, EndDate = cfg.EndDate, InitialCapital = cfg.InitialCapital };

        model.ApplyOrders(market.TradeDate, new[]
        {
            new PlannedOrderV2 { Side = OrderSideV2.Buy, StockId = "sid", StrategyName = "S", Reason = "B" }
        }, cfg, market, portfolio, result);

        portfolio.Positions.Should().ContainKey("sid");

        // next day sell at higher price
        market = new MarketSnapshotV2
        {
            TradeDate = new DateTime(2024, 1, 3),
            BarsByStockId = new Dictionary<string, StockDailyData>
            {
                ["sid"] = new StockDailyData { StockID = "sid", StockCode = "000001", TradeDate = new DateTime(2024, 1, 3), OpenPrice = 11, ClosePrice = 11, HighPrice = 11, LowPrice = 11, Volume = 1, Amount = 1 }
            },
            StockById = new Dictionary<string, StockInfo> { ["sid"] = stock }
        };

        model.ApplyOrders(market.TradeDate, new[]
        {
            new PlannedOrderV2 { Side = OrderSideV2.Sell, StockId = "sid", StrategyName = "S", Reason = "X" }
        }, cfg, market, portfolio, result);

        portfolio.Positions.Should().BeEmpty();
        result.Trades.Should().ContainSingle();
        result.Trades[0].ProfitLoss.Should().BePositive();
    }
}

