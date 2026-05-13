using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.RealtimeData;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.Core.Services;

public sealed record LimitUpReviewStockRow(
    string Code6,
    string Name,
    string Plates,
    string? FirstSealTime,
    decimal? PctChg,
    decimal? DailyTurnPct,
    decimal? DailyAmountWan,
    int? HotRank,
    decimal CompositeScore,
    string Role,
    string Tips,
    decimal? DailyClose,
    decimal? MinuteQualityScore,
    string? MinuteQualityNote);

public sealed record LimitUpReviewPlateSummary(
    string PlateName,
    int LimitUpCount,
    string? LeadCandidateCode6,
    string? LeadCandidateName);

/// <summary>近若干交易日题材出现频次与涨停家数合计（启发式强弱）。</summary>
public sealed record LimitUpPlatePersistenceRow(
    string PlateName,
    int DaysPresentInWindow,
    int TotalLimitUpStocksInWindow,
    int TodayLimitUpCount,
    decimal HeuristicScore);

public sealed record LimitUpWatchPick(string Code6, string Name, string Reason);

/// <summary>基于「题材持续性 + 当日综合分」的次日观察清单，非收益预测。</summary>
public sealed record LimitUpTomorrowFocusRow(
    string PrimarySector,
    string Rationale,
    IReadOnlyList<LimitUpWatchPick> Picks);

public sealed record LimitUpReviewResult(
    DateTime TradeDate,
    IReadOnlyList<LimitUpReviewPlateSummary> PlateHotspots,
    IReadOnlyList<LimitUpReviewStockRow> Stocks,
    string NarrativeSummary,
    IReadOnlyList<LimitUpPlatePersistenceRow> PlatePersistenceOutlook,
    LimitUpTomorrowFocusRow? TomorrowWatchHeuristic);

/// <summary>
/// 收盘后涨停复盘：涨停表（财联社 cls）+ 日线换手/成交额 + 东方财富热度排名，输出板块热点与次日关注池（启发式，不构成投资建议）。
/// </summary>
public sealed class LimitUpReviewService
{
    private readonly AppDbContext _db;
    private readonly EastMoneyHotRankService _hotRank;
    private readonly SinaMinuteChartService _sinaMinute;

    public LimitUpReviewService(AppDbContext db, EastMoneyHotRankService hotRank, SinaMinuteChartService sinaMinute)
    {
        _db = db;
        _hotRank = hotRank;
        _sinaMinute = sinaMinute;
    }

