using System.Net.Http;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.Core.RealtimeData;

/// <summary>
/// 腾讯财经实时行情服务
/// </summary>
public class TencentRealtimeService
{
    private readonly HttpClient _httpClient;
    private readonly IStockRepository _stockRepo;
    private readonly IStockDailyDataRepository _dailyDataRepo;

    public TencentRealtimeService(
        IStockRepository stockRepo,
        IStockDailyDataRepository dailyDataRepo)
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _stockRepo = stockRepo;
        _dailyDataRepo = dailyDataRepo;
    }

    /// <summary>
    /// 从腾讯API获取实时行情数据
    /// </summary>
    /// <param name="stockCodes">股票代码列表（如：sz002131, sh600722）</param>
    /// <returns>实时行情数据列表</returns>
    public async Task<List<RealtimeStockData>> GetRealtimeDataAsync(List<string> stockCodes)
    {
        if (stockCodes.Count == 0)
            return new List<RealtimeStockData>();

        // 腾讯API格式：s_sz002131,s_sz300310,...
        var codesParam = string.Join(",", stockCodes.Select(c => $"s_{c}"));
        var url = $"https://qt.gtimg.cn/?q={codesParam}";

        try
        {
            var response = await _httpClient.GetStringAsync(url);

            // 解析返回数据
            // 格式: "sz002131"="股票名称~当前价格~昨收~今开..."
            var result = new List<RealtimeStockData>();

            // 使用正则解析
            var matches = System.Text.RegularExpressions.Regex.Matches(response, "\"([^\"]+)\"=\"([^\"]+)\"");

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var stockCode = match.Groups[1].Value;
                var dataStr = match.Groups[2].Value;

                var fields = dataStr.Split('~');
                if (fields.Length >= 10)
                {
                    try
                    {
                        var data = new RealtimeStockData
                        {
                            StockCode = stockCode,
                            StockName = fields[0],
                            CurrentPrice = decimal.TryParse(fields[1], out var cp) ? cp : 0,
                            YesterdayClose = decimal.TryParse(fields[2], out var yc) ? yc : 0,
                            OpenPrice = decimal.TryParse(fields[3], out var op) ? op : 0,
                            Volume = decimal.TryParse(fields[4], out var vol) ? vol : 0,
                            Amount = decimal.TryParse(fields[5], out var amt) ? amt : 0,
                            Amplitude = decimal.TryParse(fields[6], out var amp) ? amp : 0,
                            ChangePercent = decimal.TryParse(fields[7], out var cpct) ? cpct : 0,
                            ChangeAmount = decimal.TryParse(fields[8], out var ca) ? ca : 0,
                            TurnoverRate = decimal.TryParse(fields[9], out var tr) ? tr : 0,
                            HighPrice = decimal.TryParse(fields[10], out var hp) ? hp : 0,
                            LowPrice = decimal.TryParse(fields[11], out var lp) ? lp : 0,
                        };
                        result.Add(data);
                    }
                    catch
                    {
                        // 跳过解析失败的记录
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "TencentRealtimeService.GetRealtimeData", new { StockCodes = stockCodes.Count });
            return new List<RealtimeStockData>();
        }
    }

    /// <summary>
    /// 同步所有股票的实时数据到数据库
    /// </summary>
    /// <param name="batchSize">每批数量，默认100</param>
    /// <param name="progress">进度报告</param>
    public async Task<RealtimeSyncResult> SyncAllStocksAsync(
        int batchSize = 100,
        IProgress<string>? progress = null)
    {
        var result = new RealtimeSyncResult();

        // 获取所有股票
        progress?.Report("获取股票列表...");
        var stocks = await _stockRepo.GetAllAsync();

        if (stocks.Count == 0)
        {
            result.ErrorMessage = "没有找到股票数据";
            return result;
        }

        // 转换为腾讯API格式
        var stockCodes = stocks.Select(s =>
        {
            // 上海: sh + 6位代码, 深圳: sz + 6位代码
            var code = s.StockCode.PadLeft(6, '0');
            return s.Market?.ToLower() == "sh" ? $"sh{code}" : $"sz{code}";
        }).ToList();

        var totalBatches = (int)Math.Ceiling((double)stockCodes.Count / batchSize);
        var allData = new List<StockDailyData>();
        var today = DateTime.Today;

        progress?.Report($"开始同步，共 {stockCodes.Count} 只股票，分 {totalBatches} 批...");

        for (int i = 0; i < totalBatches; i++)
        {
            var batchCodes = stockCodes.Skip(i * batchSize).Take(batchSize).ToList();
            progress?.Report($"正在同步第 {i + 1}/{totalBatches} 批 ({batchCodes.Count} 只股票)...");

            var realtimeData = await GetRealtimeDataAsync(batchCodes);

            // 转换为日线数据
            foreach (var rd in realtimeData)
            {
                // 查找对应的股票ID
                var stockCode6 = rd.StockCode.Substring(2); // 去掉sh/sz前缀
                var stock = stocks.FirstOrDefault(s => s.StockCode == stockCode6);

                if (stock != null)
                {
                    var dailyData = new StockDailyData
                    {
                        StockID = stock.StockID,
                        StockCode = stock.StockCode,
                        TradeDate = today,
                        OpenPrice = rd.OpenPrice,
                        ClosePrice = rd.CurrentPrice,
                        HighPrice = rd.HighPrice,
                        LowPrice = rd.LowPrice,
                        Volume = rd.Volume,
                        Amount = rd.Amount,
                        ChangePercent = rd.ChangePercent,
                        TurnoverRate = rd.TurnoverRate,
                        CurrentPrice = rd.CurrentPrice,
                        BeforDate = today.AddDays(-1),
                        CreatedTime = DateTime.Now
                    };
                    allData.Add(dailyData);
                }
            }

            // 每批之间稍微休息一下，避免请求过快
            if (i < totalBatches - 1)
            {
                await Task.Delay(500);
            }
        }

        // 批量保存到数据库
        if (allData.Count > 0)
        {
            progress?.Report($"保存 {allData.Count} 条数据到数据库...");

            // 先删除今日已有数据
            try
            {
                var context = GetDailyDataContext();
                var existingData = context.StockDailyData.Where(d => d.TradeDate == today).ToList();
                if (existingData.Count > 0)
                {
                    context.StockDailyData.RemoveRange(existingData);
                    await context.SaveChangesAsync();
                }
            }
            catch { }

            // 批量插入
            foreach (var batch in allData.Chunk(100))
            {
                try
                {
                    var context = GetDailyDataContext();
                    context.StockDailyData.AddRange(batch);
                    await context.SaveChangesAsync();
                    result.SuccessCount += batch.Length;
                }
                catch (Exception ex)
                {
                    result.ErrorCount += batch.Length;
                    ErrorLogger.Log(ex, "TencentRealtimeService.SyncAllStocksAsync - SaveBatch");
                }
            }
        }

        result.TotalProcessed = allData.Count;
        progress?.Report($"同步完成！成功 {result.SuccessCount} 条，失败 {result.ErrorCount} 条");

        return result;
    }

    private AppDbContext GetDailyDataContext()
    {
        // 使用IDbContextFactory来创建新的DbContext
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .Build();

        var connectionString = configuration.GetConnectionString("MySql");
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));

        return new AppDbContext(optionsBuilder.Options);
    }
}

/// <summary>
/// 实时股票数据
/// </summary>
public class RealtimeStockData
{
    public string StockCode { get; set; } = "";
    public string StockName { get; set; } = "";
    public decimal CurrentPrice { get; set; }       // 当前价格
    public decimal YesterdayClose { get; set; }    // 昨收
    public decimal OpenPrice { get; set; }          // 今开
    public decimal Volume { get; set; }             // 成交量(手)
    public decimal Amount { get; set; }            // 成交额(元)
    public decimal Amplitude { get; set; }          // 振幅(%)
    public decimal ChangePercent { get; set; }      // 涨跌幅(%)
    public decimal ChangeAmount { get; set; }       // 涨跌额(元)
    public decimal TurnoverRate { get; set; }      // 换手率(%)
    public decimal HighPrice { get; set; }          // 最高
    public decimal LowPrice { get; set; }          // 最低
}

/// <summary>
/// 同步结果
/// </summary>
public class RealtimeSyncResult
{
    public int TotalProcessed { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public string? ErrorMessage { get; set; }
}
