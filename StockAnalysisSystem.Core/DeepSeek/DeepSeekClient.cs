using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockAnalysisSystem.Core.Common;
using StockAnalysisSystem.Core.Repositories;

namespace StockAnalysisSystem.Core.DeepSeek;

/// <summary>
/// DeepSeek API客户端
/// </summary>
public class DeepSeekClient
{
    private readonly HttpClient _httpClient;
    private readonly DeepSeekSettings _settings;
    private readonly IDeepSeekLogRepository _logRepo;
    private readonly ILogger<DeepSeekClient>? _logger;

    public DeepSeekClient(
        IOptions<DeepSeekSettings> settings,
        IDeepSeekLogRepository logRepo,
        ILogger<DeepSeekClient>? logger = null)
    {
        _settings = settings.Value;
        _logRepo = logRepo;
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds)
        };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.ApiKey}");
    }

    /// <summary>
    /// 对股票进行评分
    /// </summary>
    public async Task<Dictionary<string, decimal>> ScoreStocksAsync(
        List<DailyPick.StockPickInfo> stocks,
        string? customPrompt = null)
    {
        var result = new Dictionary<string, decimal>();

        if (string.IsNullOrEmpty(_settings.ApiKey))
        {
            _logger?.LogWarning("DeepSeek API密钥未配置");
            return result;
        }

        try
        {
            var prompt = BuildPrompt(stocks, customPrompt);
            var response = await SendRequestAsync(prompt);

            if (!string.IsNullOrEmpty(response))
            {
                result = ParseScores(response);
            }

            // 记录日志
            await _logRepo.AddAsync(new Entities.DeepSeekLog
            {
                RequestData = JsonSerializer.Serialize(new { stocks.Count, prompt }),
                ResponseData = string.IsNullOrEmpty(response) ? "null" : response,
                UsedFor = "StockScoring"
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "DeepSeek评分失败");
        }

        return result;
    }

    /// <summary>
    /// 发送请求到DeepSeek API
    /// </summary>
    private async Task<string?> SendRequestAsync(string prompt)
    {
        try
        {
            var requestBody = new
            {
                model = "deepseek-chat",
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.7,
                max_tokens = 4000
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_settings.Endpoint, content);
            var responseString = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return responseString;
            }
            else
            {
                _logger?.LogError($"DeepSeek API 请求失败: {response.StatusCode}");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "DeepSeek API 请求异常");
            return null;
        }
    }

    /// <summary>
    /// 构建评分提示词
    /// </summary>
    private string BuildPrompt(List<DailyPick.StockPickInfo> stocks, string? customPrompt)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(customPrompt))
        {
            sb.AppendLine(customPrompt);
            return sb.ToString();
        }

        sb.AppendLine("请对以下股票进行评分，标准如下：");
        sb.AppendLine();
        sb.AppendLine("评分标准：");
        sb.AppendLine("1. 技术面：价格走势、成交量变化（30%）");
        sb.AppendLine("2. 基本面：市值规模、行业前景（30%）");
        sb.AppendLine("3. 市场情绪：换手率、板块热度（20%）");
        sb.AppendLine("4. 风险评估：流动性、波动性（20%）");
        sb.AppendLine();

        sb.AppendLine("请以JSON格式返回评分结果，格式如下：");
        sb.AppendLine("{\"scores\":[{\"code\":\"股票代码\",\"score\":85,\"reason\":\"简要理由\"}]}");
        sb.AppendLine();
        sb.AppendLine("股票列表：");

        foreach (var stock in stocks)
        {
            sb.AppendLine($"- 代码:{stock.StockCode}, 名称:{stock.StockName}, " +
                         $"行业:{stock.Industry ?? "未知"}, " +
                         $"流通市值:{stock.CirculationValue?.ToString("N0") ?? "未知"}万元");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 解析评分结果
    /// </summary>
    private Dictionary<string, decimal> ParseScores(string response)
    {
        var result = new Dictionary<string, decimal>();

        try
        {
            // 尝试提取JSON部分
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var json = JsonDocument.Parse(jsonStr);

                if (json.RootElement.TryGetProperty("scores", out var scores))
                {
                    foreach (var item in scores.EnumerateArray())
                    {
                        var code = item.GetProperty("code").GetString() ?? "";
                        var score = item.GetProperty("score").GetDecimal();
                        result[code] = score;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "解析DeepSeek响应失败: {Response}", response);
        }

        return result;
    }

    /// <summary>
    /// 分析单只股票
    /// </summary>
    public async Task<string?> AnalyzeStockAsync(DailyPick.StockPickInfo stock)
    {
        var prompt = $@"请分析以下股票的投资价值，给出买入建议：

股票代码：{stock.StockCode}
股票名称：{stock.StockName}
所属行业：{stock.Industry ?? "未知"}
流通市值：{stock.CirculationValue?.ToString("N0") ?? "未知"}万元
最新价格：{stock.ClosePrice}
涨跌幅：{stock.ChangePercent}%
换手率：{stock.TurnoverRate}%

请从技术面、基本面、风险提示三个方面进行分析，并给出明确的投资建议。";

        return await SendRequestAsync(prompt);
    }

    /// <summary>
    /// 分析市场消息并推荐板块
    /// </summary>
    public async Task<MarketAnalysisResult?> AnalyzeMarketAsync(string newsContent)
    {
        if (string.IsNullOrEmpty(_settings.ApiKey))
        {
            _logger?.LogWarning("DeepSeek API密钥未配置");
            return null;
        }

        try
        {
            var prompt = $@"你是一位股票分析师。请分析当前A股市场并推荐板块。

任务：
1. 判断市场趋势（bullish/bearish/neutral）
2. 推荐适合短线操作的，短线最强的3个板块，还有就是最近1-2天有利好的板块
3. 给出每个板块的推荐理由和信心度（0.6-1.0之间）

返回JSON格式（只能返回JSON代码块，不要其他说明）：

```json
{{
    ""marketTrend"": ""市场趋势描述，20字以内"",
    ""trend"": ""bullish"",
    ""recommendedPlates"": [
        {{
            ""plateName"": ""板块名称"",
            ""reason"": ""推荐理由，15字以内"",
            ""confidence"": 0.75,
            ""stocks"": [""000001"", ""600519""]
        }}
    ],
    ""risks"": [""风险1"", ""风险2""]
}}
```

要求：
- 必须返回2-4个板块
- confidence是0.6到1.0之间的数字
- 板块名称：人工智能、新能源车、半导体、消费电子、医药生物、券商等
- stocks: 1-2只代表性股票代码（6位数字）
- 只返回```json代码块```
- 不要其他文字说明";

            var response = await SendRequestAsync(prompt);

            // 记录日志
            await _logRepo.AddAsync(new Entities.DeepSeekLog
            {
                RequestData = JsonSerializer.Serialize(new { promptLength = prompt.Length, promptPreview = prompt.Substring(0, Math.Min(200, prompt.Length)) + "..." }),
                ResponseData = string.IsNullOrEmpty(response) ? "null" : response,
                UsedFor = "MarketAnalysis"
            });

            _logger?.LogInformation($"DeepSeek API 响应长度: {response?.Length ?? 0}");

            if (string.IsNullOrEmpty(response))
            {
                _logger?.LogWarning("DeepSeek API 返回空响应");
                return null;
            }

            _logger?.LogInformation($"DeepSeek 响应前200字符: {response.Substring(0, Math.Min(200, response.Length))}");

            return ParseMarketAnalysis(response);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "DeepSeek市场分析失败");
        }

        return null;
    }

    /// <summary>
    /// 解析市场分析结果
    /// </summary>
    private MarketAnalysisResult? ParseMarketAnalysis(string response)
    {
        try
        {
            _logger?.LogInformation($"开始解析DeepSeek响应，总长度: {response.Length}");

            // 先解析外层 OpenAI 格式的响应
            var outerJson = JsonDocument.Parse(response);

            // 提取 content 字段
            if (!outerJson.RootElement.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0)
            {
                _logger?.LogWarning("响应中未找到 choices 数组");
                return null;
            }

            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var contentElement))
            {
                _logger?.LogWarning("响应中未找到 message.content 字段");
                return null;
            }

            var content = contentElement.GetString() ?? "";
            _logger?.LogInformation($"提取到的 content 长度: {content.Length}");

            // 移除 markdown 代码块标记（如果有）
            var jsonStr = content.Trim();
            if (jsonStr.StartsWith("```json"))
            {
                jsonStr = jsonStr.Substring(7);
            }
            else if (jsonStr.StartsWith("```"))
            {
                jsonStr = jsonStr.Substring(3);
            }

            if (jsonStr.EndsWith("```"))
            {
                jsonStr = jsonStr.Substring(0, jsonStr.Length - 3);
            }

            jsonStr = jsonStr.Trim();

            _logger?.LogInformation($"清理后的JSON字符串: {jsonStr}");

            // 解析内层 JSON
            var json = JsonDocument.Parse(jsonStr);

            var result = new MarketAnalysisResult
            {
                MarketTrend = json.RootElement.TryGetProperty("marketTrend", out var mt)
                    ? mt.GetString() ?? ""
                    : "",
                Trend = json.RootElement.TryGetProperty("trend", out var tr)
                    ? tr.GetString() ?? "neutral"
                    : "neutral"
            };

            _logger?.LogInformation($"市场趋势: {result.MarketTrend}, 类型: {result.Trend}");

            if (json.RootElement.TryGetProperty("recommendedPlates", out var plates))
            {
                var plateCount = 0;
                foreach (var plate in plates.EnumerateArray())
                {
                    var plateRec = new PlateRecommendation
                    {
                        PlateName = plate.TryGetProperty("plateName", out var pn)
                            ? pn.GetString() ?? ""
                            : "",
                        Reason = plate.TryGetProperty("reason", out var re)
                            ? re.GetString() ?? ""
                            : "",
                        Confidence = plate.TryGetProperty("confidence", out var cf)
                            ? cf.GetDecimal()
                            : 0m
                    };

                    _logger?.LogInformation($"解析到板块: {plateRec.PlateName}, 理由: {plateRec.Reason}, 信心度: {plateRec.Confidence}");

                    if (plate.TryGetProperty("stocks", out var stocks))
                    {
                        foreach (var stock in stocks.EnumerateArray())
                        {
                            plateRec.Stocks.Add(stock.GetString() ?? "");
                        }
                    }

                    result.RecommendedPlates.Add(plateRec);
                    plateCount++;
                }
                _logger?.LogInformation($"共解析到 {plateCount} 个推荐板块");
            }
            else
            {
                _logger?.LogWarning("JSON中未找到 recommendedPlates 字段");
            }

            if (json.RootElement.TryGetProperty("risks", out var risks))
            {
                foreach (var risk in risks.EnumerateArray())
                {
                    result.Risks.Add(risk.GetString() ?? "");
                }
            }

            _logger?.LogInformation($"解析完成，最终推荐板块数: {result.RecommendedPlates.Count}");

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "解析市场分析响应失败: {Response}", response);
        }

        return null;
    }
}

/// <summary>
/// 市场分析结果
/// </summary>
public class MarketAnalysisResult
{
    /// <summary>
    /// 市场趋势描述
    /// </summary>
    public string MarketTrend { get; set; } = "";

    /// <summary>
    /// 市场趋势类型
    /// </summary>
    public string Trend { get; set; } = "neutral";

    /// <summary>
    /// 推荐的板块
    /// </summary>
    public List<PlateRecommendation> RecommendedPlates { get; set; } = new();

    /// <summary>
    /// 风险提示
    /// </summary>
    public List<string> Risks { get; set; } = new();
}

/// <summary>
/// 板块推荐
/// </summary>
public class PlateRecommendation
{
    /// <summary>
    /// 板块名称
    /// </summary>
    public string PlateName { get; set; } = "";

    /// <summary>
    /// 推荐理由
    /// </summary>
    public string Reason { get; set; } = "";

    /// <summary>
    /// 信心度（0-1）
    /// </summary>
    public decimal Confidence { get; set; }

    /// <summary>
    /// 代表性股票代码
    /// </summary>
    public List<string> Stocks { get; set; } = new();
}
