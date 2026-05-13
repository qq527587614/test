using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StockAnalysisSystem.Core.Entities;

namespace StockAnalysisSystem.Core.Services;

public sealed record TopAmountMa5PickOptions
{
    public int TopNByAmount { get; init; } = 20;
    public decimal MinChangePct { get; init; } = 5m;
    public decimal MaxChangePct { get; init; } = 9.9m;
    /// <summary>换手率下限（%）。</summary>
    public decimal MinTurnoverRate { get; init; } = 8m;
    /// <summary>换手率上限（%）。</summary>
    public decimal MaxTurnoverRate { get; init; } = 15m;
    /// <summary>成交额下限（单位：万）。会在取 TopN 前先过滤。</summary>
    public decimal MinAmountWan { get; init; } = 0m;
    
    /// <summary>
    /// 估算流通市值上限（单位：亿元）。默认 300 表示仅在「估算流通市值 &lt; 300 亿」的股票中取成交额 TopN。
    /// 估算式：流通市值(元) ≈ 成交额(万)×10000 / 换手率(小数)，其中换手率为百分数时先除以 100。
    /// </summary>
    public decimal? MaxTotalMarketCapYi { get; init; } = 300m;

    /// <summary>
    /// 允许“回踩5日线”的最大偏离比例。例如 0.01 表示 LowPrice <= MA5 * 1.01。
    /// </summary>
    public decimal PullbackTolerance { get; init; } = 0.01m;

    /// <summary>
    /// 收盘不破5日线（ClosePrice >= MA5 * (1 - CloseAboveMa5Tolerance)）。
    /// </summary>
    public decimal CloseAboveMa5Tolerance { get; init; } = 0m;

    /// <summary>
    /// 为 true 时：除“最低价下探至 MA5 带内”外，还接受“收盘价落在 MA5 上方且距离 MA5 不超过 PullbackTolerance”（强势股常全天在 MA5 上方运行）。
    /// </summary>
    public bool AllowCloseNearMa5WithoutIntradayTouch { get; init; } = true;
}

public sealed record TopAmountMa5PickRow
{
    public DateTime TradeDate { get; init; }
    public string StockId { get; init; } = "";
    public string StockCode { get; init; } = "";
    public string StockName { get; init; } = "";

    /// <summary>有效收盘价（CurrentPrice 优先，否则 ClosePrice），用于展示与回测买入价。</summary>
    public decimal ClosePrice { get; init; }
    public decimal OpenPrice { get; init; }
    public decimal HighPrice { get; init; }
    public decimal LowPrice { get; init; }
    public decimal Amount { get; init; }
    public decimal? TurnoverRate { get; init; }
    /// <summary>按「成交额(万) + 换手率」估算的流通市值（亿元）。</summary>
    public decimal? EstCirculationCapYi { get; init; }
    public decimal? ChangePercent { get; init; }
    public decimal? MA5 { get; init; }

    public string Reason { get; init; } = "";
}

public sealed record TopAmountMa5BacktestSummary
{
    public int TradingDays { get; init; }
    /// <summary>至少选出过 1 只股票的交易日天数。</summary>
    public int DaysWithPicks { get; init; }
    public int Picks { get; init; }
    public int Trades { get; init; }
    public int WinTrades { get; init; }
    public decimal WinRate { get; init; }
    public decimal AvgReturnPct { get; init; }
    public decimal TotalReturnPct { get; init; }
}

/// <summary>回测中每一笔「入选并计入每日上限」的明细（含无次日 K 线无法平仓的记录）。</summary>
public sealed record TopAmountMa5BacktestDetailRow
{
    public DateTime PickDate { get; init; }
    /// <summary>规则上的下一交易日（计划卖出日）。</summary>
    public DateTime PlannedSellDate { get; init; }
    public string StockId { get; init; } = "";
    public string StockCode { get; init; } = "";
    public string StockName { get; init; } = "";

