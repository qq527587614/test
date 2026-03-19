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
                var responseJson = JsonDocument.Parse(responseString);
                if (responseJson.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    return choices[0].GetProperty("message").GetProperty("content").GetString();
                }
            }
            else
            {
                _logger?.LogError("DeepSeek API错误: {StatusCode} - {Response}", 
                    response.StatusCode, responseString);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "DeepSeek API请求失败");
        }

        return null;
    }

    /// <summary>
    /// 构建评分提示词
    /// </summary>
    private string BuildPrompt(List<DailyPick.StockPickInfo> stocks, string? customPrompt)
    {
        var sb = new StringBuilder();
        sb.AppendLine(customPrompt ?? @"你是一位专业的股票分析师。请根据以下股票信息，对每只股票给出买入评分（0-100分）。
评分标准：
1. 技术面：价格走势、成交量变化（30%）
2. 基本面：市值规模、行业前景（30%）
3. 市场情绪：换手率、板块热度（20%）
4. 风险评估：流动性、波动性（20%）

请以JSON格式返回评分结果，格式如下：
{""scores"":[{""code"":""股票代码"",""score"":85,""reason"":""简要理由""}]}

股票列表：");

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
}
