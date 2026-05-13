using System.Globalization;
using System.Linq;
using StockAnalysisSystem.Core.RealtimeData;

namespace StockAnalysisSystem.Core.Services;

/// <summary>
/// 涨停日分时质量启发式评分（1 分钟 K 线 + 当日日线 OHLC），用于复盘粗筛。
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

        // 1) 封在涨停价附近的「段」数：一段内曾贴近 High，跌离后再回封算多段（烂板）
        var sealEpisodes = CountSealEpisodes(ordered, dailyHigh);
        var sealScore = sealEpisodes switch
        {
            1 => 100m,
            2 => 82m,
            3 => 65m,
            _ => 48m
        };

        // 2) 收盘价在分时均价（累计 VWAP）上方的比例
        var aboveAvg = 0;
        var n = 0;
        foreach (var b in ordered)
        {
            if (b.AvgPrice <= 0) continue;
            n++;
            if (b.Close >= b.AvgPrice) aboveAvg++;
        }

        var ratio = n > 0 ? (decimal)aboveAvg / n : 0.5m;
        var avgScore = ratio >= 0.82m ? 100m
            : ratio >= 0.70m ? 85m
            : ratio >= 0.55m ? 70m
            : 52m;

        // 3) 日内振幅 (High-Low)/Open：涨停日适度振幅更「稳」，过大视为分歧大
        var amplitude = dailyHigh > dailyLow && dailyOpen > 0
            ? (dailyHigh - dailyLow) / dailyOpen * 100m
            : 0m;
        var ampScore = amplitude <= 8m ? 100m
            : amplitude <= 12m ? 85m
            : amplitude <= 18m ? 68m
            : 50m;

        var score = Math.Round(0.48m * sealScore + 0.35m * avgScore + 0.17m * ampScore, 2, MidpointRounding.AwayFromZero);
        score = Math.Clamp(score, 0m, 100m);

        var summary =
            $"封板段数≈{sealEpisodes}，均线上方占比{(ratio * 100m):0.#}%，振幅{amplitude:0.#}%";
        return (score, summary);
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
