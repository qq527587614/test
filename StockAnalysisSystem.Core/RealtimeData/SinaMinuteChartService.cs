using System.Net.Http;
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
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    /// <summary>
    /// 获取分时数据
    /// </summary>
    public async Task<(List<MinuteChartData> Data, string Error)> GetMinuteChartDataAsync(
        string stockCode,
        string market = "SH",
        int minuteScale = 1,
        int? dataLen = null)
    {
        // 如果没有指定数据条数，根据当前时间计算
        if (dataLen == null)
        {
            dataLen = CalculateTodayDataCount();
        }

        // 如果计算出的dataLen <= 0，说明当前不是交易时段，没有当日数据可取
        if (dataLen <= 0)
        {
            ErrorLogger.Log(null, "SinaMinuteChartService", $"非交易时段，dataLen={dataLen}，返回空数据");
            return (new List<MinuteChartData>(), "非交易时段，无当日分时数据");
        }

        var symbol = $"{market}{stockCode}";
        var url = $"https://quotes.sina.cn/cn/api/json_v2.php/CN_MarketDataService.getKLineData?symbol={symbol}&scale={minuteScale}&datalen={dataLen}";

        ErrorLogger.Log(null, "SinaMinuteChartService", $"请求URL: {url}, 计算的dataLen: {dataLen}");

        try
        {
            var response = await _httpClient.GetStringAsync(url);

            // 调试：记录API响应
            if (string.IsNullOrEmpty(response))
            {
                return (new List<MinuteChartData>(), "API返回空");
            }

            if (response == "null" || response.StartsWith("null"))
            {
                return (new List<MinuteChartData>(), "API返回null");
            }

            var data = ParseMinuteData(response);

            if (data.Count == 0)
            {
                return (new List<MinuteChartData>(), "未解析到数据");
            }

            return (data, "");
        }
        catch (HttpRequestException ex)
        {
            var errorMsg = $"网络请求失败: {ex.Message}";
            ErrorLogger.Log(ex, "SinaMinuteChartService", url);
            return (new List<MinuteChartData>(), errorMsg);
        }
        catch (Exception ex)
        {
            var errorMsg = $"获取数据失败: {ex.Message}";
            ErrorLogger.Log(ex, "SinaMinuteChartService.GetMinuteChartData", new { url });
            return (new List<MinuteChartData>(), errorMsg);
        }
    }

    /// <summary>
    /// 获取当日分时数据
    /// </summary>
    public async Task<(List<MinuteChartData> Data, string Error)> GetTodayMinuteDataAsync(string stockCode, string market = "SH")
    {
        return await GetMinuteChartDataAsync(stockCode, market, 1, null);
    }

    /// <summary>
    /// 计算今日应该获取的数据条数
    /// </summary>
    private int CalculateTodayDataCount()
    {
        var now = DateTime.Now;

        // 如果是周末，返回0（不获取历史数据）
        if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
        {
            return 0;
        }

        // 上午时段：9:30 - 11:30
        var morningStart = new TimeSpan(9, 30, 0);
        var morningEnd = new TimeSpan(11, 30, 0);

        // 下午时段：13:00 - 15:00
        var afternoonStart = new TimeSpan(13, 0, 0);
        var afternoonEnd = new TimeSpan(15, 0, 0);

        var currentTime = now.TimeOfDay;

        // 开盘前（9:30之前）- 返回0，不取历史数据
        if (currentTime < morningStart)
        {
            return 0;
        }
        // 上午交易中
        else if (currentTime >= morningStart && currentTime < morningEnd)
        {
            return (int)(currentTime - morningStart).TotalMinutes;
        }
        // 午间休市（11:30 - 13:00）
        else if (currentTime >= morningEnd && currentTime < afternoonStart)
        {
            return 0; // 午间休市不获取历史数据，返回0
        }
        // 下午交易中
        else if (currentTime >= afternoonStart && currentTime <= afternoonEnd)
        {
            return 120 + (int)(currentTime - afternoonStart).TotalMinutes;
        }
        // 收盘后（15:00 - 24:00）
        else
        {
            return 0; // 收盘后不再获取历史分时数据，第二天开盘前没有当日数据
        }
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
                decimal runningSum = 0;  // 累计价格用于计算均价

                foreach (var item in root.EnumerateArray())
                {
                    try
                    {
                        var data = new MinuteChartData
                        {
                            Time = item.GetProperty("day").GetString() ?? "",
                            Open = ParseDecimal(item.GetProperty("open").GetString()),
                            High = ParseDecimal(item.GetProperty("high").GetString()),
                            Low = ParseDecimal(item.GetProperty("low").GetString()),
                            Close = ParseDecimal(item.GetProperty("close").GetString()),
                            Volume = ParseDecimal(item.GetProperty("volume").GetString())
                        };

                        // 计算均价（累计平均）
                        runningSum += data.Close;
                        data.AvgPrice = runningSum / (index + 1);

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
            ErrorLogger.Log(ex, "SinaMinuteChartService.ParseMinuteData", new { jsonLength = json?.Length, jsonPreview = json?.Substring(0, Math.Min(200, json.Length)) });
        }

        return result;
    }

    private decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        if (!decimal.TryParse(value, out var result))
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
    public decimal Volume { get; set; }          // 成交量
    public decimal AvgPrice { get; set; }       // 均价（累计平均）

    // 从9:30开始的分钟数（11:30和13:00合并到同一位置）
    public int MinutesFromStart { get; set; }

    // 计算属性
    public decimal Change => Open > 0 ? Close - Open : 0;
    public decimal ChangePercent => Open > 0 ? (Change / Open) * 100 : 0;
}
