namespace StockAnalysisSystem.Core.Backtest.V2;

using StockAnalysisSystem.Core.Entities;

/// <summary>
/// 简单多仓位模型：最多持有 N 只；买入时按“等权预算”分配资金并按整手取整。
/// </summary>
public sealed class EqualWeightSlotsPortfolioModelV2 : IPortfolioModelV2
{
    private readonly IExecutionModelV2 _execution;
    private readonly int _maxPositions;

    public EqualWeightSlotsPortfolioModelV2(IExecutionModelV2 execution, int maxPositions = 10)
    {
        _execution = execution;
        _maxPositions = Math.Clamp(maxPositions, 1, 200);
    }

    public void ApplyOrders(
        DateTime tradeDate,
        IReadOnlyList<PlannedOrderV2> plannedOrders,
        BacktestConfigV2 config,
        MarketSnapshotV2 market,
        PortfolioStateV2 portfolio,
        BacktestResultV2 result)
    {
        // 1) 先卖后买（释放资金）
        foreach (var o in plannedOrders.Where(x => x.Side == OrderSideV2.Sell))
        {
            if (!portfolio.Positions.TryGetValue(o.StockId, out var pos))
                continue;
            if (!market.BarsByStockId.TryGetValue(o.StockId, out var bar))
                continue;

            var fill = _execution.GetSellFillPrice(bar, config);
            if (!fill.HasValue)
                continue;

            var sellAmt = fill.Value * pos.Shares;
            var sellComm = sellAmt * config.CommissionRate;
            portfolio.Cash += sellAmt - sellComm;

            var buyAmt = pos.CostPrice * pos.Shares;
            var buyComm = buyAmt * config.CommissionRate;
            var totalComm = buyComm + sellComm;
            var pnl = sellAmt - buyAmt - totalComm;
            var pnlPct = buyAmt > 0 ? pnl / buyAmt : 0m;

            result.Trades.Add(new TradeRecordV2
            {
                StockId = pos.StockId,
                StockCode = pos.StockCode,
                StockName = pos.StockName,
                StrategyName = string.IsNullOrWhiteSpace(pos.StrategyName) ? o.StrategyName : pos.StrategyName,
                BuyDate = pos.BuyDate.Date,
                BuyPrice = pos.CostPrice,
                Shares = pos.Shares,
                SellDate = tradeDate.Date,
                SellPrice = fill.Value,
                Commission = totalComm,
                SellReason = o.Reason,
                ProfitLoss = pnl,
                ProfitLossPercent = pnlPct,
                HoldingDays = (tradeDate.Date - pos.BuyDate.Date).Days
            });

            portfolio.Positions.Remove(o.StockId);
        }

        // 2) 再处理买入
        var freeSlots = _maxPositions - portfolio.Positions.Count;
        if (freeSlots <= 0)
            return;

        var buyOrders = plannedOrders.Where(x => x.Side == OrderSideV2.Buy).ToList();
        if (buyOrders.Count == 0)
            return;

        // 当日估值：使用现价估算持仓市值（便于算 slotBudget）
        var mv = 0m;
        foreach (var pos in portfolio.Positions.Values)
        {
            if (market.BarsByStockId.TryGetValue(pos.StockId, out var bar))
            {
                var px = DefaultExecutionModelV2.ResolveBasisPrice(bar, config.PriceBasis);
                if (px.HasValue && px.Value > 0)
                    mv += px.Value * pos.Shares;
            }
        }

        var equity = portfolio.Cash + mv;
        var slotBudget = equity / _maxPositions;

        foreach (var o in buyOrders)
        {
            if (freeSlots <= 0)
                break;
            if (portfolio.Positions.ContainsKey(o.StockId))
                continue;
            if (!market.BarsByStockId.TryGetValue(o.StockId, out var bar))
                continue;

            var fill = _execution.GetBuyFillPrice(bar, config);
            if (!fill.HasValue || fill.Value <= 0)
                continue;

            var budget = Math.Min(slotBudget, portfolio.Cash / Math.Max(1, freeSlots));
            var rawShares = (int)(budget / fill.Value / config.RoundLot) * config.RoundLot;
            if (rawShares <= 0)
                continue;

            var buyAmt = fill.Value * rawShares;
            var buyComm = buyAmt * config.CommissionRate;
            if (buyAmt + buyComm > portfolio.Cash)
                continue;

            if (!market.StockById.TryGetValue(o.StockId, out var si))
                continue;

            portfolio.Cash -= buyAmt + buyComm;
            portfolio.Positions[o.StockId] = new PositionV2
            {
                StockId = o.StockId,
                StockCode = si.StockCode,
                StockName = si.StockName,
                BuyDate = tradeDate.Date,
                CostPrice = fill.Value,
                Shares = rawShares,
                StrategyName = o.StrategyName
            };

            freeSlots--;
        }
    }
}

