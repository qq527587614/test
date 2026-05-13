using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.RealtimeData;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.Core.Services;

public sealed class HotPlateRealtimePickOptions
{
    /// <summary>分析日：默认可选「今天」；盘中用当日涨停 + 实时/分时。</summary>
    public DateTime SessionDate { get; init; } = DateTime.Today;

    /// <summary>热点板块数量上限（由当日涨停分布 + 板块内实时涨幅加权排序）。</summary>
    public int MaxHotPlates { get; init; } = 8;

    /// <summary>进入分时/打分流程的候选股上限（性能保护）。</summary>
    public int MaxCandidatesToAnalyze { get; init; } = 48;

    /// <summary>最终返回条数上限。</summary>
    public int MaxResults { get; init; } = 15;

    public int MaxDegreeOfParallelism { get; init; } = 5;
}

public sealed class HotPlateRealtimePickRow
{
    public string Code6 { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>与热点集合匹配的题材（来自近窗涨停记录）。</summary>
    public string MatchedPlate { get; init; } = "";
    public DateTime LastLimitUpDate { get; init; }
    public int LimitUpDayCountIn30 { get; init; }
    public decimal DailyScore { get; init; }
    public decimal? MinuteScore { get; init; }
    public string? MinuteNote { get; init; }
    /// <summary>当前涨幅（%）：优先腾讯实时；无则取分析日日线涨跌幅。</summary>
    public decimal? RealtimeChangePct { get; init; }

    /// <summary>当前价：优先腾讯现价；无则取分析日日线收盘口径。</summary>
    public decimal? CurrentPrice { get; init; }

    public int? HotRank { get; init; }
    public decimal Composite { get; init; }
    public string BuyTag { get; init; } = "";
    public string Rationale { get; init; } = "";
}

public sealed record HotPlateRealtimePickResult(
    DateTime SessionDate,
    IReadOnlyList<string> HotPlates,
    string Narrative,
    IReadOnlyList<HotPlateRealtimePickRow> Rows);

/// <summary>
/// 热门板块实时分析选股：当日涨停表推断热点题材 +（可选）题材内实时涨幅加权；
/// 在近 30 个自然日涨停过的成分中筛股，结合日线位置、新浪分时质量、腾讯涨跌幅与东财热度做启发式排序。
/// 剔除 ST 股及 ST 板块、其他/其它等归类板块，不构成投资建议。
/// </summary>
public sealed class HotPlateRealtimePickService
{
    private readonly AppDbContext _db;
    private readonly SinaMinuteChartService _sinaMinute;
    private readonly TencentRealtimeService _tencent;
    private readonly EastMoneyHotRankService _hotRank;

    public HotPlateRealtimePickService(
        AppDbContext db,
        SinaMinuteChartService sinaMinute,
        TencentRealtimeService tencent,
        EastMoneyHotRankService hotRank)
    {
        _db = db;
        _sinaMinute = sinaMinute;
        _tencent = tencent;
        _hotRank = hotRank;
    }

    public async Task<HotPlateRealtimePickResult> AnalyzeAsync(
        HotPlateRealtimePickOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var session = options.SessionDate.Date;
        if (session > DateTime.Today)
            session = DateTime.Today;

        var sessionEnd = session.AddDays(1);
        var windowStart = session.AddDays(-30);

        progress?.Report("读取当日涨停并统计热点题材…");
        var todayRaw = await _db.StockLimitUpAnalysis.AsNoTracking()
            .Where(x => x.analysis_date >= session && x.analysis_date < sessionEnd)
            .ToListAsync(ct);

        var mergedToday = MergeLimitRowsForDay(todayRaw)
            .Where(m => !IsStStockName(m.Name))
            .ToList();
        if (mergedToday.Count == 0)
        {
            var msg =
                $"【{session:yyyy-MM-dd}】当日涨停表无有效标的（剔除 ST 股后为空），或原始无数据。请同步涨停或换日。";
            return new HotPlateRealtimePickResult(session, Array.Empty<string>(), msg, Array.Empty<HotPlateRealtimePickRow>());
        }

        var plateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var m in mergedToday)
        {
            var p = GetPrimaryPlate(m.Plates);
            if (IsExcludedHotPlate(p))
                continue;
            plateCounts[p] = plateCounts.GetValueOrDefault(p) + 1;
        }

