using System.Globalization;
using System.Linq;
using StockAnalysisSystem.Core.RealtimeData;

namespace StockAnalysisSystem.Core.Services;

/// <summary>
/// 涨停日分时质量启发式评分（1 分钟 K 线 + 当日日线 OHLC）。
/// 维度：封板段数、分时 VWAP 上方占比、分时 MA5 上方占比、量价与相对量能、日线振幅。
/// </summary>
public static class LimitUpMinuteQualityAnalyzer
{
    /// <param name="bars">已按时间升序、且为同一交易日的 1 分钟 K 线。</param>
    /// <returns>0–100 分与一句中文摘要。</returns>
    public static (decimal Score, string Summary) Score(
        IReadOnlyList<MinuteChartData> bars,
        decimal dailyOpen,
        decimal dailyHigh,
        decimal dailyLow,
        DateTime tradeDate)
    {
        var day = tradeDate.Date;
        var ordered = bars
            .Where(b => !string.IsNullOrWhiteSpace(b.Time))
            .OrderBy(b => TryParseBarDateTime(b.Time, day, out var t) ? t.Ticks : (long)b.MinutesFromStart)
            .ToList();

        if (ordered.Count < 30)
            return (58m, $"有效分时仅{ordered.Count}根，质量分按中性处理");

        if (dailyOpen <= 0 || dailyHigh <= 0)
            return (60m, "日线 OHLC 不完整，分时质量分参考性弱");

        // 1) 封在涨停价附近的「段」数
        var sealEpisodes = CountSealEpisodes(ordered, dailyHigh);
        var sealScore = sealEpisodes switch
        {
            1 => 100m,
            2 => 82m,
            3 => 65m,
            _ => 48m
        };

        // 2) 收盘价在分时均价（累计 VWAP）上方的比例
        var (vwapRatio, vwapScore) = ScoreVwapAboveRatio(ordered);

        // 3) 分时 MA5（收盘）上方占比
        var (maRatio, maScore) = ScoreMa5AboveRatio(ordered);

        // 4) 量价 + 相对量能（有量上涨占比、尾盘/早盘量比）
        var (volSummary, volScore) = ScoreVolumeQuality(ordered);

        // 5) 日内振幅（日线）
        var amplitude = dailyHigh > dailyLow && dailyOpen > 0
            ? (dailyHigh - dailyLow) / dailyOpen * 100m
            : 0m;
        var ampScore = amplitude <= 8m ? 100m
            : amplitude <= 12m ? 85m
            : amplitude <= 18m ? 68m
            : 50m;

        const decimal wSeal = 0.26m;
        const decimal wVwap = 0.22m;
        const decimal wMa = 0.18m;
        const decimal wVol = 0.18m;
        const decimal wAmp = 0.16m;

        var score = Math.Round(
            wSeal * sealScore + wVwap * vwapScore + wMa * maScore + wVol * volScore + wAmp * ampScore,
            2,
            MidpointRounding.AwayFromZero);
        score = Math.Clamp(score, 0m, 100m);

        var summary =
            $"封板段≈{sealEpisodes}，VWAP上{(vwapRatio * 100m):0.#}%，MA5上{(maRatio * 100m):0.#}%，{volSummary}，振幅{amplitude:0.#}%";
        return (score, summary);
    }

    private static (decimal Ratio, decimal Score) ScoreVwapAboveRatio(IReadOnlyList<MinuteChartData> ordered)
    {
        var above = 0;
        var n = 0;
        foreach (var b in ordered)
        {
            if (b.AvgPrice <= 0) continue;
            n++;
            if (b.Close >= b.AvgPrice) above++;
        }

        var ratio = n > 0 ? (decimal)above / n : 0.5m;
        var s = ratio >= 0.82m ? 100m
            : ratio >= 0.70m ? 85m
            : ratio >= 0.55m ? 70m
            : 52m;
        return (ratio, s);
    }

    /// <summary>各根收盘的 5 周期简单均线（前 4 根无定义，内部用 0 占位）。</summary>
    private static decimal[] ComputeMinuteMa5(IReadOnlyList<MinuteChartData> ordered)
    {
        var n = ordered.Count;
        var ma = new decimal[n];
        for (var i = 0; i < n; i++)
        {
            if (i < 4)
            {
                ma[i] = 0m;
                continue;
            }

            var sum = 0m;
            for (var j = i - 4; j <= i; j++)
                sum += ordered[j].Close;
            ma[i] = sum / 5m;
        }

        return ma;
    }

    private static (decimal Ratio, decimal Score) ScoreMa5AboveRatio(IReadOnlyList<MinuteChartData> ordered)
    {
        if (ordered.Count < 10)
            return (0.5m, 65m);

        var ma = ComputeMinuteMa5(ordered);
        var above = 0;
        var denom = 0;
        for (var i = 4; i < ordered.Count; i++)
        {
            if (ma[i] <= 0) continue;
            denom++;
            if (ordered[i].Close >= ma[i]) above++;
        }

        if (denom == 0)
            return (0.5m, 65m);

        var ratio = (decimal)above / denom;
        var s = ratio >= 0.78m ? 100m
            : ratio >= 0.65m ? 86m
            : ratio >= 0.52m ? 72m
            : 54m;
        return (ratio, s);
    }

