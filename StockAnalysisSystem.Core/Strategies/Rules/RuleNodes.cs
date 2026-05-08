namespace StockAnalysisSystem.Core.Strategies.Rules;

public sealed class AndNode : IRuleNode
{
    public List<IRuleNode> Children { get; set; } = new();

    public bool Evaluate(RuleContext ctx) => Children.Count > 0 && Children.All(c => c.Evaluate(ctx));
}

public sealed class OrNode : IRuleNode
{
    public List<IRuleNode> Children { get; set; } = new();

    public bool Evaluate(RuleContext ctx) => Children.Any(c => c.Evaluate(ctx));
}

public sealed class NotNode : IRuleNode
{
    public required IRuleNode Child { get; set; }

    public bool Evaluate(RuleContext ctx) => !Child.Evaluate(ctx);
}

public sealed class CompareNode : IRuleNode
{
    public required ValueSource Left { get; set; }
    public required CompareOp Op { get; set; }
    public required decimal RightValue { get; set; }

    public bool Evaluate(RuleContext ctx)
    {
        var lv = ValueResolver.TryResolve(ctx, Left);
        if (!lv.HasValue)
            return false;

        return Op switch
        {
            CompareOp.GreaterThan => lv.Value > RightValue,
            CompareOp.GreaterOrEqual => lv.Value >= RightValue,
            CompareOp.LessThan => lv.Value < RightValue,
            CompareOp.LessOrEqual => lv.Value <= RightValue,
            CompareOp.Equal => lv.Value == RightValue,
            CompareOp.NotEqual => lv.Value != RightValue,
            _ => false
        };
    }
}

public sealed class CrossOverNode : IRuleNode
{
    public required ValueSource Fast { get; set; }
    public required ValueSource Slow { get; set; }

    public bool Evaluate(RuleContext ctx)
    {
        var fast = ValueResolver.TryResolve(ctx, Fast);
        var slow = ValueResolver.TryResolve(ctx, Slow);
        var fastPrev = ValueResolver.TryResolvePrev(ctx, Fast);
        var slowPrev = ValueResolver.TryResolvePrev(ctx, Slow);

        if (!fast.HasValue || !slow.HasValue || !fastPrev.HasValue || !slowPrev.HasValue)
            return false;

        return fastPrev.Value <= slowPrev.Value && fast.Value > slow.Value;
    }
}

public sealed class CrossUnderNode : IRuleNode
{
    public required ValueSource Fast { get; set; }
    public required ValueSource Slow { get; set; }

    public bool Evaluate(RuleContext ctx)
    {
        var fast = ValueResolver.TryResolve(ctx, Fast);
        var slow = ValueResolver.TryResolve(ctx, Slow);
        var fastPrev = ValueResolver.TryResolvePrev(ctx, Fast);
        var slowPrev = ValueResolver.TryResolvePrev(ctx, Slow);

        if (!fast.HasValue || !slow.HasValue || !fastPrev.HasValue || !slowPrev.HasValue)
            return false;

        return fastPrev.Value >= slowPrev.Value && fast.Value < slow.Value;
    }
}

/// <summary>
/// 首板后回落（方案 A）规则节点：
/// 在近 N 日内向前找最近一次首板（当日涨幅>=阈值且前一日未达阈值），
/// 判断当前日是否满足回落买点（无未来函数）。
/// </summary>
public sealed class FirstBoardPullbackNode : IRuleNode
{
    public decimal LimitUpThreshold { get; set; } = 9.95m;

    /// <summary>收盘价相对首板最低价最大允许偏离（比例）。</summary>
    public decimal PullbackRange { get; set; } = 0.03m;

    /// <summary>相对首板最低价不得超过的百分点（与 PullbackRange 取更严）。</summary>
    public decimal MaxDeviationFromFirstBoardLowPercent { get; set; } = 3m;

