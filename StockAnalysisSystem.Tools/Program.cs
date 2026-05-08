using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core;
using StockAnalysisSystem.Core.Backtest.V2;
using StockAnalysisSystem.Core.RealtimeData;
using StockAnalysisSystem.Core.Services;
using StockAnalysisSystem.Core.Strategies.Rules;
using StockAnalysisSystem.Tools;

static string? GetArg(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
        if (i + 1 >= args.Length) return null;
        return args[i + 1];
    }
    return null;
}

var configBuilder = new ConfigurationBuilder()
    // 兼容：
    // - dotnet run 从仓库根目录启动（appsettings.json 在 Tools 目录下）
    // - 发布/输出目录运行（appsettings.json 会被复制到输出目录）
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile(Path.Combine("StockAnalysisSystem.Tools", "appsettings.json"), optional: true);

var configuration = configBuilder.Build();
var connectionString = configuration.GetConnectionString("MySql");

if (string.IsNullOrEmpty(connectionString))
{
    Console.WriteLine("错误：数据库连接字符串为空");
    return;
}

if (args.Length > 0 && string.Equals(args[0], "sync-limitup-30d", StringComparison.OrdinalIgnoreCase))
{
    // 示例：
    // dotnet run --project StockAnalysisSystem.Tools -- sync-limitup-30d
    // dotnet run --project StockAnalysisSystem.Tools -- sync-limitup-30d --days 30 --end 2026-05-07
    var days = 30;
    DateTime? end = null;
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] == "--days" && i + 1 < args.Length && int.TryParse(args[i + 1], out var d))
        {
            days = d;
            i++;
            continue;
        }
        if (args[i] == "--end" && i + 1 < args.Length && DateTime.TryParse(args[i + 1], out var e))
        {
            end = e.Date;
            i++;
            continue;
        }
    }

    var services = new ServiceCollection();
    services.AddStockAnalysisServices(configuration);
    var sp = services.BuildServiceProvider();

    var svc = sp.GetRequiredService<LimitUpSyncService>();
    var progress = new Progress<string>(msg => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}"));

    Console.WriteLine($"[LimitUp] Sync recent {days} days  End={(end ?? DateTime.Today):yyyy-MM-dd}");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var result = await svc.SyncRecentDaysAsync(days, end, clearExistingInRange: true, progress);
    sw.Stop();
    Console.WriteLine($"[Done] Elapsed={sw.Elapsed}");
    Console.WriteLine($"Range={result.StartDate:yyyy-MM-dd}..{result.EndDate:yyyy-MM-dd}  OkDays={result.OkDays}  EmptyDays={result.EmptyDays}  FailedDays={result.FailedDays}  InsertedRows={result.InsertedRows}");
    return;
}

