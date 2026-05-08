using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StockAnalysisSystem.Core.Backtest;
using StockAnalysisSystem.Core.Backtest.V2;
using StockAnalysisSystem.Core.Strategies.Rules;
using StockAnalysisSystem_UI_WinUI.Services;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace StockAnalysisSystem_UI_WinUI.Pages;

public sealed partial class BacktestRunnerPage : Page
{
    public BacktestRunnerPage()
    {
        InitializeComponent();
    }

    private StrategyBacktestSettingsV2 BuildSettingsFromUi()
    {
        static int ParseInt(string? s, int fallback)
            => int.TryParse((s ?? "").Trim(), out var v) ? v : fallback;

        static decimal ParseDecimal(string? s, decimal fallback)
            => decimal.TryParse((s ?? "").Trim(), out var v) ? v : fallback;

        var days = ParseInt(TxtDays?.Text, 365);
        if (days <= 0) days = 365;

        var maxStocks = ParseInt(TxtMaxStocks?.Text, 500);
        if (maxStocks < 0) maxStocks = 0; // 0 = 不限制

        var initial = ParseDecimal(TxtInitialCapital?.Text, 1_000_000m);
        if (initial <= 0) initial = 1_000_000m;

        var maxPos = ParseInt(TxtMaxPositions?.Text, 10);
        if (maxPos <= 0) maxPos = 10;

        var stopLossPct = ParseDecimal(TxtStopLossPct?.Text, -5m);

        var end = DateTime.Today.Date;
        var start = end.AddDays(-days);

        return new StrategyBacktestSettingsV2
        {
            StartDate = start,
            EndDate = end,
            InitialCapital = initial,
            MaxPositions = maxPos,
            StopLossPercent = stopLossPct,
            MaxStockCount = maxStocks
        };
    }

    private static List<int> ParseIdsCsv(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return new List<int>();
        return s
            .Split(new[] { ',', '，', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var v) ? v : 0)
            .Where(v => v > 0)
            .Distinct()
            .ToList();
    }

    private FirstBoardPullbackPortfolioSettings BuildFirstBoardPortfolioSettingsFromUi()
    {
        static int ParseInt(string? s, int fallback)
            => int.TryParse((s ?? "").Trim(), out var v) ? v : fallback;

        static decimal ParseDecimal(string? s, decimal fallback)
            => decimal.TryParse((s ?? "").Trim(), out var v) ? v : fallback;

        var ids = ParseIdsCsv(TxtFirstBoardCombineIds?.Text);
        var maxHold = ParseInt(TxtFirstBoardMaxHold?.Text, 3);
        if (maxHold < 1) maxHold = 1;

        var takeProfit = ParseDecimal(TxtFirstBoardTakeProfit?.Text, 1.5m);
        if (takeProfit < 0) takeProfit = 0;

        var maxSlots = ParseInt(TxtFirstBoardMaxSlots?.Text, 10);
        if (maxSlots < 1) maxSlots = 10;

        var maxPicks = ParseInt(TxtFirstBoardMaxPicks?.Text, 3);
        if (maxPicks < 1) maxPicks = 1;

        var minUp = ParseDecimal(TxtFirstBoardMinUpRatio?.Text, 0.45m);
        if (minUp < 0) minUp = 0;
        if (minUp > 1) minUp = 1;

        var enableFilter = ParseInt(TxtFirstBoardEnableMarketFilter?.Text, 1) != 0;

        var baseSettings = BuildSettingsFromUi();

        return new FirstBoardPullbackPortfolioSettings
        {
            StartDate = baseSettings.StartDate,
            EndDate = baseSettings.EndDate,
            CombineStrategyIds = ids,
            InitialCapital = baseSettings.InitialCapital,
            Commission = baseSettings.CommissionRate,
            Slippage = baseSettings.SlippageRate,
            MaxSlots = maxSlots,
            MaxHoldingSessionsAfterEntry = maxHold,
            EnableMarketFilter = enableFilter,
            MinMarketUpRatio = minUp,
            MaxPicksPerDay = maxPicks,
            TakeProfitMinPercent = takeProfit
        };
    }

