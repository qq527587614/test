using FluentAssertions;
using StockAnalysisSystem.Core.Backtest.V2;
using Xunit;

namespace StockAnalysisSystem.Tests.Backtest.V2;

public class AnalyticsServiceV2Tests
{
    [Fact]
    public void Calculate_ComputesMaxDrawdownAndRecovery()
    {
        var r = new BacktestResultV2
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 4),
            InitialCapital = 100m,
            FinalEquity = 130m
        };

        // equity: 100 -> 120 -> 90 -> 130
        r.EquityCurve.Add(new PortfolioPointV2 { Date = new DateTime(2024, 1, 1), Equity = 100m, Cash = 100m, PositionValue = 0m });
        r.EquityCurve.Add(new PortfolioPointV2 { Date = new DateTime(2024, 1, 2), Equity = 120m, Cash = 100m, PositionValue = 20m });
        r.EquityCurve.Add(new PortfolioPointV2 { Date = new DateTime(2024, 1, 3), Equity = 90m, Cash = 100m, PositionValue = -10m });
        r.EquityCurve.Add(new PortfolioPointV2 { Date = new DateTime(2024, 1, 4), Equity = 130m, Cash = 100m, PositionValue = 30m });

        r.DrawdownCurve.Add(new DrawdownPointV2 { Date = new DateTime(2024, 1, 1), PeakEquity = 100m, Drawdown = 0m });
        r.DrawdownCurve.Add(new DrawdownPointV2 { Date = new DateTime(2024, 1, 2), PeakEquity = 120m, Drawdown = 0m });
        r.DrawdownCurve.Add(new DrawdownPointV2 { Date = new DateTime(2024, 1, 3), PeakEquity = 120m, Drawdown = 0.25m });
        r.DrawdownCurve.Add(new DrawdownPointV2 { Date = new DateTime(2024, 1, 4), PeakEquity = 130m, Drawdown = 0m });

        r.DailyReturns.Add(new ReturnPointV2 { Date = new DateTime(2024, 1, 2), Return = 0.2m });
        r.DailyReturns.Add(new ReturnPointV2 { Date = new DateTime(2024, 1, 3), Return = -0.25m });
        r.DailyReturns.Add(new ReturnPointV2 { Date = new DateTime(2024, 1, 4), Return = 0.4444444444m });

        var svc = new AnalyticsServiceV2();
        var m = svc.Calculate(r);

        m.MaxDrawdown.Should().Be(0.25m);
        m.MaxDrawdownStartDate.Should().Be(new DateTime(2024, 1, 2));
        m.MaxDrawdownValleyDate.Should().Be(new DateTime(2024, 1, 3));
        m.MaxDrawdownRecoveryDate.Should().Be(new DateTime(2024, 1, 4));
    }
}