    public decimal BuyClose { get; init; }
    public decimal? SellClose { get; init; }
    /// <summary>有卖出价时：(卖出有效收盘-买入有效收盘)/买入有效收盘*100，单位：百分点。</summary>
    public decimal? ReturnPct { get; init; }
    public bool HasNextDayBar { get; init; }

    public decimal Amount { get; init; }
    public decimal? ChangePct { get; init; }
    public decimal? MA5 { get; init; }
    public decimal OpenPrice { get; init; }
    public decimal HighPrice { get; init; }
    public decimal LowPrice { get; init; }
    public decimal? TurnoverRate { get; init; }
    /// <summary>按「成交额(万) + 换手率」估算的流通市值（亿元）。</summary>
    public decimal? EstCirculationCapYi { get; init; }
    public string Reason { get; init; } = "";
}

public sealed record TopAmountMa5BacktestResult(
    TopAmountMa5BacktestSummary Summary,
    IReadOnlyList<TopAmountMa5BacktestDetailRow> Details);

/// <summary>
/// 成交额TopN + 回踩5日线选股与简易回测（不构成投资建议）。MA5 优先读 StockDailyIndicator，缺失时由最近 5 根日线收盘补算。
/// 有效收盘价：<see cref="StockDailyData.CurrentPrice"/> 有值且大于 0 时采用，否则采用 <see cref="StockDailyData.ClosePrice"/>。
/// 市值过滤：<see cref="StockDailyData.Amount"/> 按项目约定为「万元」，估算流通市值时会先换算为元再与上限比较。
/// </summary>
public sealed class TopAmountMa5PickService
{
    private readonly AppDbContext _db;

    public TopAmountMa5PickService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>业务上的收盘价：CurrentPrice 优先，否则 ClosePrice。</summary>
    private static decimal EffectiveClose(decimal closePrice, decimal? currentPrice) =>
        currentPrice is > 0 ? currentPrice.Value : closePrice;

    /// <summary>1「亿元」折合的元数（1 亿 = 1e8 元）。</summary>
    private const decimal YiYuan = 100_000_000m;

    /// <summary>万元成交额换算为元的乘数。</summary>
    private const decimal WanYuanToYuan = 10_000m;

    private static bool IsTurnoverInRange(decimal? turnoverPct, decimal minPct, decimal maxPct)
    {
        if (!turnoverPct.HasValue) return false;
        var t = turnoverPct.Value;
        return t >= minPct && t <= maxPct;
    }

    /// <summary>
    /// 用当日成交额（万元）、换手率估算流通市值（元）。换手多为百分数（如 3.5 表示 3.5%）；若值在 (0,1] 则视为已小数化的小数。
    /// </summary>
    private static bool TryEstimateCirculatingMarketCapYuan(decimal amountWan, decimal? turnover, out decimal capYuan)
    {
        capYuan = 0;
        if (amountWan <= 0 || turnover is not > 0)
            return false;
        var t = turnover.Value;
        var frac = t > 1m ? t / 100m : t;
        if (frac is <= 0 or >= 1m)
            return false;
        capYuan = amountWan * WanYuanToYuan / frac;
        return capYuan > 0;
    }

    private static bool TryEstimateCirculatingMarketCapYi(decimal amountWan, decimal? turnover, out decimal capYi)
    {
        capYi = 0;
        if (!TryEstimateCirculatingMarketCapYuan(amountWan, turnover, out var capYuan))
            return false;
        capYi = capYuan / YiYuan;
        return capYi > 0;
    }

    private sealed record TopCandidate(
        string StockID,
        string StockCode,
        decimal ClosePrice,
        decimal? CurrentPrice,
        decimal OpenPrice,
        decimal HighPrice,
        decimal LowPrice,
        decimal Amount,
        decimal? TurnoverRate,
        decimal? ChangePercent);

