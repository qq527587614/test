namespace StockAnalysisSystem.Core.Backtest.V2;

public sealed class AnalyticsServiceV2
{
    public BacktestMetricsV2 Calculate(BacktestResultV2 result)
    {
        var m = new BacktestMetricsV2();

        var curve = result.EquityCurve;
        m.TradingDays = curve.Count;

        if (result.InitialCapital > 0 && result.FinalEquity > 0)
        {
            m.TotalReturn = result.FinalEquity / result.InitialCapital - 1m;
        }

        if (curve.Count >= 2 && result.InitialCapital > 0 && result.FinalEquity > 0)
        {
            // 用交易日近似年化（252）
            var years = (decimal)curve.Count / 252m;
            if (years > 0)
            {
                m.AnnualReturn = (decimal)Math.Pow((double)(result.FinalEquity / result.InitialCapital), 1.0 / (double)years) - 1m;
            }
        }

        // 最大回撤 + 区间
        if (result.DrawdownCurve.Count > 0)
        {
            var max = result.DrawdownCurve.MaxBy(x => x.Drawdown);
            if (max != null)
            {
                m.MaxDrawdown = max.Drawdown;
                m.MaxDrawdownValleyDate = max.Date.Date;

                // 找峰值对应日期（第一次达到 PeakEquity 的日期）
                var peakEquity = max.PeakEquity;
                var peakPt = curve.FirstOrDefault(x => x.Equity == peakEquity && x.Date.Date <= max.Date.Date);
                if (peakPt != null)
                    m.MaxDrawdownStartDate = peakPt.Date.Date;

                // 找恢复日期：权益重新 >= peakEquity
                var rec = curve.FirstOrDefault(x => x.Date.Date >= max.Date.Date && x.Equity >= peakEquity);
                if (rec != null)
                    m.MaxDrawdownRecoveryDate = rec.Date.Date;

                if (m.MaxDrawdownStartDate.HasValue)
                    m.MaxDrawdownDurationDays = (m.MaxDrawdownValleyDate.Value.Date - m.MaxDrawdownStartDate.Value.Date).Days;

                if (m.MaxDrawdownRecoveryDate.HasValue)
                    m.MaxDrawdownRecoveryDays = (m.MaxDrawdownRecoveryDate.Value.Date - m.MaxDrawdownValleyDate.Value.Date).Days;
            }
        }

        // Sharpe / Sortino
        var rets = result.DailyReturns.Select(x => x.Return).ToList();
        if (rets.Count > 1)
        {
            var avg = rets.Average();
            var std = StdDev(rets);
            if (std > 0)
                m.SharpeRatio = avg / std * (decimal)Math.Sqrt(252);

            var downside = rets.Where(x => x < 0).ToList();
            if (downside.Count > 1)
            {
                var dstd = StdDev(downside);
                if (dstd > 0)
                    m.SortinoRatio = avg / dstd * (decimal)Math.Sqrt(252);
            }
        }

        if (m.MaxDrawdown > 0)
            m.CalmarRatio = m.AnnualReturn / m.MaxDrawdown;

        // 交易统计
        if (result.Trades.Count > 0)
        {
            m.TradeCount = result.Trades.Count;
            m.WinCount = result.Trades.Count(t => t.ProfitLoss > 0);
            m.LossCount = result.Trades.Count(t => t.ProfitLoss < 0);
            m.WinRate = m.TradeCount > 0 ? (decimal)m.WinCount / m.TradeCount : 0m;

            var profits = result.Trades.Where(t => t.ProfitLoss > 0).Select(t => t.ProfitLoss).ToList();
            var losses = result.Trades.Where(t => t.ProfitLoss < 0).Select(t => Math.Abs(t.ProfitLoss)).ToList();
            m.AverageProfit = profits.Count > 0 ? profits.Average() : 0m;
            m.AverageLoss = losses.Count > 0 ? losses.Average() : 0m;
            m.MaxProfit = profits.Count > 0 ? profits.Max() : 0m;
            m.MaxLoss = losses.Count > 0 ? losses.Max() : 0m;
            if (m.AverageLoss > 0)
                m.ProfitFactor = m.AverageProfit / m.AverageLoss;
        }

        return m;
    }

    public void Fill(BacktestResultV2 result)
    {
        result.Metrics = Calculate(result);
    }

    private static decimal StdDev(IReadOnlyList<decimal> xs)
    {
        if (xs.Count <= 1)
            return 0m;
        var avg = xs.Average();
        var v = xs.Select(x => (double)((x - avg) * (x - avg))).Average();
        return (decimal)Math.Sqrt(v);
    }
}