    /// <summary>
    /// 量价：阳线且成交量不低于均量一定比例视为「有量上涨」；尾盘与早盘（各约 20% 根数）均量比。
    /// </summary>
    private static (string Summary, decimal Score) ScoreVolumeQuality(IReadOnlyList<MinuteChartData> ordered)
    {
        var vols = ordered.Where(b => b.Volume > 0).Select(b => b.Volume).ToList();
        if (vols.Count == 0)
            return ("量能数据弱", 65m);

        var avgVol = vols.Average();
        if (avgVol <= 0)
            return ("量能数据弱", 65m);

        var sorted = vols.OrderBy(v => v).ToList();
        var medianVol = sorted[sorted.Count / 2];

        var upBars = 0;
        var healthyUp = 0;
        foreach (var b in ordered)
        {
            if (b.Close <= b.Open) continue;
            upBars++;
            var thr = Math.Max(0.35m * (decimal)avgVol, 1m);
            if (b.Volume >= thr)
                healthyUp++;
        }

        var upRatio = upBars > 0 ? (decimal)healthyUp / upBars : 1m;
        var upScore = upRatio >= 0.72m ? 100m
            : upRatio >= 0.55m ? 84m
            : upRatio >= 0.40m ? 68m
            : 52m;

        var n = ordered.Count;
        var seg = Math.Max(3, n / 5);
        var early = ordered.Take(seg).Where(b => b.Volume > 0).Select(b => b.Volume).DefaultIfEmpty(0m).Average();
        var late = ordered.Skip(n - seg).Where(b => b.Volume > 0).Select(b => b.Volume).DefaultIfEmpty(0m).Average();
        decimal lateEarlyRatio;
        if (early <= 0m)
            lateEarlyRatio = 1m;
        else
            lateEarlyRatio = late / early;

        // 温和放量尾盘参与较好；极端放量或极度缩量扣分
        var ratioScore = lateEarlyRatio switch
        {
            >= 0.85m and <= 2.0m => 95m,
            >= 0.55m and < 0.85m => 72m,
            > 2.0m and <= 3.2m => 68m,
            > 3.2m => 52m,
            < 0.55m and >= 0.35m => 62m,
            _ => 50m
        };

        var combined = Math.Round(0.58m * upScore + 0.42m * ratioScore, 1, MidpointRounding.AwayFromZero);
        combined = Math.Clamp(combined, 0m, 100m);

        var leTxt = early > 0 ? $"尾/首量比{lateEarlyRatio:0.##}" : "量比—";
        var summary = $"有量阳{upRatio * 100m:0.#}%，{leTxt}";
        return (summary, combined);
    }

    /// <summary>
    /// 优先保留复盘日 K 线；根数不足时放宽为复盘日前后各 1 个日历日（接口偶发日期标注偏差）；仍不足则使用全部排序结果。
    /// </summary>
    public static IReadOnlyList<MinuteChartData> FilterBarsForTradeDate(
        IReadOnlyList<MinuteChartData> bars,
        DateTime tradeDate)
    {
        var target = tradeDate.Date;
        var d1 = target.AddDays(-1);
        var d2 = target.AddDays(1);

        IReadOnlyList<MinuteChartData> OrderSeq(IEnumerable<MinuteChartData> seq) =>
            seq.OrderBy(b => TryParseBarDateTime(b.Time, target, out var t) ? t.Ticks : (long)b.MinutesFromStart).ToList();

        var strict = OrderSeq(bars.Where(b =>
            TryParseBarDateTime(b.Time, target, out var dt) && dt.Date == target));
        if (strict.Count >= 30) return strict;

        var relaxed = OrderSeq(bars.Where(b =>
            TryParseBarDateTime(b.Time, target, out var dt) && dt.Date >= d1 && dt.Date <= d2));
        if (relaxed.Count >= 30) return relaxed;

        return OrderSeq(bars);
    }

    private static int CountSealEpisodes(IReadOnlyList<MinuteChartData> bars, decimal dailyHigh)
    {
        if (dailyHigh <= 0) return 1;
        var near = dailyHigh * 0.992m;
        var far = dailyHigh * 0.965m;

        var episodes = 0;
        var inSeal = false;
        foreach (var b in bars)
        {
            var atSeal = b.Close >= near;
            if (atSeal && !inSeal)
            {
                episodes++;
                inSeal = true;
            }
            else if (inSeal && b.Close < far)
            {
                inSeal = false;
            }
        }

        return Math.Max(1, episodes);
    }

    private static bool TryParseBarDateTime(string? timeStr, DateTime fallbackCalendarDate, out DateTime fullDt)
    {
        fullDt = default;
        if (string.IsNullOrWhiteSpace(timeStr)) return false;
        if (TryParseBarCalendarDate(timeStr, out fullDt)) return true;

        var tail = timeStr.Trim();
        var prefix = fallbackCalendarDate.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return DateTime.TryParse(prefix + " " + tail, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out fullDt);
    }

    private static bool TryParseBarCalendarDate(string? timeStr, out DateTime calendarDate)
    {
        calendarDate = default;
        if (string.IsNullOrWhiteSpace(timeStr)) return false;
        var t = timeStr.Trim();
        if (t.Length >= 10 && char.IsDigit(t[0]))
        {
            var head = t.Length >= 19 ? t[..19] : t[..10];
            if (DateTime.TryParse(head, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out calendarDate))
                return true;
        }

        return false;
    }
}