    public async Task<List<TopAmountMa5PickRow>> PickAsync(DateTime tradeDate, TopAmountMa5PickOptions options, CancellationToken ct = default)
    {
        var date = tradeDate.Date;
        var topN = Math.Clamp(options.TopNByAmount, 1, 200);
        var dayEnd = date.AddDays(1);
        const int historyLookbackDays = 80;

        var allDay = await _db.StockDailyData
            .AsNoTracking()
            .Where(d => d.TradeDate >= date && d.TradeDate < dayEnd)
            .Select(d => new TopCandidate(
                d.StockID,
                d.StockCode,
                d.ClosePrice,
                d.CurrentPrice,
                d.OpenPrice,
                d.HighPrice,
                d.LowPrice,
                d.Amount,
                d.TurnoverRate,
                d.ChangePercent))
            .ToListAsync(ct);

        // TopN 候选池：先做「成交额下限 + 换手率区间 + 市值上限」过滤，再按成交额取 TopN
        var minTurn = options.MinTurnoverRate;
        var maxTurn = options.MaxTurnoverRate;
        var capLimitYuan = options.MaxTotalMarketCapYi.HasValue ? options.MaxTotalMarketCapYi.Value * YiYuan : (decimal?)null;
        var minAmountWan = options.MinAmountWan;

        IEnumerable<TopCandidate> candidates = allDay
            .Where(d => d.Amount >= minAmountWan)
            .Where(d => IsTurnoverInRange(d.TurnoverRate, minTurn, maxTurn));

        if (capLimitYuan.HasValue)
        {
            candidates = candidates.Where(d =>
                TryEstimateCirculatingMarketCapYuan(d.Amount, d.TurnoverRate, out var cap) &&
                cap < capLimitYuan.Value);
        }

        var top = candidates
            .OrderByDescending(d => d.Amount)
            .ThenBy(d => d.StockID, StringComparer.Ordinal)
            .Take(topN)
            .ToList();

        if (top.Count == 0)
            return new List<TopAmountMa5PickRow>();

        var stockIds = top.Select(x => x.StockID).Distinct(StringComparer.Ordinal).ToList();

        // 查名称与 MA5（指标表可能未预计算，后续用日线补算）
        var nameMap = await _db.StockInfos
            .AsNoTracking()
            .Where(s => stockIds.Contains(s.StockID))
            .Select(s => new { s.StockID, s.StockName })
            .ToDictionaryAsync(x => x.StockID, x => x.StockName, StringComparer.Ordinal, ct);

        var ma5Rows = await _db.StockDailyIndicators
            .AsNoTracking()
            .Where(i => stockIds.Contains(i.StockId) && i.TradeDate >= date && i.TradeDate < dayEnd)
            .Select(i => new { i.StockId, i.MA5 })
            .ToListAsync(ct);
        var ma5Map = ma5Rows
            .GroupBy(x => x.StockId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().MA5, StringComparer.Ordinal);

        var windowStart = date.AddDays(-historyLookbackDays);
        var history = await _db.StockDailyData
            .AsNoTracking()
            .Where(d => stockIds.Contains(d.StockID) && d.TradeDate >= windowStart && d.TradeDate < dayEnd)
            .Select(d => new { d.StockID, d.TradeDate, d.ClosePrice, d.CurrentPrice })
            .ToListAsync(ct);

        var histByStock = history
            .GroupBy(h => h.StockID, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.TradeDate).ToList(), StringComparer.Ordinal);