    public int MaxDaysAfterLimitUp { get; set; } = 10;
    public int MinDaysAfterLimitUp { get; set; } = 1;

    /// <summary>仅在该自然日窗口内寻找最近一次首板。</summary>
    public int FirstBoardLookbackDays { get; set; } = 30;

    /// <summary>信号日跌幅上限（%），即 ChangePercent >= -MaxDailyDropPercent。</summary>
    public decimal MaxDailyDropPercent { get; set; } = 9m;

    public bool Evaluate(RuleContext ctx)
    {
        if (ctx.Bars == null || ctx.Bars.Count == 0)
            return false;
        if (ctx.BarIndex < 0 || ctx.BarIndex >= ctx.Bars.Count)
            return false;

        var ordered = ctx.Bars;
        var i = ctx.BarIndex;
        var data = ordered[i];
        var signalDate = data.TradeDate.Date;

        var lookbackDays = FirstBoardLookbackDays < 1 ? 1 : FirstBoardLookbackDays;
        var maxDaysAfterLimitUp = MaxDaysAfterLimitUp < 1 ? 1 : MaxDaysAfterLimitUp;
        var minDaysAfterLimitUp = Math.Max(0, MinDaysAfterLimitUp);

        var maxDevLowPctPoints = MaxDeviationFromFirstBoardLowPercent <= 0 ? 3m : MaxDeviationFromFirstBoardLowPercent;
        var maxDeviationFromLowRatio = maxDevLowPctPoints / 100m;
        var pullbackRange = PullbackRange <= 0 ? maxDeviationFromLowRatio : PullbackRange;
        var effectiveMaxDeviationFromFirstBoardLow = Math.Min(pullbackRange, maxDeviationFromLowRatio);

        var maxDailyDropPct = MaxDailyDropPercent <= 0 ? 9m : MaxDailyDropPercent;

        bool IsLimitUpDay(StockAnalysisSystem.Core.Entities.StockDailyData bar) =>
            bar.ChangePercent.HasValue && bar.ChangePercent.Value >= LimitUpThreshold;

        bool IsFirstBoardAt(int idx)
        {
            if (idx < 0 || idx >= ordered.Count)
                return false;
            if (!IsLimitUpDay(ordered[idx]))
                return false;
            if (idx == 0)
                return true;
            return !IsLimitUpDay(ordered[idx - 1]);
        }

        int FindRecentFirstBoardIndex(int signalIndex)
        {
            if (signalIndex <= 0)
                return -1;

            var oldestAnchorDate = signalDate.AddDays(-lookbackDays);
            for (int j = signalIndex - 1; j >= 0; j--)
            {
                var d = ordered[j].TradeDate.Date;
                if (d < oldestAnchorDate)
                    break;
                if (IsFirstBoardAt(j))
                    return j;
            }

            return -1;
        }

        var anchorIdx = FindRecentFirstBoardIndex(i);
        if (anchorIdx < 0)
            return false;

        var anchorDate = ordered[anchorIdx].TradeDate.Date;
        var firstLimitUpLowPrice = ordered[anchorIdx].LowPrice;
        if (firstLimitUpLowPrice <= 0)
            return false;

        var daysAfterLimitUp = (signalDate - anchorDate).Days;
        if (daysAfterLimitUp < minDaysAfterLimitUp || daysAfterLimitUp > maxDaysAfterLimitUp)
            return false;

        // 首板后至 signal 日（含）的最低收盘价，不使用未来数据
        decimal? lowestCloseAfterBoard = null;
        for (int k = anchorIdx + 1; k <= i; k++)
        {
            var c = ordered[k].ClosePrice > 0 ? ordered[k].ClosePrice : (decimal?)null;
            if (!c.HasValue)
                continue;
            if (!lowestCloseAfterBoard.HasValue || c < lowestCloseAfterBoard.Value)
                lowestCloseAfterBoard = c;
        }
        if (!lowestCloseAfterBoard.HasValue)
            return false;

        // 条件1：当天为跌（涨幅 < 0）
        if (!data.ChangePercent.HasValue || data.ChangePercent.Value >= 0)
            return false;

        // 条件1b：当日跌幅不超过上限（默认 9%，即 ChangePercent >= -9）
        if (data.ChangePercent.Value < -maxDailyDropPct)
            return false;

        // 条件2：当日最低价不破首板当日最低价
        if (data.LowPrice < firstLimitUpLowPrice)
            return false;

        // 条件3：收盘价不得高于「首板后～当日」区间最低收盘（即：当前收盘为区间最低收盘之一）
        var currentClose = data.ClosePrice > 0 ? data.ClosePrice : (decimal?)null;
        if (!currentClose.HasValue)
            return false;
        if (currentClose.Value > lowestCloseAfterBoard.Value)
            return false;

        var deviation = (currentClose.Value - firstLimitUpLowPrice) / firstLimitUpLowPrice;
        if (deviation < 0 || deviation > effectiveMaxDeviationFromFirstBoardLow)
            return false;

        return true;
    }
}