        var plateAvgChg = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (session == DateTime.Today.Date)
        {
            progress?.Report("拉取当日涨停股腾讯实时涨幅，计算题材内平均涨跌（板块实时维度）…");
            var codes = mergedToday.Select(x => x.Code6).Distinct(StringComparer.Ordinal).ToList();
            var rt = await FetchTencentByCode6Async(codes, ct).ConfigureAwait(false);
            var sum = new Dictionary<string, (decimal Sum, int N)>(StringComparer.Ordinal);
            foreach (var m in mergedToday)
            {
                if (!rt.TryGetValue(m.Code6, out var row))
                    continue;
                var plate = GetPrimaryPlate(m.Plates);
                if (IsExcludedHotPlate(plate))
                    continue;
                var e = sum.GetValueOrDefault(plate);
                e.Sum += row.ChangePercent;
                e.N += 1;
                sum[plate] = e;
            }

            foreach (var kv in sum)
            {
                if (kv.Value.N > 0)
                    plateAvgChg[kv.Key] = Math.Round(kv.Value.Sum / kv.Value.N, 3, MidpointRounding.AwayFromZero);
            }
        }

        var rankedPlates = plateCounts.Keys
            .Where(p => !IsExcludedHotPlate(p))
            .Select(p => (Name: p, Count: plateCounts[p], AvgChg: plateAvgChg.GetValueOrDefault(p, 0m)))
            .OrderByDescending(x => x.Count * 50m + x.AvgChg)
            .Take(Math.Max(1, options.MaxHotPlates))
            .Select(x => x.Name)
            .ToList();

        var hotSet = new HashSet<string>(rankedPlates, StringComparer.Ordinal);
        if (hotSet.Count == 0)
        {
            var fb = plateCounts
                .Where(kv => !IsExcludedHotPlate(kv.Key))
                .OrderByDescending(kv => kv.Value)
                .Select(kv => kv.Key)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(fb))
            {
                var msg =
                    $"【{session:yyyy-MM-dd}】剔除 ST 板块、其他/其它等归类板块后，无可用热点题材。请检查涨停表题材字段或换日。";
                return new HotPlateRealtimePickResult(session, Array.Empty<string>(), msg, Array.Empty<HotPlateRealtimePickRow>());
            }

            hotSet.Add(fb);
            rankedPlates = new List<string> { fb };
        }

        progress?.Report("扫描近 30 日涨停记录，匹配热点题材成分…");
        var windowRows = await _db.StockLimitUpAnalysis.AsNoTracking()
            .Where(x => x.analysis_date >= windowStart && x.analysis_date < sessionEnd)
            .Select(x => new { x.code, x.plate_name, x.name, d = x.analysis_date })
            .ToListAsync(ct);

        var byCode = windowRows
            .GroupBy(x => ToCode6(x.code), StringComparer.Ordinal)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToList();