        var rows = new List<TopAmountMa5PickRow>();
        foreach (var x in top)
        {
            if (!histByStock.TryGetValue(x.StockID, out var bars) || bars.Count == 0)
                continue;

            var idx = bars.FindIndex(b => b.TradeDate.Date == date);
            if (idx < 0)
                continue;

            var todayPx = EffectiveClose(x.ClosePrice, x.CurrentPrice);
            if (todayPx <= 0)
                continue;

            decimal ma5Val;
            decimal? ma5ForRow = null;
            if (ma5Map.TryGetValue(x.StockID, out var indMa5) && indMa5.HasValue && indMa5.Value > 0)
            {
                ma5Val = indMa5.Value;
                ma5ForRow = indMa5;
            }
            else
            {
                if (idx < 4)
                    continue;
                var sum = 0m;
                for (var j = idx - 4; j <= idx; j++)
                    sum += EffectiveClose(bars[j].ClosePrice, bars[j].CurrentPrice);
                ma5Val = Math.Round(sum / 5m, 4, MidpointRounding.AwayFromZero);
                ma5ForRow = ma5Val;
                if (ma5Val <= 0)
                    continue;
            }

            decimal chg;
            if (x.ChangePercent.HasValue)
                chg = x.ChangePercent.Value;
            else if (idx > 0)
            {
                var prevClose = EffectiveClose(bars[idx - 1].ClosePrice, bars[idx - 1].CurrentPrice);
                if (prevClose <= 0)
                    continue;
                chg = Math.Round((todayPx - prevClose) / prevClose * 100m, 4, MidpointRounding.AwayFromZero);
            }
            else
                continue;

            if (chg < options.MinChangePct || chg > options.MaxChangePct)
                continue;

            var turn = x.TurnoverRate ?? 0m;
            if (turn < options.MinTurnoverRate || turn > options.MaxTurnoverRate)
                continue;

            decimal? estCapYi = null;
            if (TryEstimateCirculatingMarketCapYi(x.Amount, x.TurnoverRate, out var capYi))
                estCapYi = Math.Round(capYi, 2, MidpointRounding.AwayFromZero);

            var ma5High = ma5Val * (1m + options.PullbackTolerance);
            var ma5Floor = ma5Val * (1m - options.CloseAboveMa5Tolerance);

            var intradayTouch = x.LowPrice <= ma5High;
            var closeNearMa5 = todayPx <= ma5High && todayPx >= ma5Floor;
            var pullbackOk = intradayTouch || (options.AllowCloseNearMa5WithoutIntradayTouch && closeNearMa5);
            if (!pullbackOk)
                continue;

            var reasonTag = intradayTouch
                ? $"Low<=MA5*{FormatPct(1m + options.PullbackTolerance)}"
                : (options.AllowCloseNearMa5WithoutIntradayTouch ? "收盘贴近MA5(无下探)" : "");

            rows.Add(new TopAmountMa5PickRow
            {
                TradeDate = date,
                StockId = x.StockID,
                StockCode = x.StockCode,
                StockName = nameMap.TryGetValue(x.StockID, out var n) ? n : x.StockCode,
                ClosePrice = todayPx,
                OpenPrice = x.OpenPrice,
                HighPrice = x.HighPrice,
                LowPrice = x.LowPrice,
                Amount = x.Amount,
                TurnoverRate = x.TurnoverRate,
                EstCirculationCapYi = estCapYi,
                ChangePercent = x.ChangePercent ?? chg,
                MA5 = ma5ForRow,
                Reason = $"成交额Top{topN} & 回踩MA5（{reasonTag}）收盘>=MA5*{FormatPct(1m - options.CloseAboveMa5Tolerance)}"
            });
        }

