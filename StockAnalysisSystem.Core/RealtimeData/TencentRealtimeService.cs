using System.Net.Http;
using System.Text;
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
        // 注册GBK编码支持（.NET Core需要）
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }

        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _stockRepo = stockRepo;
        _dailyDataRepo = dailyDataRepo;
    }

    /// <summary>
    /// 从腾讯API获取实时行情数据
    /// </summary>
    /// <param name="stockCodes">股票代码列表（6位数字，如：002131, 300005, 600722）</param>
    /// <returns>实时行情数据列表</returns>
    public async Task<List<RealtimeStockData>> GetRealtimeDataAsync(List<string> stockCodes)
    {
        if (stockCodes.Count == 0)
            return new List<RealtimeStockData>();

        // 腾讯接口格式：q=sz002229,sz300166,sh600722
        // 股票代码已经带前缀（sz/sh），直接使用
        var codesParam = string.Join(",", stockCodes);
        var url = $"https://qt.gtimg.cn/q={codesParam}";

        try
        {
            // 使用GetByteArrayAsync避免编码问题，腾讯API返回GBK编码
            var bytes = await _httpClient.GetByteArrayAsync(url);
            // 使用GBK编码解码
            var gbkEncoding = Encoding.GetEncoding("GBK");
            var response = gbkEncoding.GetString(bytes);

            // 解析返回数据
            // 格式: v_sz002131="51~利欧股份~002131~24.90~23.70~24.45~2985677~72895.58~1~..."
            var result = new List<RealtimeStockData>();

            // 按行分割
            var lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                try
                {
                    // 查找等号位置
                    var eqIndex = line.IndexOf('=');
                    if (eqIndex < 0) continue;

                    var stockCodePart = line.Substring(0, eqIndex).Trim();
                    var dataPart = line.Substring(eqIndex + 1).Trim();

                    // 去掉引号
                    if (dataPart.StartsWith("\"") && dataPart.EndsWith("\""))
                    {
                        dataPart = dataPart.Substring(1, dataPart.Length - 2);
                    }

                    // 提取股票代码（去掉v_前缀，如 v_sz002131 -> 002131）
                    var stockCode = stockCodePart;
                    if (stockCode.StartsWith("v_sz") || stockCode.StartsWith("v_sh"))
                    {
                        stockCode = stockCode.Substring(3);
                    }
                    else if (stockCode.StartsWith("sz") || stockCode.StartsWith("sh"))
                    {
                        stockCode = stockCode.Substring(2);
                    }

                    var fields = dataPart.Split('~');
                    if (fields.Length >= 13)
                    {
                        var data = new RealtimeStockData
                        {
                            StockCode = fields[2],      // 如 002131
                            StockName = fields[1],       // 股票名称
                            CurrentPrice = ParseDecimal(fields[3]),    // 当前价格
                            YesterdayClose = ParseDecimal(fields[4]),   // 昨收
                            OpenPrice = ParseDecimal(fields[5]),        // 今开
                            Volume = ParseDecimal(fields[6]),           // 成交量(手)
                            Amount = ParseDecimal(fields[37]),           // 成交额(万)
                            Amplitude = ParseDecimal(fields[43]),        // 振幅
                            ChangePercent = ParseDecimal(fields[32]),    // 涨跌幅
                            ChangeAmount = ParseDecimal(fields[31]),     // 涨跌额
                            TurnoverRate = ParseDecimal(fields[38]),    // 换手率
                            HighPrice = ParseDecimal(fields[33]),       // 最高
                            LowPrice = ParseDecimal(fields[34]),       // 最低
                            Buy1 = fields.Length > 13 ? ParseDecimal(fields[13]) : 0,      // 买一价
                            Buy2 = fields.Length > 14 ? ParseDecimal(fields[14]) : 0,      // 买二价
                            Buy3 = fields.Length > 15 ? ParseDecimal(fields[15]) : 0,      // 买三价
                            Buy4 = fields.Length > 16 ? ParseDecimal(fields[16]) : 0,       // 买四价
                            Buy5 = fields.Length > 17 ? ParseDecimal(fields[17]) : 0,       // 买五价
                        };
                        result.Add(data);
                    }
                }
                catch (Exception ex)
                {
                    // 跳过解析失败的记录
                    ErrorLogger.Log(ex, $"解析行失败: {line}");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "TencentRealtimeService.GetRealtimeData", new { StockCodes = stockCodes.Count, Url = url });
            return new List<RealtimeStockData>();
        }
    }

    private decimal ParseDecimal(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        return decimal.TryParse(value, out var result) ? result : 0;
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
            progress?.Report("错误: 没有找到股票数据");
            return result;
        }

        progress?.Report($"获取到 {stocks.Count} 只股票");

        // 构建股票代码映射（6位代码 -> 股票信息）
        // 支持多种格式的key：纯6位数字、带sz/sh前缀
        var stockMap = new Dictionary<string, StockInfo>();
        var stockCodes = new List<string>();

        foreach (var s in stocks)
        {
            // 提取纯6位数字代码
            var rawCode = s.StockCode ?? "";
            var code6 = rawCode.Replace("sz", "").Replace("sh", "").PadLeft(6, '0');

            // 根据前两位数字判断市场
            // 00/30开头 -> 深圳(sz)，60/68开头 -> 上海(sh)
            var prefix = "sz";
            if (code6.StartsWith("60") || code6.StartsWith("68"))
            {
                prefix = "sh";
            }

            // 存储到map，支持多种格式查找
            stockMap[code6] = s;  // 纯数字格式：002131
            stockMap[$"sz{code6}"] = s;  // 深圳格式
            stockMap[$"sh{code6}"] = s;  // 上海格式

            stockCodes.Add($"{prefix}{code6}");
        }

        var totalBatches = (int)Math.Ceiling((double)stockCodes.Count / batchSize);
        var allData = new List<StockDailyData>();
        var today = DateTime.Today;

        progress?.Report($"开始同步，共 {stockCodes.Count} 只股票，分 {totalBatches} 批...");

        for (int i = 0; i < totalBatches; i++)
        {
            var batchCodes = stockCodes.Skip(i * batchSize).Take(batchSize).ToList();
            progress?.Report($"正在同步第 {i + 1}/{totalBatches} 批 ({batchCodes.Count} 只股票)...");

            try
            {
                var realtimeData = await GetRealtimeDataAsync(batchCodes);
                progress?.Report($"第 {i + 1} 批获取到 {realtimeData.Count} 条数据");

                // 转换为日线数据
                foreach (var rd in realtimeData)
                {
                    // 尝试多种格式查找股票
                    StockInfo? stock = null;
                    var rawCode = rd.StockCode ?? "";

                    // 尝试直接查找
                    if (!stockMap.TryGetValue(rawCode, out stock))
                    {
                        // 去掉前缀后查找
                        var code6 = rawCode.Replace("sz", "").Replace("sh", "");
                        if (!stockMap.TryGetValue(code6, out stock))
                        {
                            // 尝试带sz前缀查找
                            if (!stockMap.TryGetValue($"sz{code6}", out stock))
                            {
                                // 尝试带sh前缀查找
                                stockMap.TryGetValue($"sh{code6}", out stock);
                            }
                        }
                    }

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
                            Volume = rd.Volume,  // 手转换为股
                            Amount = rd.Amount, // 万转换为元
                            ChangePercent = rd.ChangePercent,
                            TurnoverRate = rd.TurnoverRate,
                            CurrentPrice = rd.CurrentPrice,
                            BeforDate = today.AddDays(-1),
                            CreatedTime = DateTime.Now
                        };
                        allData.Add(dailyData);
                    }
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"第 {i + 1} 批同步失败: {ex.Message}");
                ErrorLogger.Log(ex, $"同步第 {i + 1} 批失败");
            }

            // 每批之间稍微休息一下，避免请求过快
            if (i < totalBatches - 1)
            {
                await Task.Delay(300);
            }
        }

        progress?.Report($"共获取到 {allData.Count} 条数据，开始保存到数据库...");

        // 批量保存到数据库
        if (allData.Count > 0)
        {
            try
            {
                using var context = GetDailyDataContext();

                // 先删除今日已有数据
                var existingData = context.StockDailyData
                    .Where(d => d.TradeDate == today)
                    .ToList();

                if (existingData.Count > 0)
                {
                    progress?.Report($"删除今日已有数据 {existingData.Count} 条...");
                    context.StockDailyData.RemoveRange(existingData);
                    await context.SaveChangesAsync();
                }

                // 批量插入
                progress?.Report($"插入 {allData.Count} 条新数据...");
                context.StockDailyData.AddRange(allData);
                await context.SaveChangesAsync();

                result.SuccessCount = allData.Count;
                result.TotalProcessed = allData.Count;
                progress?.Report($"保存成功！共 {allData.Count} 条数据");
            }
            catch (Exception ex)
            {
                result.ErrorCount = allData.Count;
                result.ErrorMessage = ex.Message;
                progress?.Report($"保存失败: {ex.Message}");
                ErrorLogger.Log(ex, "TencentRealtimeService.SyncAllStocksAsync - SaveToDb");
            }
        }
        else
        {
            progress?.Report("没有获取到有效数据");
        }

        return result;
    }

    private AppDbContext GetDailyDataContext()
    {
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
    public decimal Amount { get; set; }            // 成交额(万)
    public decimal Amplitude { get; set; }          // 振幅(%)
    public decimal ChangePercent { get; set; }      // 涨跌幅(%)
    public decimal ChangeAmount { get; set; }       // 涨跌额(元)
    public decimal TurnoverRate { get; set; }      // 换手率(%)
    public decimal HighPrice { get; set; }          // 最高
    public decimal LowPrice { get; set; }          // 最低
    public decimal Buy1 { get; set; }               // 买一价
    public decimal Buy2 { get; set; }               // 买二价
    public decimal Buy3 { get; set; }               // 买三价
    public decimal Buy4 { get; set; }               // 买四价
    public decimal Buy5 { get; set; }               // 买五价
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