    private void RunSmoke_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sp = AppServices.Provider;
            // 只验证 UI 自身与纯内存组件：避免未配置数据库时就报错
            _ = sp.GetRequiredService<BacktestSessionState>();
            _ = sp.GetRequiredService<AnalyticsServiceV2>();
            TxtStatus.Text = "界面运行正常：已成功解析基础服务（不需要数据库）。";
        }
        catch (Exception ex)
        {
            TxtStatus.Text =
                "界面基础服务初始化失败。\n\n" +
                ex.Message;
        }
    }

    private void TestDb_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sp = AppServices.Provider;
            var cfg = sp.GetRequiredService<IConfiguration>();
            var cs = cfg.GetConnectionString("MySql") ?? "";
            var csPreview = cs.Length <= 80 ? cs : cs.Substring(0, 80) + "...";
            TxtStatus.Text =
                $"配置诊断：RepoRoot={(AppServices.DebugRepoRoot ?? "(null)")}\n" +
                $"BaseDir={AppContext.BaseDirectory}\n" +
                $"CWD={Environment.CurrentDirectory}\n" +
                $"MySqlConn(len={cs.Length})={csPreview}\n\n";

            // 解析这些服务会触发 EF Core/MySql 初始化；若连接串为空或不可达，会在此报错
            _ = sp.GetRequiredService<BacktestEngineV2>();
            _ = sp.GetRequiredService<DailyPickBacktesterV2>();
            _ = sp.GetRequiredService<FirstBoardPullbackPortfolioBacktesterV2>();
            _ = sp.GetRequiredService<StrategyBacktesterV2>();
            TxtStatus.Text += "数据库相关服务初始化成功（不代表账号/权限一定正确，但连接串至少可用）。";
        }
        catch (Exception ex)
        {
            TxtStatus.Text +=
                "\n数据库连接/初始化失败。\n" +
                "连接串来源优先级：WinUI/appsettings.json -> Tools/appsettings.json -> UI/appsettings.json -> （可选）WinUI/appsettings.Local.json -> 环境变量 SAS_*。\n\n" +
                ex.Message;
        }
    }

    private void LoadSample_Click(object sender, RoutedEventArgs e)
    {
        var sp = AppServices.Provider;
        var state = sp.GetRequiredService<BacktestSessionState>();
        var analytics = sp.GetRequiredService<AnalyticsServiceV2>();

        var start = new DateTime(2024, 1, 1);
        var r = new BacktestResultV2
        {
            StartDate = start,
            EndDate = start.AddDays(39),
            InitialCapital = 100_000m,
            FinalEquity = 0m
        };

        var equity = 100_000m;
        var peak = equity;
        var rnd = new Random(1);
        for (var i = 0; i < 40; i++)
        {
            var d = start.AddDays(i);
            // 简单随机游走（含回撤段）
            var dailyRet = (decimal)(rnd.NextDouble() * 0.02 - 0.01); // -1%~+1%
            equity *= (1m + dailyRet);

            r.DailyReturns.Add(new ReturnPointV2 { Date = d, Return = dailyRet });

            if (equity > peak)
                peak = equity;
            var dd = peak > 0 ? (peak - equity) / peak : 0m;
            r.DrawdownCurve.Add(new DrawdownPointV2 { Date = d, PeakEquity = peak, Drawdown = dd });

            r.EquityCurve.Add(new PortfolioPointV2
            {
                Date = d,
                Cash = equity,
                PositionValue = 0m,
                Equity = equity
            });
        }

        r.FinalEquity = equity;
        analytics.Fill(r);
        state.LastResult = r;

        TxtStatus.Text = "示例结果已加载。请切到「回测报告」查看权益/回撤图。";
    }

    private async void RunRuleJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sp = AppServices.Provider;
            var state = sp.GetRequiredService<BacktestSessionState>();
            var runner = sp.GetRequiredService<StrategyBacktesterV2>();

            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            var hwnd = AppServices.MainWindow == null
                ? IntPtr.Zero
                : WinRT.Interop.WindowNative.GetWindowHandle(AppServices.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null)
            {
                TxtStatus.Text = "已取消选择。";
                return;
            }

            var json = await FileIO.ReadTextAsync(file);
            var def = JsonSerializer.Deserialize<StrategyDefinition>(json);
            if (def == null)
                throw new InvalidOperationException("策略 JSON 解析失败：StrategyDefinition 为 null。");

            var strategy = new RuleBasedStrategyAdapter(def);
            var settings = BuildSettingsFromUi();
            TxtStatus.Text =
                $"开始回测：{strategy.Name}\n" +
                $"区间：{settings.StartDate:yyyy-MM-dd}..{settings.EndDate:yyyy-MM-dd}\n" +
                $"MaxStocks={settings.MaxStockCount}  MaxPos={settings.MaxPositions}  SL={settings.StopLossPercent}%\n" +
                "时间可能较长，请稍候…";

            var result = await runner.RunAsync(strategy, settings);
            state.LastResult = result;
            TxtStatus.Text = $"回测完成：{strategy.Name}\n最终权益={result.FinalEquity:F2}\n请切到「回测报告」查看曲线与指标。";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "回测失败：\n" + ex.Message;
        }
    }

    private async void RunFirstBoardTemplate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sp = AppServices.Provider;
            var state = sp.GetRequiredService<BacktestSessionState>();
            var runner = sp.GetRequiredService<StrategyBacktesterV2>();

            var def = new StrategyDefinition
            {
                Name = "首板后回落（方案A）",
                EntryRule = new FirstBoardPullbackNode(),
                ExitRule = null,
                Parameters = new()
            };

            var strategy = new RuleBasedStrategyAdapter(def);
            var settings = BuildSettingsFromUi();
            TxtStatus.Text =
                $"开始回测：{strategy.Name}\n" +
                $"区间：{settings.StartDate:yyyy-MM-dd}..{settings.EndDate:yyyy-MM-dd}\n" +
                $"MaxStocks={settings.MaxStockCount}  MaxPos={settings.MaxPositions}  SL={settings.StopLossPercent}%\n" +
                "时间可能较长，请稍候…";

            var result = await runner.RunAsync(strategy, settings);
            state.LastResult = result;
            TxtStatus.Text = $"回测完成：{strategy.Name}\n最终权益={result.FinalEquity:F2}\n请切到「回测报告」查看曲线与指标。";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "回测失败：\n" + ex.Message;
        }
    }

    private async void RunFirstBoardPortfolio_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sp = AppServices.Provider;
            var state = sp.GetRequiredService<BacktestSessionState>();
            var runner = sp.GetRequiredService<FirstBoardPullbackPortfolioBacktesterV2>();

            var s = BuildFirstBoardPortfolioSettingsFromUi();
            if (s.CombineStrategyIds.Count == 0)
            {
                TxtStatus.Text = "请先填写“组合策略ID（逗号分隔）”，例如：12,15。";
                return;
            }

            TxtStatus.Text =
                "开始回测：首板回落短线（十仓组合）\n" +
                $"区间：{s.StartDate:yyyy-MM-dd}..{s.EndDate:yyyy-MM-dd}\n" +
                $"CombineIds={string.Join(",", s.CombineStrategyIds)}  MaxHold={s.MaxHoldingSessionsAfterEntry}  TP>={s.TakeProfitMinPercent}%\n" +
                $"UpDaySell=是  MarketFilter={(s.EnableMarketFilter ? "是" : "否")} (MinUp={s.MinMarketUpRatio})\n" +
                "时间可能较长，请稍候…";

            var result = await runner.RunAsync(s);
            state.LastResult = result;
            TxtStatus.Text = $"回测完成：首板回落短线（十仓组合）\n最终权益={result.FinalEquity:F2}\n请切到「回测报告」查看曲线与买卖明细。";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "回测失败：\n" + ex.Message;
        }
    }
}

