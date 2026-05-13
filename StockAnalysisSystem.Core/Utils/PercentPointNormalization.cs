namespace StockAnalysisSystem.Core.Utils;

/// <summary>
/// 将不同数据源传入的「涨跌幅」统一为百分点（如 9.98 表示 9.98%）。
/// 财联社涨停接口常为「小数比例」如 0.0998；日线/腾讯等常为已是百分点。
/// </summary>
public static class PercentPointNormalization
{
    /// <param name="raw">原始涨跌幅（比例或百分点之一）。</param>
    /// <param name="referencePrice">用于消歧的参考价（如当日收盘、现价），无则传 0。</param>
    public static decimal ToChangePercentPoints(decimal raw, decimal referencePrice)
    {
        if (raw == 0) return 0;
        if (Math.Abs(raw) >= 1m) return raw;

        if (referencePrice <= 0)
            return raw * 100m;

        var ycAsRatio = referencePrice / (1 + raw);
        var ycAsPct = referencePrice / (1 + raw / 100m);

        static bool YesterdayLooksSane(decimal yc, decimal price)
            => yc > 0 && yc >= price * 0.5m && yc <= price * 1.5m;

        var ratioSane = YesterdayLooksSane(ycAsRatio, referencePrice);
        var pctSane = YesterdayLooksSane(ycAsPct, referencePrice);

        if (ratioSane && !pctSane)
            return raw * 100m;
        if (pctSane && !ratioSane)
            return raw;

        if (ratioSane && pctSane)
        {
            var moveIfPct = Math.Abs(raw);
            var moveIfRatio = Math.Abs(raw * 100m);
            if (moveIfPct < 2m && moveIfRatio is >= 4m and <= 25m)
                return raw * 100m;
            return raw;
        }

        return raw * 100m;
    }
}
