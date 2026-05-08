using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StockAnalysisSystem.Core;

namespace StockAnalysisSystem_UI_WinUI.Services;

public static class AppServices
{
    public static string? DebugRepoRoot { get; private set; }

    public static IHost Host { get; } = Microsoft.Extensions.Hosting.Host
        .CreateDefaultBuilder()
        .ConfigureAppConfiguration((ctx, cfg) =>
        {
            // WinUI packaged app 通常不支持传统 appsettings 发现流程，这里显式加载
            cfg.Sources.Clear();
            cfg.SetBasePath(AppContext.BaseDirectory);

            cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            // 自动复用旧 WinForms UI 的连接串（避免重复填写）
            // 规则：向上查找解决方案根目录（含 StockAnalysisSystem.sln），再读取旧项目的 appsettings.json
            var repoRoot = TryFindRepoRoot(AppContext.BaseDirectory) ?? TryFindRepoRoot(Environment.CurrentDirectory);
            DebugRepoRoot = repoRoot;
            if (!string.IsNullOrWhiteSpace(repoRoot))
            {
                // 优先使用 Tools 项目的连接串（你原项目里已有）
                var legacyToolsSettings = Path.Combine(repoRoot, "StockAnalysisSystem.Tools", "appsettings.json");
                cfg.AddJsonFile(legacyToolsSettings, optional: true, reloadOnChange: false);

                // 兼容：UI 项目的连接串（若存在也可作为来源）
                var legacyUiSettings = Path.Combine(repoRoot, "StockAnalysisSystem.UI", "appsettings.json");
                cfg.AddJsonFile(legacyUiSettings, optional: true, reloadOnChange: false);
            }

            // 本地专用配置（不进 git）：在这里放 ConnectionStrings:MySql 等敏感信息
            cfg.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
            cfg.AddEnvironmentVariables(prefix: "SAS_");
        })
        .ConfigureServices((ctx, services) =>
        {
            // Core 依赖连接串；如果为空，会在首次数据库访问时报错（Runner 页会给出提示）。
            services.AddStockAnalysisServices(ctx.Configuration);
            services.AddSingleton<BacktestSessionState>();
        })
        .Build();

    public static IServiceProvider Provider => Host.Services;

    public static Microsoft.UI.Xaml.Window? MainWindow { get; set; }

    private static string? TryFindRepoRoot(string startDir)
    {
        try
        {
            var dir = new DirectoryInfo(startDir);
            for (var i = 0; i < 25 && dir != null; i++)
            {
                var slnPath = Path.Combine(dir.FullName, "StockAnalysisSystem.sln");
                if (File.Exists(slnPath))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }
}

public sealed class BacktestSessionState
{
    public StockAnalysisSystem.Core.Backtest.V2.BacktestResultV2? LastResult { get; set; }
}