    public async Task<LimitUpReviewResult> AnalyzeAsync(DateTime tradeDate, CancellationToken ct = default)
    {
        var selected = tradeDate.Date;
        var today = DateTime.Today;
        var now = DateTime.Now;
        // 选中「今天」且当前早于收盘：涨停名单用前一交易日，分时/日线计分用「今天」实时
        var intradayTodayMode = selected == today && !IsAfterAshareCloseLocal(now);
        var limitUpDate = intradayTodayMode ? PreviousTradingDay(selected) : selected;
        var sessionDate = intradayTodayMode ? today : selected;
        var sessionDayEnd = sessionDate.AddDays(1);
        var limitDayEnd = limitUpDate.AddDays(1);
        var lookbackStart = (limitUpDate < sessionDate ? limitUpDate : sessionDate).AddDays(-28);
        var queryEndExclusive = limitDayEnd > sessionDayEnd ? limitDayEnd : sessionDayEnd;

        var histRaw = await _db.StockLimitUpAnalysis
            .AsNoTracking()
            .Where(x => x.analysis_date >= lookbackStart && x.analysis_date < queryEndExclusive)
            .ToListAsync(ct);

        var raw = histRaw.Where(x => x.analysis_date >= limitUpDate && x.analysis_date < limitDayEnd).ToList();

        if (raw.Count == 0)
        {
            var emptyMsg = intradayTodayMode
                ? $"【{sessionDate:yyyy-MM-dd} 盘中】前一交易日 {limitUpDate:yyyy-MM-dd} 涨停表无数据，无法对照分析。请先同步昨日涨停，或待今日 15:00 后再分析当日涨停。"
                : $"【{selected:yyyy-MM-dd}】涨停表无数据。请先在「数据管理」中同步当日涨停，或确认财联社接口是否有该日数据。";
            return new LimitUpReviewResult(
                sessionDate,
                Array.Empty<LimitUpReviewPlateSummary>(),
                Array.Empty<LimitUpReviewStockRow>(),
                emptyMsg,
                Array.Empty<LimitUpPlatePersistenceRow>(),
                null);
        }

        var sessionContextBanner = intradayTodayMode
            ? $"【盘中模式】涨停名单：{limitUpDate:yyyy-MM-dd}（前一交易日）；分时与日 K 计分：{sessionDate:yyyy-MM-dd}（当日实时）。"
            : null;

        var hotByCode = await _hotRank.GetPopularityRankByCode6Async(1000, ct);

        var merged = MergeLimitRowsForDay(raw);

        var codes6 = merged.Select(m => m.Code6).Distinct(StringComparer.Ordinal).ToList();

        var dailyRows = await _db.StockDailyData
            .AsNoTracking()
            .Where(d => d.TradeDate >= sessionDate && d.TradeDate < sessionDayEnd && codes6.Contains(d.StockCode))
            .Select(d => new { d.StockCode, d.Amount, d.TurnoverRate, d.ChangePercent, d.ClosePrice, d.OpenPrice, d.HighPrice, d.LowPrice })
            .ToListAsync(ct);

        var dailyByCode = dailyRows
            .GroupBy(d => d.StockCode, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var plateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var m in merged)
        {
            var primary = GetPrimaryPlate(m.Plates);
            plateCounts[primary] = plateCounts.GetValueOrDefault(primary) + 1;
        }

        var leadByPlate = new Dictionary<string, (string Code6, string Name)>(StringComparer.Ordinal);
        foreach (var g in merged.GroupBy(m => GetPrimaryPlate(m.Plates), StringComparer.Ordinal))
        {
            var best = g
                .OrderBy(x => ParseTimeForSort(x.FirstSealTime))
                .ThenBy(x => x.Code6, StringComparer.Ordinal)
                .First();
            leadByPlate[g.Key] = (best.Code6, best.Name);
        }

        var amountOrdered = merged
            .Select(m =>
            {
                dailyByCode.TryGetValue(m.Code6, out var d);
                return (m, Amount: d?.Amount ?? 0m);
            })
            .OrderByDescending(x => x.Amount)
            .ToList();

        var rankByCode = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < amountOrdered.Count; i++)
            rankByCode[amountOrdered[i].m.Code6] = i + 1;

        var n = Math.Max(1, merged.Count);