if (args.Length > 0 && string.Equals(args[0], "diagnose-hotspot", StringComparison.OrdinalIgnoreCase))
{
    // 示例：
    // dotnet run --project StockAnalysisSystem.Tools -- diagnose-hotspot --code 600379 --date 2026-05-08
    var code = GetArg(args, "--code") ?? GetArg(args, "-c");
    if (string.IsNullOrWhiteSpace(code))
    {
        Console.WriteLine("用法：diagnose-hotspot --code <6位代码> [--date yyyy-MM-dd]");
        return;
    }

    var dateStr = GetArg(args, "--date");
    DateTime? date = null;
    if (!string.IsNullOrWhiteSpace(dateStr) && DateTime.TryParse(dateStr, out var d))
        date = d.Date;

    var services = new ServiceCollection();
    services.AddStockAnalysisServices(configuration);
    var sp = services.BuildServiceProvider();

    var picker = sp.GetRequiredService<HotSpotLimitUpMa5Picker>();
    var realtime = sp.GetRequiredService<TencentRealtimeService>();

    Console.WriteLine($"[HotSpot] Diagnose  Code={code}  Date={(date ?? DateTime.Today):yyyy-MM-dd}");
    var diag = await picker.DiagnoseAsync(code.Trim(), date);
    Console.WriteLine($"CoreSummary: {diag.Summary}");
    Console.WriteLine($"Core: HasLimitUpInWindow={diag.HasLimitUpInWindow}  First={diag.FirstLimitUpInWindow:yyyy-MM-dd}  Recent={diag.RecentLimitUpDate:yyyy-MM-dd}");
    Console.WriteLine($"Core: PrevTradeDate={diag.PrevTradeDate:yyyy-MM-dd}  HasDailyBars={diag.HasDailyBars}  HasPrevTradeBar={diag.HasPrevTradeBar}");
    Console.WriteLine($"Core: RtPx={diag.RealtimeCurrentPrice?.ToString("F2") ?? "-"}  RtOpen={diag.RealtimeOpenPrice?.ToString("F2") ?? "-"}  PassTrend={diag.PassTrendFilters}");
    Console.WriteLine($"Core: LimitPx={diag.TodayLimitPrice?.ToString("F2") ?? "-"}  Ma5WithLimit={diag.Ma5WithTodayLimit?.ToString("F2") ?? "-"}");

    // UI层过滤1：东方财富热度排名 Top200
    int? hotRank = null;
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        const string url = "https://data.eastmoney.com/dataapi/xuangu/list" +
                           "?st=CHANGE_RATE&sr=-1&ps=1000&p=1&sty=SECUCODE%2CSECURITY_CODE%2CSECURITY_NAME_ABBR%2CNEW_PRICE%2CCHANGE_RATE%2CVOLUME_RATIO%2CHIGH_PRICE%2CLOW_PRICE%2CPRE_CLOSE_PRICE%2CVOLUME%2CDEAL_AMOUNT%2CTURNOVERRATE%2CPOPULARITY_RANK" +
                           "&filter=(POPULARITY_RANK%3E0)(POPULARITY_RANK%3C%3D1000)&source=SELECT_SECURITIES&client=WEB";
        var json = await http.GetStringAsync(url);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("result", out var result) &&
            result.TryGetProperty("data", out var data) &&
            data.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("SECURITY_CODE", out var codeEl)) continue;
                var c = codeEl.GetString();
                if (!string.Equals(c, code, StringComparison.Ordinal)) continue;
                if (!item.TryGetProperty("POPULARITY_RANK", out var rankEl)) break;
                if (rankEl.ValueKind == System.Text.Json.JsonValueKind.Number && rankEl.TryGetInt32(out var r))
                    hotRank = r;
                break;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"UI: HotRank fetch failed: {ex.Message}");
    }
    Console.WriteLine($"UI: HotRank={(hotRank?.ToString() ?? "-")}  PassTop200={(hotRank.HasValue && hotRank.Value > 0 && hotRank.Value <= 200)}");

    // UI层过滤2：实时最低价 < 含涨停假设MA5
    if (diag.Ma5WithTodayLimit is { } ma5WithLimit && ma5WithLimit > 0m)
    {
        try
        {
            string ToTencentQuoteCode(string code6)
            {
                if (string.IsNullOrWhiteSpace(code6) || code6.Length < 2)
                    return "sz" + code6;
                if (code6.StartsWith("60", StringComparison.Ordinal) || code6.StartsWith("68", StringComparison.Ordinal))
                    return "sh" + code6;
                if (code6.StartsWith("00", StringComparison.Ordinal) || code6.StartsWith("30", StringComparison.Ordinal))
                    return "sz" + code6;
                if (code6.StartsWith("43", StringComparison.Ordinal) || code6.StartsWith("83", StringComparison.Ordinal) ||
                    code6.StartsWith("87", StringComparison.Ordinal) || code6.StartsWith("92", StringComparison.Ordinal))
                    return "bj" + code6;
                return "sz" + code6;
            }

            var q = ToTencentQuoteCode(code);
            var list = await realtime.GetRealtimeDataAsync(new List<string> { q });
            var rt = list.FirstOrDefault(x => string.Equals(x.StockCode, code, StringComparison.Ordinal));
            if (rt == null)
            {
                Console.WriteLine("UI: Realtime not found (rt=null) => 页面展示会剔除。");
            }
            else
            {
                Console.WriteLine($"UI: RtLow={rt.LowPrice:F2}  Ma5WithLimit={ma5WithLimit:F2}  PassLowFilter={(rt.LowPrice > 0m && rt.LowPrice < ma5WithLimit)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UI: Realtime low check failed: {ex.Message}");
        }
    }
    else
    {
        Console.WriteLine("UI: Ma5WithLimit unavailable => 无法判断实时最低价过滤。");
    }

    return;
}

