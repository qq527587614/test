using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core;
using StockAnalysisSystem.Core.Backtest.V2;
using StockAnalysisSystem.Core.Services;
using StockAnalysisSystem.Core.Strategies.Rules;
using StockAnalysisSystem.Tools;

var configBuilder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false);

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