        var minuteByCode = new ConcurrentDictionary<string, (decimal? Score, string Note)>(StringComparer.Ordinal);
        await Parallel.ForEachAsync(
            merged,
            new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = ct },
            async (m, _) =>
            {
                try
                {
                    var (data, err) = await _sinaMinute.GetMinuteChartDataAsync(m.Code6, 1, 241).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(err) || data.Count == 0)
                    {
                        minuteByCode[m.Code6] = (null, string.IsNullOrEmpty(err) ? "分时无数据" : $"分时:{err}");
                        return;
                    }

                    var bars = LimitUpMinuteQualityAnalyzer.FilterBarsForTradeDate(data, sessionDate);
                    if (!dailyByCode.TryGetValue(m.Code6, out var d) || d.OpenPrice <= 0 || d.HighPrice <= 0)
                    {
                        minuteByCode[m.Code6] = (null, "无日线 OHLC，未计分时质量分");
                        return;
                    }

                    var (mqScore, mqSummary) = LimitUpMinuteQualityAnalyzer.Score(bars, d.OpenPrice, d.HighPrice, d.LowPrice, sessionDate);
                    minuteByCode[m.Code6] = (mqScore, mqSummary);
                }
                catch (Exception ex)
                {
                    ErrorLogger.Log(ex, nameof(LimitUpReviewService), $"分时拉取 {m.Code6}");
                    minuteByCode[m.Code6] = (null, $"分时异常:{ex.Message}");
                }
            }).ConfigureAwait(false);

        var stockRows = new List<LimitUpReviewStockRow>(merged.Count);
        foreach (var m in merged)
        {
            dailyByCode.TryGetValue(m.Code6, out var d);
            var hasHot = hotByCode.TryGetValue(m.Code6, out var hot);
            int? hotNullable = hasHot ? hot : null;

            var timeScore = ScoreSealTime(m.FirstSealTime);
            var hotScore = ScoreHotRank(hotNullable);
            var rank = rankByCode.GetValueOrDefault(m.Code6, n);
            var liqScore = 100m * (1m - (rank - 1m) / n);
            var turn = d?.TurnoverRate;
            var turnScore = ScoreTurnoverSweetSpot(turn);

            minuteByCode.TryGetValue(m.Code6, out var mq);
            var minuteScore = mq.Score;

            decimal composite;
            if (minuteScore.HasValue)
            {
                composite = Math.Round(
                    0.30m * timeScore + 0.24m * hotScore + 0.16m * liqScore + 0.12m * turnScore + 0.18m * minuteScore.Value,
                    2, MidpointRounding.AwayFromZero);
            }
            else
            {
                composite = Math.Round(
                    0.35m * timeScore + 0.30m * hotScore + 0.20m * liqScore + 0.15m * turnScore,
                    2, MidpointRounding.AwayFromZero);
            }

            var (role, tips) = Classify(composite, hotNullable, turn, m.FirstSealTime, minuteScore, mq.Note);

            var closeRef = d?.ClosePrice ?? 0m;
            decimal? pctForRow = null;
            if (m.PctChg is { } clsPct)
                pctForRow = PercentPointNormalization.ToChangePercentPoints(clsPct, closeRef);
            else if (d?.ChangePercent is { } dailyPct)
                pctForRow = PercentPointNormalization.ToChangePercentPoints(dailyPct, closeRef);

            stockRows.Add(new LimitUpReviewStockRow(
                m.Code6,
                m.Name,
                m.Plates,
                m.FirstSealTime,
                pctForRow,
                turn,
                d?.Amount,
                hotNullable,
                composite,
                role,
                tips,
                d?.ClosePrice,
                minuteScore,
                mq.Note));
        }

        stockRows = stockRows.OrderByDescending(r => r.CompositeScore).ThenBy(r => r.HotRank ?? int.MaxValue).ToList();

        var plateHotspots = plateCounts
            .OrderByDescending(kv => kv.Value)
            .Take(15)
            .Select(kv =>
            {
                leadByPlate.TryGetValue(kv.Key, out var lead);
                return new LimitUpReviewPlateSummary(
                    kv.Key,
                    kv.Value,
                    lead.Code6,
                    lead.Name);
            })
            .ToList();

        var persistenceRows = BuildPlatePersistenceOutlook(histRaw, sessionDate, plateCounts);
        var tomorrowFocus = BuildTomorrowWatchHeuristic(persistenceRows, plateHotspots, stockRows, histRaw, sessionDate);

        var listDayLabel = intradayTodayMode ? $"名单日({limitUpDate:MM-dd})" : "今日";
        var narrative = BuildNarrative(
            sessionDate,
            plateHotspots,
            stockRows,
            dailyByCode.Count,
            hotByCode.Count,
            persistenceRows,
            tomorrowFocus,
            sessionContextBanner,
            listDayLabel);

        return new LimitUpReviewResult(sessionDate, plateHotspots, stockRows, narrative, persistenceRows, tomorrowFocus);
    }

    /// <summary>A 股常规收盘后（本地时间严格晚于 15:00）。</summary>
    private static bool IsAfterAshareCloseLocal(DateTime nowLocal) =>
        nowLocal.TimeOfDay > new TimeSpan(15, 0, 0);

    private static DateTime PreviousTradingDay(DateTime fromDate)
    {
        var d = fromDate.AddDays(-1).Date;
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            d = d.AddDays(-1);
        return d;
    }

    private sealed record MergedLimitRow(string Code6, string Name, string Plates, string? FirstSealTime, decimal? PctChg);

    private static List<MergedLimitRow> MergeLimitRowsForDay(IEnumerable<StockLimitUpAnalysis> dayRows) =>
        dayRows
            .GroupBy(r => ToCode6(r.code), StringComparer.Ordinal)
            .Select(g =>
            {
                var rows = g.ToList();
                var name = rows.Select(x => x.name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "";
                var plates = string.Join("、",
                    rows.Select(x => x.plate_name).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal));
                var bestTime = rows
                    .Select(x => x.first_limit_up_time)
                    .Where(t => !string.IsNullOrWhiteSpace(t) && t != "--")
                    .OrderBy(t => ParseTimeForSort(t!))
                    .FirstOrDefault();
                var pct = rows.Max(x => x.pct_chg);
                return new MergedLimitRow(g.Key, name, plates, bestTime, pct);
            })
            .ToList();

    private const int PersistenceWindowDays = 5;

    private static IReadOnlyList<LimitUpPlatePersistenceRow> BuildPlatePersistenceOutlook(
        IReadOnlyList<StockLimitUpAnalysis> histRaw,
        DateTime tradeDate,
        Dictionary<string, int> todayPlateCounts)
    {
        var windowDates = histRaw
            .Select(x => x.analysis_date.Date)
            .Where(d => d <= tradeDate.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .Take(PersistenceWindowDays)
            .ToList();

        if (windowDates.Count == 0)
            return Array.Empty<LimitUpPlatePersistenceRow>();

        var plateDays = new Dictionary<string, HashSet<DateTime>>(StringComparer.Ordinal);
        var plateTotals = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var d in windowDates)
        {
            var mergedDay = MergeLimitRowsForDay(histRaw.Where(x => x.analysis_date.Date == d));
            foreach (var m in mergedDay)
            {
                var p = GetPrimaryPlate(m.Plates);
                if (!plateDays.TryGetValue(p, out var set))
                {
                    set = new HashSet<DateTime>();
                    plateDays[p] = set;
                }

                set.Add(d);
                plateTotals[p] = plateTotals.GetValueOrDefault(p) + 1;
            }
        }

        var rows = plateTotals.Keys.Select(plate =>
        {
            var days = plateDays.TryGetValue(plate, out var s) ? s.Count : 0;
            var total = plateTotals[plate];
            var todayN = todayPlateCounts.GetValueOrDefault(plate);
            var score = days * 22m + Math.Min(total, 45) * 1.1m + (todayN >= 3 ? 18m : todayN >= 2 ? 10m : 0m);
            return new LimitUpPlatePersistenceRow(plate, days, total, todayN, Math.Round(score, 2, MidpointRounding.AwayFromZero));
        }).OrderByDescending(r => r.HeuristicScore).ThenByDescending(r => r.DaysPresentInWindow).ToList();

        return rows.Take(20).ToList();
    }

    private static LimitUpTomorrowFocusRow? BuildTomorrowWatchHeuristic(
        IReadOnlyList<LimitUpPlatePersistenceRow> persistence,
        IReadOnlyList<LimitUpReviewPlateSummary> plateHotspots,
        IReadOnlyList<LimitUpReviewStockRow> stockRows,
        IReadOnlyList<StockLimitUpAnalysis> histRaw,
        DateTime tradeDate)
    {
        if (stockRows.Count == 0)
            return null;

        var windowDates = histRaw
            .Select(x => x.analysis_date.Date)
            .Where(d => d <= tradeDate.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .Take(PersistenceWindowDays)
            .ToList();

        var primary = persistence.FirstOrDefault()?.PlateName
                      ?? plateHotspots.FirstOrDefault()?.PlateName
                      ?? "未分类";

        var crossLeader = FindCrossDayPlateLeader(histRaw, windowDates, primary);

        var rationale = new StringBuilder();
        var topP = persistence.FirstOrDefault();
        if (topP != null)
            rationale.Append(
                $"近{PersistenceWindowDays}个有数据的交易日窗口内，「{topP.PlateName}」在 {topP.DaysPresentInWindow} 天出现涨停股、累计 {topP.TotalLimitUpStocksInWindow} 只次；名单交易日该题材涨停约 {topP.TodayLimitUpCount} 家。");
        else
            rationale.Append("近窗数据不足，以下仅按当日综合分与首封结构给出观察参考。");

        rationale.Append(" 以下为**次日观察用启发式筛选**，不代表涨跌预测或投资建议。");

        if (crossLeader.HasValue)
            rationale.Append(
                $" 题材内「多日反复出现在涨停名单」的代码：{crossLeader.Value.Name}({crossLeader.Value.Code6})，近窗出现 {crossLeader.Value.DayHits} 天（非基本面龙头认定）。");

        var picks = new List<LimitUpWatchPick>();
        var inPlate = stockRows
            .Where(s => string.Equals(GetPrimaryPlate(s.Plates), primary, StringComparison.Ordinal)
                        || (!string.IsNullOrWhiteSpace(s.Plates) && s.Plates.Contains(primary, StringComparison.Ordinal)))
            .OrderByDescending(s => s.CompositeScore)
            .Take(3)
            .ToList();

        foreach (var s in inPlate)
            picks.Add(new LimitUpWatchPick(s.Code6, s.Name, $"属「{primary}」且当日综合分 {s.CompositeScore:0.#}（{s.Role}）"));

        foreach (var s in stockRows.OrderByDescending(s => s.CompositeScore))
        {
            if (picks.Any(p => p.Code6 == s.Code6)) continue;
            picks.Add(new LimitUpWatchPick(s.Code6, s.Name, "当日全市场综合分领先（题材可交叉验证）"));
            if (picks.Count >= 5) break;
        }

        return new LimitUpTomorrowFocusRow(primary, rationale.ToString(), picks);
    }

    private static (string Code6, string Name, int DayHits)? FindCrossDayPlateLeader(
        IReadOnlyList<StockLimitUpAnalysis> histRaw,
        List<DateTime> windowDates,
        string plate)
    {
        if (string.IsNullOrWhiteSpace(plate) || plate == "未分类")
            return null;

        var codeDays = new Dictionary<string, (HashSet<DateTime> Days, string Name)>(StringComparer.Ordinal);
        foreach (var d in windowDates)
        {
            foreach (var m in MergeLimitRowsForDay(histRaw.Where(x => x.analysis_date.Date == d)))
            {
                if (!string.Equals(GetPrimaryPlate(m.Plates), plate, StringComparison.Ordinal))
                    continue;
                if (!codeDays.TryGetValue(m.Code6, out _))
                    codeDays[m.Code6] = (new HashSet<DateTime>(), m.Name ?? "");
                var e = codeDays[m.Code6];
                e.Days.Add(d);
                if (!string.IsNullOrWhiteSpace(m.Name))
                    codeDays[m.Code6] = (e.Days, m.Name);
            }
        }

        var best = codeDays.OrderByDescending(kv => kv.Value.Days.Count).ThenByDescending(kv => kv.Key).FirstOrDefault();
        if (codeDays.Count == 0 || string.IsNullOrEmpty(best.Key) || best.Value.Days.Count < 2)
            return null;
        return (best.Key, best.Value.Name, best.Value.Days.Count);
    }

    private static string BuildNarrative(
        DateTime date,
        IReadOnlyList<LimitUpReviewPlateSummary> plates,
        IReadOnlyList<LimitUpReviewStockRow> stocks,
        int dailyMatchCount,
        int hotRankCount,
        IReadOnlyList<LimitUpPlatePersistenceRow> persistence,
        LimitUpTomorrowFocusRow? tomorrow,
        string? sessionContextBanner,
        string persistenceListCountLabel)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"【{date:yyyy-MM-dd} 涨停复盘摘要】");
        if (!string.IsNullOrWhiteSpace(sessionContextBanner))
        {
            sb.AppendLine();
            sb.AppendLine(sessionContextBanner);
        }

        sb.AppendLine();
        sb.AppendLine("一、数据说明");
        sb.AppendLine("- 涨停/题材/首封时间：来自「涨停表」同步数据（财联社 cls 接口）。");
        sb.AppendLine("- 换手率、成交额(万)：来自「日线」同日数据（用于热度/流动性交叉验证；涨停表内 turn 字段并非可靠换手）。");
        sb.AppendLine("- 热度排名：东方财富 xuangu 热度榜（数值越小越热）。");
        var scored = stocks.Count(s => s.MinuteQualityScore.HasValue);
        sb.AppendLine(
            $"- 分时质量：{scored}/{stocks.Count} 已按新浪 1 分钟 K 线+当日日线 OHLC 自动计分（封板段数、VWAP 与分时 MA5 上方占比、量价与尾/首段量能比、日内振幅），并折入综合分；其余股票见表格「分时备注」。");
        sb.AppendLine($"- 当日匹配到日线的股票数：{dailyMatchCount}；热度榜返回条目数：{hotRankCount}。");
        sb.AppendLine();

        sb.AppendLine("二、今日热点题材（按涨停家数）");
        if (plates.Count == 0)
            sb.AppendLine("（无）");
        else
        {
            foreach (var p in plates)
            {
                var lead = string.IsNullOrWhiteSpace(p.LeadCandidateCode6)
                    ? ""
                    : $" 龙头候选：{p.LeadCandidateName}({p.LeadCandidateCode6})";
                sb.AppendLine($"- {p.PlateName}：涨停 {p.LimitUpCount} 家{lead}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("三、明日可重点跟踪（启发式综合分 Top）");
        foreach (var s in stocks.Take(8))
        {
            sb.AppendLine(
                $"- {s.Name}({s.Code6}) 分={s.CompositeScore:0.#} 角色={s.Role} 分时={(s.MinuteQualityScore?.ToString("0.#", CultureInfo.InvariantCulture) ?? "-")} 热度={(s.HotRank?.ToString(CultureInfo.InvariantCulture) ?? "-")} 首封={s.FirstSealTime ?? "-"} 日换手={(s.DailyTurnPct?.ToString("0.##", CultureInfo.InvariantCulture) ?? "-")}%  {s.Tips}");
        }

        sb.AppendLine();
        sb.AppendLine("四、近窗题材持续性（约最近 5 个「库中有涨停记录」的交易日，含当日）");
        sb.AppendLine("- 指标：题材在窗口内出现天数、窗口内涨停股「只次」合计、名单交易日该题材涨停家数、启发式得分（越高仅表示题材在涨停维度更活跃/更连贯，不代表明日涨跌）。");
        if (persistence.Count == 0)
            sb.AppendLine("（无足够历史窗口数据）");
        else
        {
            foreach (var r in persistence.Take(12))
            {
                sb.AppendLine(
                    $"- {r.PlateName}：{r.DaysPresentInWindow} 天有票、窗内合计 {r.TotalLimitUpStocksInWindow} 只次、{persistenceListCountLabel}约 {r.TodayLimitUpCount} 家、得分 {r.HeuristicScore:0.#}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("五、次日观察参考（题材 + 个股启发式，非预测）");
        if (tomorrow == null)
            sb.AppendLine("（无）");
        else
        {
            sb.AppendLine($"- 优先观察题材：**{tomorrow.PrimarySector}**");
            sb.AppendLine($"- 依据摘要：{tomorrow.Rationale}");
            sb.AppendLine("- 个股清单（按程序规则拼接，请自行交叉验证基本面与公告）：");
            foreach (var p in tomorrow.Picks)
                sb.AppendLine($"  · {p.Name}({p.Code6})：{p.Reason}");
        }

        sb.AppendLine();
        sb.AppendLine("六、使用建议");
        sb.AppendLine("- 「涨停定身份、分时定成色、热度定地位」：表格分数只是粗筛，次日竞价与开盘走势仍需结合盘面。");
        sb.AppendLine("- 若热度高但分时烂板，常见为分歧或出货，谨慎接力。");
        sb.AppendLine("- 持续性统计依赖历史涨停表是否已同步；缺日时窗口会短于 5 个交易日。");

        return sb.ToString();
    }

    private static (string Role, string Tips) Classify(
        decimal composite,
        int? hotRank,
        decimal? dailyTurn,
        string? sealTime,
        decimal? minuteQualityScore,
        string? minuteQualityNote)
    {
        string tips;
        if (hotRank is > 0 and <= 30 && composite >= 72m)
            tips = "热度与分数共振，优先放入自选跟踪。";
        else if (hotRank is null or 0 or > 200)
            tips = "热度不在前列或缺失：可能是分支/独立逻辑或数据缺失，谨慎。";
        else
            tips = "中等关注度：结合板块地位与分时再定。";

        if (dailyTurn.HasValue && (dailyTurn.Value < 5m || dailyTurn.Value > 35m))
            tips += " 日换手偏离常见活跃区，注意流动性与筹码交换。";

        if (string.IsNullOrWhiteSpace(sealTime) || sealTime == "--")
            tips += " 首封时间缺失，龙头辨识度打折。";

        if (minuteQualityScore is < 58m)
            tips += " 自动分时质量偏弱。";
        if (!minuteQualityScore.HasValue && !string.IsNullOrWhiteSpace(minuteQualityNote))
            tips += " " + minuteQualityNote;

        string role;
        if (composite >= 78m && hotRank is > 0 and <= 50) role = "核心关注";
        else if (composite >= 65m) role = "重点观察";
        else if (composite >= 52m) role = "跟风/补涨";
        else role = "后排";

        return (role, tips.Trim());
    }

    private static decimal ScoreSealTime(string? t)
    {
        if (string.IsNullOrWhiteSpace(t) || t == "--") return 55m;
        var m = ParseTimeForSort(t);
        if (m <= 9 * 60 + 45) return 100m;
        if (m <= 10 * 60 + 30) return 88m;
        if (m <= 11 * 60 + 30) return 78m;
        if (m <= 13 * 60) return 68m;
        if (m <= 15 * 60) return 58m;
        return 45m;
    }

    private static decimal ScoreHotRank(int? rank)
    {
        if (rank is null or <= 0) return 45m;
        var r = rank.Value;
        if (r <= 20) return 100m;
        if (r <= 50) return 88m;
        if (r <= 100) return 78m;
        if (r <= 200) return 68m;
        if (r <= 500) return 55m;
        return 40m;
    }

    private static decimal ScoreTurnoverSweetSpot(decimal? turnPct)
    {
        if (!turnPct.HasValue) return 60m;
        var t = turnPct.Value;
        if (t >= 8m && t <= 20m) return 100m;
        if (t >= 5m && t < 8m) return 78m;
        if (t > 20m && t <= 35m) return 72m;
        if (t > 35m) return 45m;
        return 50m;
    }

    private static int ParseTimeForSort(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return int.MaxValue;
        t = t.Trim();
        var parts = t.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return int.MaxValue;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hh)) return int.MaxValue;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mm)) return int.MaxValue;
        return hh * 60 + mm;
    }

    private static string GetPrimaryPlate(string? plates)
    {
        if (string.IsNullOrWhiteSpace(plates)) return "未分类";
        return plates.Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "未分类";
    }

    private static string ToCode6(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var c = code.Trim().ToLowerInvariant();
        c = c.Replace(".sh", "", StringComparison.Ordinal).Replace(".sz", "", StringComparison.Ordinal).Replace(".bj", "", StringComparison.Ordinal);
        c = c.Replace("sh", "", StringComparison.Ordinal).Replace("sz", "", StringComparison.Ordinal).Replace("bj", "", StringComparison.Ordinal);
        var digits = new string(c.Where(char.IsDigit).ToArray());
        if (digits.Length >= 6) return digits[^6..];
        if (digits.Length > 0) return digits.PadLeft(6, '0');
        return "";
    }
}