internal static class ValueResolver
{
    public static decimal? TryResolve(RuleContext ctx, ValueSource src)
    {
        return src switch
        {
            ValueSource.OpenPrice => ctx.Bar.OpenPrice,
            ValueSource.ClosePrice => ctx.Bar.ClosePrice,
            ValueSource.HighPrice => ctx.Bar.HighPrice,
            ValueSource.LowPrice => ctx.Bar.LowPrice,
            ValueSource.Volume => ctx.Bar.Volume,
            ValueSource.Amount => ctx.Bar.Amount,
            ValueSource.ChangePercent => ctx.Bar.ChangePercent,
            ValueSource.TurnoverRate => ctx.Bar.TurnoverRate,

            ValueSource.MA5 => ctx.Indicator?.MA5,
            ValueSource.MA10 => ctx.Indicator?.MA10,
            ValueSource.MA20 => ctx.Indicator?.MA20,
            ValueSource.RSI6 => ctx.Indicator?.RSI6,
            ValueSource.RSI12 => ctx.Indicator?.RSI12,
            ValueSource.VolumeMA5 => ctx.Indicator?.VolumeMA5,
            ValueSource.VolumeMA10 => ctx.Indicator?.VolumeMA10,
            ValueSource.VolumeMA120 => ctx.Indicator?.VolumeMA120,
            _ => null
        };
    }

    public static decimal? TryResolvePrev(RuleContext ctx, ValueSource src)
    {
        return src switch
        {
            ValueSource.OpenPrice => ctx.PrevBar?.OpenPrice,
            ValueSource.ClosePrice => ctx.PrevBar?.ClosePrice,
            ValueSource.HighPrice => ctx.PrevBar?.HighPrice,
            ValueSource.LowPrice => ctx.PrevBar?.LowPrice,
            ValueSource.Volume => ctx.PrevBar?.Volume,
            ValueSource.Amount => ctx.PrevBar?.Amount,
            ValueSource.ChangePercent => ctx.PrevBar?.ChangePercent,
            ValueSource.TurnoverRate => ctx.PrevBar?.TurnoverRate,

            ValueSource.MA5 => ctx.PrevIndicator?.MA5,
            ValueSource.MA10 => ctx.PrevIndicator?.MA10,
            ValueSource.MA20 => ctx.PrevIndicator?.MA20,
            ValueSource.RSI6 => ctx.PrevIndicator?.RSI6,
            ValueSource.RSI12 => ctx.PrevIndicator?.RSI12,
            ValueSource.VolumeMA5 => ctx.PrevIndicator?.VolumeMA5,
            ValueSource.VolumeMA10 => ctx.PrevIndicator?.VolumeMA10,
            ValueSource.VolumeMA120 => ctx.PrevIndicator?.VolumeMA120,
            _ => null
        };
    }
}

