using System.Text.Json;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.Core.Services;

/// <summary>东方财富「热度排名」拉取（与热点选股同源接口）。</summary>
public sealed class EastMoneyHotRankService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>返回 6 位股票代码 → 热度排名（数值越小越热）。</summary>
    public async Task<Dictionary<string, int>> GetPopularityRankByCode6Async(int topN = 1000, CancellationToken ct = default)
    {
        if (topN < 1) topN = 1000;
        topN = Math.Clamp(topN, 1, 5000);

        var url =
            "https://data.eastmoney.com/dataapi/xuangu/list" +
            "?st=CHANGE_RATE&sr=-1&ps=" + topN +
            "&p=1&sty=SECUCODE%2CSECURITY_CODE%2CSECURITY_NAME_ABBR%2CNEW_PRICE%2CCHANGE_RATE%2CVOLUME_RATIO%2CHIGH_PRICE%2CLOW_PRICE%2CPRE_CLOSE_PRICE%2CVOLUME%2CDEAL_AMOUNT%2CTURNOVERRATE%2CPOPULARITY_RANK" +
            "&filter=(POPULARITY_RANK%3E0)(POPULARITY_RANK%3C%3D" + topN + ")&source=SELECT_SECURITIES&client=WEB";

        try
        {
            var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, int>(StringComparer.Ordinal);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
                return new Dictionary<string, int>(StringComparer.Ordinal);

            var dict = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("SECURITY_CODE", out var codeEl)) continue;
                if (!item.TryGetProperty("POPULARITY_RANK", out var rankEl)) continue;
                var code = codeEl.GetString();
                if (string.IsNullOrWhiteSpace(code)) continue;
                if (rankEl.ValueKind != JsonValueKind.Number) continue;
                if (!rankEl.TryGetInt32(out var rank)) continue;
                dict[code] = rank;
            }

            return dict;
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, nameof(EastMoneyHotRankService), url);
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }
}
