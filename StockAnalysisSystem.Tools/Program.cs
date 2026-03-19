using Microsoft.Extensions.Configuration;
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