        var candidates = new List<CandidateState>();
        foreach (var g in byCode)
        {
            var code6 = g.Key;
            if (g.Any(x => IsStStockName(x.name)))
                continue;

            var names = g.Select(x => x.name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            var name = names.FirstOrDefault() ?? "";
            var plateParts = g.Select(x => x.plate_name ?? "")
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .SelectMany(p => p.Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var mergedPlates = string.Join("、", plateParts);
            var primary = GetPrimaryPlate(mergedPlates);
            if (!PlateHitsHot(plateParts, primary, hotSet))
                continue;

            var distinctDays = g.Select(x => x.d.Date).Distinct().Count();
            var lastD = g.Max(x => x.d.Date);
            var matched = plateParts.FirstOrDefault(p => hotSet.Contains(p)) ?? primary;
            candidates.Add(new CandidateState(code6, name, matched, lastD, distinctDays));
        }

        if (candidates.Count == 0)
        {
            var msg =
                $"近 30 日内无与热点题材（{string.Join("、", rankedPlates)}）相交的涨停记录。可尝试扩大同步范围或换交易日。";
            return new HotPlateRealtimePickResult(session, rankedPlates, msg, Array.Empty<HotPlateRealtimePickRow>());
        }

        candidates = candidates
            .OrderByDescending(c => c.LimitUpDayCountIn30)
            .ThenByDescending(c => c.LastLimitUpDate)
            .Take(Math.Max(1, options.MaxCandidatesToAnalyze))
            .ToList();

        var codeList = candidates.Select(c => c.Code6).ToList();
        progress?.Report($"批量加载 {codeList.Count} 只股票的日线（用于位置分）…");
        var dailyAll = await _db.StockDailyData.AsNoTracking()
            .Where(d => codeList.Contains(d.StockCode) && d.TradeDate >= session.AddDays(-130) && d.TradeDate <= session)
            .OrderBy(d => d.StockCode)
            .ThenBy(d => d.TradeDate)
            .ToListAsync(ct);
        var dailyByCode = dailyAll.GroupBy(d => d.StockCode, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.TradeDate).ToList(), StringComparer.Ordinal);

        progress?.Report("东财热度榜…");
        var hotByCode = await _hotRank.GetPopularityRankByCode6Async(1000, ct).ConfigureAwait(false);

        progress?.Report("腾讯实时（候选全集）…");
        var realtimeMap = await FetchTencentByCode6Async(codeList, ct).ConfigureAwait(false);

        var rowsOut = new ConcurrentBag<HotPlateRealtimePickRow>();
        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism, CancellationToken = ct },
            async (c, _) =>
            {
                try
                {
                    dailyByCode.TryGetValue(c.Code6, out var bars);
                    bars ??= new List<StockDailyData>();
                    var dailyScore = ScoreDailyPosition(bars, session);
                    realtimeMap.TryGetValue(c.Code6, out var rt);
                    var displayName = string.IsNullOrWhiteSpace(c.Name) ? (rt?.StockName ?? "") : c.Name;
                    if (IsStStockName(displayName))
                        return;

                    decimal? minuteScore = null;
                    string? minuteNote = null;
                    var open = 0m;
                    var high = 0m;
                    var low = 0m;
                    var dayBar = bars.LastOrDefault(b => b.TradeDate.Date == session.Date);
                    if (dayBar != null && dayBar.OpenPrice > 0)
                    {
                        open = dayBar.OpenPrice;
                        high = dayBar.HighPrice;
                        low = dayBar.LowPrice;
                    }
                    else if (rt != null && rt.OpenPrice > 0)
                    {
                        open = rt.OpenPrice;
                        high = rt.HighPrice;
                        low = rt.LowPrice;
                    }

                    if (open > 0 && high > 0 && low > 0)
                    {
                        var (data, err) = await _sinaMinute.GetMinuteChartDataAsync(c.Code6, 1, 241).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(err) || data.Count == 0)
                            minuteNote = string.IsNullOrEmpty(err) ? "分时无数据" : $"分时:{err}";
                        else
                        {
                            var filtered = LimitUpMinuteQualityAnalyzer.FilterBarsForTradeDate(data, session);
                            var (mq, note) = LimitUpMinuteQualityAnalyzer.Score(filtered, open, high, low, session);
                            minuteScore = mq;
                            minuteNote = note;
                        }
                    }
                    else
                        minuteNote = "无有效 OHLC，未计分时";

                    var hotRank = hotByCode.TryGetValue(c.Code6, out var hr) ? hr : (int?)null;
                    var hotScore = ScoreHotRank(hotRank);
                    var rtSweet = ScoreRealtimeSweet(rt?.ChangePercent);
                    var minutePart = minuteScore ?? 58m;
                    var composite = Math.Round(
                        0.40m * dailyScore + 0.34m * minutePart + 0.16m * rtSweet + 0.10m * hotScore,
                        2,
                        MidpointRounding.AwayFromZero);
                    composite = Math.Clamp(composite, 0m, 100m);

                    var (tag, why) = BuildBuyHint(composite, rt?.ChangePercent, dailyScore, minuteScore);

                    decimal? displayPx = rt is { CurrentPrice: > 0 } ? rt.CurrentPrice : null;
                    decimal? displayPct = rt?.ChangePercent;
                    if (dayBar != null)
                    {
                        if (displayPx is null or <= 0)
                        {
                            var cl = dayBar.CurrentPrice ?? dayBar.ClosePrice;
                            if (cl > 0)
                                displayPx = cl;
                        }

                        displayPct ??= dayBar.ChangePercent;
                    }

                    rowsOut.Add(new HotPlateRealtimePickRow
                    {
                        Code6 = c.Code6,
                        Name = displayName,
                        MatchedPlate = c.MatchedPlate,
                        LastLimitUpDate = c.LastLimitUpDate,
                        LimitUpDayCountIn30 = c.LimitUpDayCountIn30,
                        DailyScore = dailyScore,
                        MinuteScore = minuteScore,
                        MinuteNote = minuteNote,
                        RealtimeChangePct = displayPct,
                        CurrentPrice = displayPx,
                        HotRank = hotRank,
                        Composite = composite,
                        BuyTag = tag,
                        Rationale = why
                    });
                }
                catch (Exception ex)
                {
                    ErrorLogger.Log(ex, nameof(HotPlateRealtimePickService), c.Code6);
                }
            }).ConfigureAwait(false);

        var sorted = rowsOut
            .Where(r => !IsStStockName(r.Name))
            .OrderByDescending(r => r.Composite)
            .ThenByDescending(r => r.LimitUpDayCountIn30)
            .Take(Math.Max(1, options.MaxResults))
            .ToList();

        var narrative = BuildNarrative(session, rankedPlates, mergedToday.Count, candidates.Count, sorted);
        progress?.Report($"完成，输出 {sorted.Count} 条。");
        return new HotPlateRealtimePickResult(session, rankedPlates, narrative, sorted);
    }

    private sealed record CandidateState(
        string Code6,
        string Name,
        string MatchedPlate,
        DateTime LastLimitUpDate,
        int LimitUpDayCountIn30);

    private sealed record MergedLimitRow(string Code6, string Name, string Plates, string? FirstSealTime, decimal? PctChg);

    private static List<MergedLimitRow> MergeLimitRowsForDay(IEnumerable<StockLimitUpAnalysis> dayRows) =>
        dayRows
            .GroupBy(r => ToCode6(r.code), StringComparer.Ordinal)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g =>
            {
                var rows = g.ToList();
                var name = rows.Select(x => x.name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "";
                var plates = string.Join("、",
                    rows.Select(x => x.plate_name).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal));
                var bestTime = rows
                    .Select(x => x.first_limit_up_time)
                    .Where(t => !string.IsNullOrWhiteSpace(t) && t != "--")
                    .OrderBy(ParseTimeForSort)
                    .FirstOrDefault();
                var pct = rows.Max(x => x.pct_chg);
                return new MergedLimitRow(g.Key, name, plates, bestTime, pct);
            })
            .ToList();

    private static bool PlateHitsHot(
        IReadOnlyList<string> plateParts,
        string primary,
        HashSet<string> hotSet)
    {
        foreach (var p in plateParts)
        {
            if (IsExcludedHotPlate(p))
                continue;
            if (hotSet.Contains(p))
                return true;
        }

        if (IsExcludedHotPlate(primary))
            return false;
        if (hotSet.Contains(primary))
            return true;
        return false;
    }

    /// <summary>风险警示股（名称维度，与常见行情展示一致）。</summary>
    private static bool IsStStockName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        if (n.StartsWith("S*ST", StringComparison.OrdinalIgnoreCase)) return true;
        if (n.StartsWith("*ST", StringComparison.OrdinalIgnoreCase)) return true;
        if (n.StartsWith("ST", StringComparison.OrdinalIgnoreCase) && n.Length >= 4) return true;
        return false;
    }

    /// <summary>不参与「热点板块」统计与匹配的题材名（ST 板块、其他归类、风险警示板块名等）。</summary>
    private static bool IsExcludedHotPlate(string? plateName)
    {
        if (string.IsNullOrWhiteSpace(plateName)) return true;
        var p = plateName.Trim();
        if (string.Equals(p, "未分类", StringComparison.Ordinal)) return true;
        if (string.Equals(p, "其他", StringComparison.Ordinal) || string.Equals(p, "其它", StringComparison.Ordinal))
            return true;
        if (string.Equals(p, "其他板块", StringComparison.Ordinal) || string.Equals(p, "其它板块", StringComparison.Ordinal))
            return true;
        if (string.Equals(p, "ST板块", StringComparison.Ordinal) || string.Equals(p, "ST股", StringComparison.Ordinal))
            return true;
        if (p.Contains("ST板块", StringComparison.Ordinal)) return true;
        if (p.Contains("风险警示", StringComparison.Ordinal)) return true;
        if (p.Contains("退市整理", StringComparison.Ordinal)) return true;
        return false;
    }

    private static decimal ScoreDailyPosition(IReadOnlyList<StockDailyData> bars, DateTime sessionDate)
    {
        var ordered = bars
            .Where(b => b.TradeDate.Date <= sessionDate.Date)
            .OrderBy(b => b.TradeDate)
            .ToList();
        if (ordered.Count < 5)
            return 48m;

        static decimal Cl(StockDailyData b) => b.CurrentPrice ?? b.ClosePrice;

        var tail = ordered.Count > 12 ? ordered.TakeLast(12).ToList() : ordered;
        var closes = tail.Select(Cl).ToList();
        if (closes.Count < 5)
            return 50m;

        var ma5 = closes.TakeLast(5).Average();
        var c0 = closes[^1];
        var lastBar = tail[^1];
        var chg = lastBar.ChangePercent;

        decimal score = 46m;
        if (ma5 > 0 && c0 >= ma5 * 0.985m && c0 <= ma5 * 1.065m)
            score += 24m;
        if (chg is >= -2.8m and <= 8.5m)
            score += 20m;
        if (c0 > ma5)
            score += 12m;

        if (closes.Count >= 6)
        {
            var prevSeg = closes.SkipLast(1).ToList();
            if (prevSeg.Count >= 5)
            {
                var prevMa5 = prevSeg.TakeLast(5).Average();
                if (ma5 >= prevMa5)
                    score += 10m;
            }
        }

        return Math.Clamp(Math.Round(score, 2, MidpointRounding.AwayFromZero), 0m, 100m);
    }

    private static decimal ScoreHotRank(int? rank)
    {
        if (rank is null or <= 0) return 52m;
        var r = rank.Value;
        if (r <= 30) return 100m;
        if (r <= 80) return 86m;
        if (r <= 200) return 74m;
        if (r <= 500) return 62m;
        return 48m;
    }

    private static decimal ScoreRealtimeSweet(decimal? chg)
    {
        if (!chg.HasValue) return 62m;
        var c = chg.Value;
        if (c is >= 0.5m and <= 5.5m) return 92m;
        if (c is >= -1.2m and < 0.5m) return 78m;
        if (c is > 5.5m and <= 8.8m) return 68m;
        if (c > 9.5m) return 38m;
        if (c < -3m) return 44m;
        return 58m;
    }

    private static (string Tag, string Why) BuildBuyHint(
        decimal composite,
        decimal? rtChg,
        decimal dailyScore,
        decimal? minuteScore)
    {
        var sb = new StringBuilder();
        sb.Append($"综合分{composite:0.#}（日线位{dailyScore:0.#}");
        if (minuteScore.HasValue)
            sb.Append($"、分时{minuteScore.Value:0.#}");
        sb.Append(')');
        if (rtChg.HasValue)
            sb.Append($"；实时{rtChg.Value:0.##}%");

        var detail = sb.ToString();
        if (composite >= 74m && (!rtChg.HasValue || rtChg.Value <= 7.2m))
            return ("观察低吸", detail + "。偏强但未过度拉升，可列自选、结合盘口与基本面再定是否介入。");
        if (composite >= 66m)
            return ("轻仓试错", detail + "。信号中等，若参与需严格止损与仓位纪律。");
        return ("不建议新建仓", detail + "。分数一般或追涨风险偏高，仅作复盘参考。");
    }

    private static string BuildNarrative(
        DateTime session,
        IReadOnlyList<string> hotPlates,
        int todayLimitMergedCount,
        int candidateCount,
        IReadOnlyList<HotPlateRealtimePickRow> picks)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"【{session:yyyy-MM-dd} 热门板块实时分析选股】");
        sb.AppendLine();
        sb.AppendLine("一、口径说明（必读）");
        sb.AppendLine("- 热点题材：以**当日涨停表**中各股「主题材」出现频次为主；若分析日为今天，则再按**当日涨停股所属题材内腾讯实时涨跌幅均值**做加权排序（与「板块分析」页当日逻辑一致的数据来源维度）。");
        sb.AppendLine("- **剔除规则**：不参与热点统计的板块包括 ST 板块、风险警示/退市整理类题材名，以及「其他」「其它」「其他板块」「其它板块」等归类板块；**ST / *ST / S*ST** 等风险警示证券不进入候选与结果。");
        sb.AppendLine("- 候选池：近 **30 个自然日**内在涨停表出现、且题材与上述热点之一相交的股票（非仅上一交易日）。");
        sb.AppendLine("- 打分：日线位置（贴近 MA5 等）+ 新浪 1 分钟分时质量 + 腾讯实时涨跌偏好 + 东财热度，均为**启发式**，**不构成投资建议**。");
        sb.AppendLine();
        sb.AppendLine("二、当日统计");
        sb.AppendLine($"- 合并后涨停家数（当日表）: {todayLimitMergedCount}");
        sb.AppendLine($"- 推断热点板块: {string.Join("、", hotPlates)}");
        sb.AppendLine($"- 近窗匹配候选: {candidateCount}（已按活跃度截断至分析上限）");
        sb.AppendLine($"- 本页展示 Top: {picks.Count}");
        sb.AppendLine();
        sb.AppendLine("三、使用建议");
        sb.AppendLine("- 表格「推荐」仅为程序规则下的文字标签，请自行核对公告、龙虎榜、流动性。");
        sb.AppendLine("- 若当日涨停表尚未同步，热点与候选会偏空或失真。");
        return sb.ToString();
    }

    private async Task<Dictionary<string, RealtimeStockData>> FetchTencentByCode6Async(
        IReadOnlyList<string> codes6,
        CancellationToken ct)
    {
        var map = new Dictionary<string, RealtimeStockData>(StringComparer.Ordinal);
        if (codes6.Count == 0)
            return map;

        const int batchSize = 80;
        for (var i = 0; i < codes6.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = codes6.Skip(i).Take(batchSize).Select(ToTencentPrefix).ToList();
            var list = await _tencent.GetRealtimeDataAsync(batch).ConfigureAwait(false);
            foreach (var r in list)
            {
                var c6 = SinaMinuteChartService.NormalizeToCode6(r.StockCode);
                if (!string.IsNullOrEmpty(c6))
                    map[c6] = r;
            }
        }

        return map;
    }

    private static string ToTencentPrefix(string code6)
    {
        if (string.IsNullOrWhiteSpace(code6) || code6.Length < 2)
            return "sz" + code6;
        if (code6.StartsWith("60", StringComparison.Ordinal) || code6.StartsWith("68", StringComparison.Ordinal))
            return "sh" + code6;
        if (code6.StartsWith("00", StringComparison.Ordinal) || code6.StartsWith("30", StringComparison.Ordinal))
            return "sz" + code6;
        if (code6.StartsWith("43", StringComparison.Ordinal) || code6.StartsWith("83", StringComparison.Ordinal) ||
            code6.StartsWith("87", StringComparison.Ordinal) || code6.StartsWith("92", StringComparison.Ordinal))
            return "bj" + code6;
        return "sz" + code6;
    }

    private static string GetPrimaryPlate(string? plates)
    {
        if (string.IsNullOrWhiteSpace(plates)) return "未分类";
        return plates.Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
               ?? "未分类";
    }

    private static string ToCode6(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var c = code.Trim().ToLowerInvariant();
        c = c.Replace(".sh", "", StringComparison.Ordinal).Replace(".sz", "", StringComparison.Ordinal)
            .Replace(".bj", "", StringComparison.Ordinal);
        c = c.Replace("sh", "", StringComparison.Ordinal).Replace("sz", "", StringComparison.Ordinal)
            .Replace("bj", "", StringComparison.Ordinal);
        var digits = new string(c.Where(char.IsDigit).ToArray());
        if (digits.Length >= 6) return digits[^6..];
        if (digits.Length > 0) return digits.PadLeft(6, '0');
        return "";
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
}