if (args.Length > 0 && string.Equals(args[0], "backtest-firstboard", StringComparison.OrdinalIgnoreCase))
{
    // 示例：
    // dotnet run --project StockAnalysisSystem.Tools -- backtest-firstboard --days 180 --max-stocks 200
    var days = 180;
    var maxStocks = 200;
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] == "--days" && i + 1 < args.Length && int.TryParse(args[i + 1], out var d))
        {
            days = d;
            i++;
            continue;
        }
        if (args[i] == "--max-stocks" && i + 1 < args.Length && int.TryParse(args[i + 1], out var m))
        {
            maxStocks = m;
            i++;
            continue;
        }
    }

    var services = new ServiceCollection();
    services.AddStockAnalysisServices(configuration);
    var sp = services.BuildServiceProvider();

    var runner = sp.GetRequiredService<StrategyBacktesterV2>();
    var def = new StrategyDefinition
    {
        Name = "首板后回落（方案A）",
        EntryRule = new FirstBoardPullbackNode(),
        ExitRule = null,
        Parameters = new()
    };

    var strategy = new RuleBasedStrategyAdapter(def);
    var end = DateTime.Today.Date;
    var start = end.AddDays(-days);

    Console.WriteLine($"[Backtest] {strategy.Name}  Range={start:yyyy-MM-dd}..{end:yyyy-MM-dd}  MaxStocks={maxStocks}");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        var result = await runner.RunAsync(strategy, new StrategyBacktestSettingsV2
        {
            StartDate = start,
            EndDate = end,
            InitialCapital = 1_000_000m,
            MaxPositions = 10,
            StopLossPercent = -5m,
            MaxStockCount = maxStocks
        });

        sw.Stop();
        var m = result.Metrics;
        Console.WriteLine($"[Done] Elapsed={sw.Elapsed}");
        Console.WriteLine($"FinalEquity={result.FinalEquity:F2}");
        Console.WriteLine($"TotalReturn={m.TotalReturn:P2}  AnnualReturn={m.AnnualReturn:P2}  MaxDrawdown={m.MaxDrawdown:P2}");
        Console.WriteLine($"Trades={m.TradeCount}  WinRate={m.WinRate:P1}");
        return;
    }
    catch (Exception ex)
    {
        sw.Stop();
        Console.WriteLine($"[Fail] Elapsed={sw.Elapsed}");
        Console.WriteLine(ex.ToString());
        return;
    }
}

Console.WriteLine("股票分析系统 - 数据库初始化工具");
Console.WriteLine("================================");

var initializer = new DatabaseInitializer(connectionString);

// 1. 测试数据库连接
Console.WriteLine("\n步骤1: 测试数据库连接...");
if (!await initializer.TestConnectionAsync())
{
    Console.WriteLine("数据库连接失败，请检查连接字符串");
    return;
}

// 2. 显示当前数据库信息
Console.WriteLine("\n步骤2: 查看当前数据库信息...");
await initializer.ShowDatabaseInfoAsync();

// 3. 执行数据库初始化脚本
Console.WriteLine("\n步骤3: 执行数据库初始化脚本...");
var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "init_database.sql");
await initializer.ExecuteScriptAsync(scriptPath);

// 4. 验证表是否创建成功
Console.WriteLine("\n步骤4: 验证表是否创建成功...");
var tables = new[] { "Strategy", "BacktestTask", "OptimizationTask", "DailyPick", "DeepSeekLog", "StockDailyIndicator" };
bool allTablesExist = true;

foreach (var table in tables)
{
    var exists = await initializer.TableExistsAsync(table);
    Console.WriteLine($"  {table}: {(exists ? "存在" : "不存在")}");
    if (!exists) allTablesExist = false;
}

if (allTablesExist)
{
    Console.WriteLine("\n✅ 所有表创建成功！");
    Console.WriteLine("\n步骤5: 最终数据库信息...");
    await initializer.ShowDatabaseInfoAsync();
}
else
{
    Console.WriteLine("\n⚠️ 部分表创建失败，请检查错误信息");
}

Console.WriteLine("\n数据库初始化完成！");
