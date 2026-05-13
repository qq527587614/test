using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.Core.RealtimeData;

/// <summary>
/// 新浪分时数据服务
/// </summary>
public class SinaMinuteChartService
{
    private readonly HttpClient _httpClient;

    public SinaMinuteChartService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://finance.sina.com.cn/");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
    }

    /// <summary>从任意写法提取 6 位数字代码（与涨停表、自选股等来源对齐）。</summary>
    public static string NormalizeToCode6(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var digits = new string(raw.Trim().Where(char.IsDigit).ToArray());
        if (digits.Length >= 6) return digits[^6..];
        if (digits.Length > 0) return digits.PadLeft(6, '0');
        return "";
    }

    /// <summary>沪/深/北交所 → 新浪 K 线接口使用的市场段（北交所用小写 bj，其余为 SH/SZ）。</summary>
    public static string ResolveExchangeMarket(string code6)
    {
        if (string.IsNullOrEmpty(code6) || code6.Length < 2) return "SZ";
        if (code6.StartsWith("60", StringComparison.Ordinal) || code6.StartsWith("68", StringComparison.Ordinal))
            return "SH";
        if (code6.StartsWith("00", StringComparison.Ordinal) || code6.StartsWith("30", StringComparison.Ordinal))
            return "SZ";
        if (code6.StartsWith("43", StringComparison.Ordinal) || code6.StartsWith("83", StringComparison.Ordinal) ||
            code6.StartsWith("87", StringComparison.Ordinal) || code6.StartsWith("92", StringComparison.Ordinal))
            return "BJ";
        return "SZ";
    }

    /// <summary>拼接 <c>symbol</c> 查询参数（新浪接口常用小写 <c>sh</c>/<c>sz</c>/<c>bj</c> 前缀）。</summary>
    public static string BuildSinaKLineSymbol(string code6, string market)
    {
        if (string.Equals(market, "BJ", StringComparison.OrdinalIgnoreCase))
            return "bj" + code6;
        if (string.Equals(market, "SH", StringComparison.OrdinalIgnoreCase))
            return "sh" + code6;
        return "sz" + code6;
    }

    /// <summary>
    /// 获取分时数据（自动识别沪/深/北交所，勿再传错 market 导致 <c>SZ920xxx</c> 这类无效请求）。
    /// </summary>
    public async Task<(List<MinuteChartData> Data, string Error)> GetMinuteChartDataAsync(
        string stockCodeInput,
        int minuteScale = 1,
        int? dataLen = null)
    {
        var code6 = NormalizeToCode6(stockCodeInput);
        if (code6.Length != 6 || !code6.All(char.IsDigit))
            return (new List<MinuteChartData>(), "股票代码无效，无法请求分时");

        var market = ResolveExchangeMarket(code6);

        // 如果没有指定数据条数，根据当前时间计算（非连续竞价时段则按「整交易日」条数请求，便于复盘看最近交易日分时）
        if (dataLen == null)
        {
            dataLen = CalculateTodayDataCount(minuteScale);
        }

        if (dataLen <= 0)
            dataLen = FullSessionBarCount(minuteScale);

        var symbol = BuildSinaKLineSymbol(code6, market);
        var url = $"https://quotes.sina.cn/cn/api/json_v2.php/CN_MarketDataService.getKLineData?symbol={symbol}&scale={minuteScale}&datalen={dataLen}";

        ErrorLogger.Log(null, "SinaMinuteChartService", $"请求URL: {url}, 计算的dataLen: {dataLen}");

        try
        {
            using var resp = await _httpClient.GetAsync(url).ConfigureAwait(false);
            var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var response = DecodeResponseBody(resp, bytes);
            var preview = PreviewForLog(response, 800);

            if (!resp.IsSuccessStatusCode)
            {
                var msg = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                ErrorLogger.LogDiagnostics("minute_chart", "HTTP 非成功", $"url={url}\n{msg}\nBodyHead:\n{preview}");
                ErrorLogger.Log(null, "SinaMinuteChartService.Http", $"url={url}\n{msg}\n{preview}");
                return (new List<MinuteChartData>(), $"网络异常: {msg}（详见 logs/minute_chart_*.log）");
            }

            if (string.IsNullOrEmpty(response))
            {
                ErrorLogger.LogDiagnostics("minute_chart", "响应体为空", $"url={url}");
                return (new List<MinuteChartData>(), "API返回空（详见 logs/minute_chart_*.log）");
            }

            if (response.TrimStart().StartsWith("null", StringComparison.Ordinal))
            {
                ErrorLogger.LogDiagnostics("minute_chart", "API 返回 null", $"url={url}\nBodyHead:\n{preview}");
                return (new List<MinuteChartData>(), "API返回null（详见 logs/minute_chart_*.log）");
            }

            var data = ParseMinuteData(response);

            if (data.Count == 0)
            {
                ErrorLogger.LogDiagnostics("minute_chart", "JSON 已收到但未解析到 K 线", $"url={url}\nBodyHead:\n{preview}");
                return (new List<MinuteChartData>(), "未解析到数据（详见 logs/minute_chart_*.log）");
            }

            return (data, "");
        }
        catch (HttpRequestException ex)
        {
            var errorMsg = $"网络请求失败: {ex.Message}";
            ErrorLogger.LogDiagnostics("minute_chart", "HttpRequestException", $"url={url}\n{errorMsg}\n{ex}");
            ErrorLogger.Log(ex, "SinaMinuteChartService", url);
            return (new List<MinuteChartData>(), errorMsg);
        }
        catch (OperationCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
        {
            const string msg = "请求超时（已延长到 30 秒），请检查网络或代理；详情见 logs/minute_chart_*.log";
            ErrorLogger.LogDiagnostics("minute_chart", "请求超时", $"url={url}\n{ex}");
            ErrorLogger.Log(ex, "SinaMinuteChartService.Timeout", url);
            return (new List<MinuteChartData>(), msg);
        }
        catch (Exception ex)
        {
            var errorMsg = $"获取数据失败: {ex.Message}";
            ErrorLogger.LogDiagnostics("minute_chart", "GetMinuteChartData 异常", $"url={url}\n{errorMsg}\n{ex}");
            ErrorLogger.Log(ex, "SinaMinuteChartService.GetMinuteChartData", new { url });
            return (new List<MinuteChartData>(), errorMsg);
        }
    }

    private static string PreviewForLog(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "(empty)";
        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length <= maxLen ? text : text[..maxLen] + "…";
    }

    /// <summary>
    /// 新浪常返回带 GBK/非法 charset 的 Content-Type，<see cref="HttpContent.ReadAsStringAsync"/> 会抛错；改为字节解码。
    /// </summary>
    private static string DecodeResponseBody(HttpResponseMessage resp, byte[] bytes)
    {
        if (bytes.Length == 0) return "";

        var charset = resp.Content.Headers.ContentType?.CharSet?.Trim().Trim('"', '\'');

        if (!string.IsNullOrEmpty(charset))
        {
            try
            {
                var name = charset;
                if (name.Equals("utf8", StringComparison.OrdinalIgnoreCase))
                    name = "utf-8";
                if (name.Equals("gb2312", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("gbk", StringComparison.OrdinalIgnoreCase))
                    name = "GB18030";

                return Encoding.GetEncoding(name).GetString(bytes);
            }
            catch (ArgumentException)
            {
                // charset 名称非法，回退 UTF-8
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// 获取当日分时数据
    /// </summary>
    public async Task<(List<MinuteChartData> Data, string Error)> GetTodayMinuteDataAsync(string stockCode)
    {
        return await GetMinuteChartDataAsync(stockCode, 1, null);
    }

    /// <summary>A 股连续竞价约 240 分钟；按周期换算为 K 线条数上限（多取 1 条避免边界截断）。</summary>
    private static int FullSessionBarCount(int minuteScale)
    {
        minuteScale = Math.Clamp(minuteScale, 1, 240);
        return (int)Math.Ceiling(241.0 / minuteScale);
    }

    private static int BarsFromOneMinuteCount(int oneMinuteBars, int minuteScale)
    {
        if (oneMinuteBars <= 0) return 0;
        minuteScale = Math.Clamp(minuteScale, 1, 240);
        return Math.Max(1, (int)Math.Ceiling(oneMinuteBars / (double)minuteScale));
    }

    /// <summary>
    /// 计算应请求的 K 线条数：盘中按已交易分钟数；盘前/午休/盘后/周末按整交易日条数（接口通常返回最近已收盘交易日数据）。
    /// </summary>
    private int CalculateTodayDataCount(int minuteScale)
    {
        var now = DateTime.Now;
        var full = FullSessionBarCount(minuteScale);

        if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
            return full;

        var morningStart = new TimeSpan(9, 30, 0);
        var morningEnd = new TimeSpan(11, 30, 0);
        var afternoonStart = new TimeSpan(13, 0, 0);
        var afternoonEnd = new TimeSpan(15, 0, 0);
        var currentTime = now.TimeOfDay;

        if (currentTime < morningStart)
            return full;
        if (currentTime >= morningEnd && currentTime < afternoonStart)
            return full;
        if (currentTime > afternoonEnd)
            return full;

        if (currentTime >= morningStart && currentTime < morningEnd)
            return BarsFromOneMinuteCount((int)(currentTime - morningStart).TotalMinutes, minuteScale);

        // 下午盘中
        var oneMin = 120 + (int)(currentTime - afternoonStart).TotalMinutes;
        return BarsFromOneMinuteCount(oneMin, minuteScale);
    }

    /// <summary>
    /// 解析分时数据
    /// </summary>
    private List<MinuteChartData> ParseMinuteData(string json)
    {
        var result = new List<MinuteChartData>();

        try
        {
            if (string.IsNullOrEmpty(json) || json == "null")
            {
                ErrorLogger.Log(null, "SinaMinuteChartService.ParseMinuteData", "JSON为空或null");
                return result;
            }

            // 解析JSON数组
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                decimal cumAmount = 0;   // 累计成交额（元），用于 VWAP
                decimal cumVolume = 0;   // 累计成交量（与接口 volume 单位一致）
                decimal sumCloseVol = 0; // Σ(收盘×量)，无额字段时的量加权回退

                foreach (var item in root.EnumerateArray())
                {
                    try
                    {
                        var data = new MinuteChartData
                        {
                            Time = JsonScalarAsString(item, "day") ?? "",
                            Open = ParseDecimal(JsonScalarAsString(item, "open")),
                            High = ParseDecimal(JsonScalarAsString(item, "high")),
                            Low = ParseDecimal(JsonScalarAsString(item, "low")),
                            Close = ParseDecimal(JsonScalarAsString(item, "close")),
                            Volume = ParseDecimal(JsonScalarAsString(item, "volume")),
                            Amount = ParseDecimal(JsonScalarAsString(item, "amount") ?? JsonScalarAsString(item, "money"))
                        };

                        // 分时「均价线」：累计成交额/累计成交量（VWAP）。新浪 JSON 含 amount；
                        // 若某根缺额则用 收盘×本根量 近似该根成交额再累计。
                        cumVolume += data.Volume;
                        sumCloseVol += data.Close * data.Volume;
                        if (data.Amount > 0)
                            cumAmount += data.Amount;
                        else if (data.Volume > 0 && data.Close > 0)
                            cumAmount += data.Close * data.Volume;

                        if (cumVolume > 0 && cumAmount > 0)
                            data.AvgPrice = cumAmount / cumVolume;
                        else if (cumVolume > 0)
                            data.AvgPrice = sumCloseVol / cumVolume;
                        else
                            data.AvgPrice = data.Close;

                        // 解析时间 - 支持多种格式: "09:30:00", "09:30", "2024-01-15 09:30:00"
                        if (!string.IsNullOrEmpty(data.Time))
                        {
                            // 尝试提取时间部分
                            var timeStr = data.Time;
                            if (data.Time.Contains(" "))
                            {
                                // 格式: "2024-01-15 09:30:00"
                                timeStr = data.Time.Split(' ')[1];
                            }

                            var timeParts = timeStr.Split(':');
                            if (timeParts.Length >= 2)
                            {
                                if (int.TryParse(timeParts[0], out var hour))
                                    data.Hour = hour;
                                if (int.TryParse(timeParts[1], out var minute))
                                    data.Minute = minute;

                                // 计算从9:30开始的分钟数（11:30和13:00合并）
                                // 9:30 = 570分钟, 11:30 = 690分钟, 13:00 = 780分钟
                                int timeInMinutes = hour * 60 + minute;
                                if (timeInMinutes < 780) // 上午 (9:30-11:30)
                                {
                                    data.MinutesFromStart = timeInMinutes - 570;
                                }
                                else // 下午 (13:00-15:00)
                                {
                                    // 下午时间 = 120(上午) + (当前分钟 - 780)，合并午休
                                    data.MinutesFromStart = 120 + (timeInMinutes - 780);
                                }
                            }
                        }

                        result.Add(data);
                        index++;
                    }
                    catch (Exception ex)
                    {
                        // 记录解析单条数据的错误
                        ErrorLogger.Log(ex, "SinaMinuteChartService.ParseMinuteData", $"解析第{index}条数据失败, item: {item}");
                    }
                }

                ErrorLogger.Log(null, "SinaMinuteChartService.ParseMinuteData", $"成功解析{result.Count}条数据");
            }
        }
        catch (Exception ex)
        {
            var head = json == null ? "" : PreviewForLog(json, 800);
            ErrorLogger.LogDiagnostics("minute_chart", "ParseMinuteData 外层异常（整包无法解析）", $"{ex}\nBodyHead:\n{head}");
            ErrorLogger.Log(ex, "SinaMinuteChartService.ParseMinuteData", new { jsonLength = json?.Length, jsonPreview = json?.Substring(0, Math.Min(200, json?.Length ?? 0)) });
        }

        return result;
    }

    /// <summary>新浪部分字段为字符串或数字，统一成可解析的文本。</summary>
    private static string? JsonScalarAsString(JsonElement parent, string propName)
    {
        if (!parent.TryGetProperty(propName, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Number => p.GetRawText(),
            JsonValueKind.Null => null,
            _ => p.ToString()
        };
    }

    private decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
        {
            ErrorLogger.Log(null, "SinaMinuteChartService.ParseDecimal", $"解析失败: {value}");
            return 0;
        }
        return result;
    }
}

/// <summary>
/// 分时数据
/// </summary>
public class MinuteChartData
{
    public string Time { get; set; } = "";      // 时间字符串
    public int Hour { get; set; }                // 小时
    public int Minute { get; set; }              // 分钟
    public decimal Open { get; set; }            // 开盘价
    public decimal High { get; set; }            // 最高价
    public decimal Low { get; set; }             // 最低价
    public decimal Close { get; set; }          // 收盘价（当前价）
    public decimal Volume { get; set; }          // 成交量（与新浪接口一致，多为股）
    public decimal Amount { get; set; }          // 本根成交额（元），来自 JSON amount/money
    /// <summary>分时均价（至当前根的累计 VWAP：Σ成交额/Σ成交量；无额量时回退为收盘价算术均）。</summary>
    public decimal AvgPrice { get; set; }

    // 从9:30开始的分钟数（11:30和13:00合并到同一位置）
    public int MinutesFromStart { get; set; }

    // 计算属性
    public decimal Change => Open > 0 ? Close - Open : 0;
    public decimal ChangePercent => Open > 0 ? (Change / Open) * 100 : 0;
}