        return rows;
    }

    private static string FormatPct(decimal v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// 简易回测：按每个交易日的选股结果，假设收盘买入、次日收盘卖出（不含手续费滑点）。
    /// </summary>
    public async Task<TopAmountMa5BacktestSummary> BacktestCloseToCloseAsync(
        DateTime startDate,
        DateTime endDate,
        TopAmountMa5PickOptions options,
        int maxPicksPerDay = 3,
        CancellationToken ct = default)
    {
        var r = await BacktestCloseToCloseWithDetailsAsync(startDate, endDate, options, maxPicksPerDay, ct);
        return r.Summary;
    }

    /// <summary>
    /// 与 <see cref="BacktestCloseToCloseAsync"/> 相同规则，并返回每笔入选明细（便于导出核对）。
    /// </summary>
    public async Task<TopAmountMa5BacktestResult> BacktestCloseToCloseWithDetailsAsync(
        DateTime startDate,
        DateTime endDate,
        TopAmountMa5PickOptions options,
        int maxPicksPerDay = 3,
        CancellationToken ct = default)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        if (end < start)
            (start, end) = (end, start);

        maxPicksPerDay = Math.Clamp(maxPicksPerDay, 1, 50);

        var rangeEnd = end.AddDays(1);
        var tradeDates = await _db.StockDailyData
            .AsNoTracking()
            .Where(d => d.TradeDate >= start && d.TradeDate < rangeEnd)
            .Select(d => d.TradeDate.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(ct);

        var details = new List<TopAmountMa5BacktestDetailRow>();

        if (tradeDates.Count < 2)
        {
            return new TopAmountMa5BacktestResult(
                new TopAmountMa5BacktestSummary
                {
                    TradingDays = tradeDates.Count,
                    DaysWithPicks = 0,
                    Picks = 0,
                    Trades = 0,
                    WinTrades = 0,
                    WinRate = 0,
                    AvgReturnPct = 0,
                    TotalReturnPct = 0
                },
                details);
        }

        var totalReturn = 0m;
        var tradeCount = 0;
        var winCount = 0;
        var pickCount = 0;
        var daysWithPicks = 0;

        for (var i = 0; i < tradeDates.Count - 1; i++)
        {
            var d0 = tradeDates[i];
            var d1 = tradeDates[i + 1];
            var d1End = d1.AddDays(1);

            var picks = await PickAsync(d0, options, ct);
            if (picks.Count == 0)
                continue;

            daysWithPicks++;

            var dayPicks = picks
                .OrderByDescending(p => p.Amount)
                .Take(maxPicksPerDay)
                .ToList();

            pickCount += dayPicks.Count;

            var codes = dayPicks.Select(p => p.StockId).Distinct().ToList();
            var nextBars = await _db.StockDailyData
                .AsNoTracking()
                .Where(b => codes.Contains(b.StockID) && b.TradeDate >= d1 && b.TradeDate < d1End)
                .Select(b => new { b.StockID, b.ClosePrice, b.CurrentPrice })
                .ToListAsync(ct);

            var nextMap = nextBars
                .GroupBy(x => x.StockID, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => EffectiveClose(g.First().ClosePrice, g.First().CurrentPrice),
                    StringComparer.Ordinal);

            foreach (var p in dayPicks)
            {
                var hasBar = nextMap.TryGetValue(p.StockId, out var sellClose) && p.ClosePrice > 0;
                decimal? retPct = null;
                if (hasBar)
                {
                    var ret = (sellClose - p.ClosePrice) / p.ClosePrice;
                    retPct = Math.Round(ret * 100m, 4, MidpointRounding.AwayFromZero);
                    totalReturn += ret;
                    tradeCount++;
                    if (ret > 0) winCount++;
                }

                details.Add(new TopAmountMa5BacktestDetailRow
                {
                    PickDate = d0,
                    PlannedSellDate = d1,
                    StockId = p.StockId,
                    StockCode = p.StockCode,
                    StockName = p.StockName,
                    BuyClose = p.ClosePrice,
                    SellClose = hasBar ? sellClose : null,
                    ReturnPct = retPct,
                    HasNextDayBar = hasBar,
                    Amount = p.Amount,
                    ChangePct = p.ChangePercent,
                    MA5 = p.MA5,
                    OpenPrice = p.OpenPrice,
                    HighPrice = p.HighPrice,
                    LowPrice = p.LowPrice,
                    TurnoverRate = p.TurnoverRate,
                    EstCirculationCapYi = p.EstCirculationCapYi,
                    Reason = p.Reason
                });
            }
        }

        var avgRet = tradeCount > 0 ? totalReturn / tradeCount : 0m;
        var summary = new TopAmountMa5BacktestSummary
        {
            TradingDays = tradeDates.Count,
            DaysWithPicks = daysWithPicks,
            Picks = pickCount,
            Trades = tradeCount,
            WinTrades = winCount,
            WinRate = tradeCount > 0 ? (decimal)winCount / tradeCount : 0m,
            AvgReturnPct = avgRet * 100m,
            TotalReturnPct = totalReturn * 100m
        };

        return new TopAmountMa5BacktestResult(summary, details);
    }
}

