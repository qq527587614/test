using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StockAnalysisSystem.Core;
using StockAnalysisSystem.Core.RealtimeData;
using StockAnalysisSystem.Core.Services;

namespace StockAnalysisSystem.SyncWorker;

/// <summary>
/// 每分钟检查一次：本地时间已过配置的盘后时刻且当日尚未成功跑过，则执行同步。
/// 步骤：腾讯全市场实时→当日日线快照、财联社当日涨停、板块成分增量、板块日线增量。
/// </summary>
public sealed class PostMarketSyncWorker : BackgroundService
{
    private readonly IServiceProvider _root;
    private readonly IOptionsMonitor<PostMarketSyncOptions> _options;
    private readonly ILogger<PostMarketSyncWorker> _logger;
    private readonly string _statePath;
    private readonly string _fileLogPath;

    public PostMarketSyncWorker(
        IServiceProvider root,
        IOptionsMonitor<PostMarketSyncOptions> options,
        ILogger<PostMarketSyncWorker> logger)
    {
        _root = root;
        _options = options;
        _logger = logger;
        var dir = AppContext.BaseDirectory;
        _statePath = Path.Combine(dir, "post-market-sync-state.json");
        _fileLogPath = Path.Combine(dir, "logs", "post-market-sync.log");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("盘后同步 Worker 已启动；状态文件：{State}", _statePath);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
                var opt = _options.CurrentValue;
                var now = DateTime.Now;
                if (opt.RunOnlyWeekdays && now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;

                var trigger = now.Date.AddHours(NormalizeHour(opt.RunHour)).AddMinutes(NormalizeMinute(opt.RunMinute));
                if (now < trigger)
                    continue;

                if (await TryGetLastSuccessDateAsync(stoppingToken).ConfigureAwait(false) == now.Date)
                    continue;

                await RunPipelineAsync(opt, stoppingToken).ConfigureAwait(false);
                await WriteLastSuccessDateAsync(now.Date, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "盘后同步循环异常");
                AppendFileLog($"[{DateTime.Now:O}] ERROR {ex}");
            }
        }
    }

    private async Task RunPipelineAsync(PostMarketSyncOptions opt, CancellationToken ct)
    {
        _logger.LogInformation("开始执行盘后数据同步…");
        AppendFileLog($"[{DateTime.Now:O}] START pipeline");

        await using var scope = _root.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        if (opt.EnableRealtimeDaily)
        {
            var realtime = sp.GetRequiredService<TencentRealtimeService>();
            var progress = new Progress<string>(msg =>
            {
                _logger.LogInformation("{Msg}", msg);
                AppendFileLog($"[{DateTime.Now:O}] realtime {msg}");
            });
            var r = await realtime.SyncAllStocksAsync(opt.RealtimeBatchSize, progress).ConfigureAwait(false);
            _logger.LogInformation("腾讯日线快照完成：处理 {Count} 条。", r.TotalProcessed);
            AppendFileLog($"[{DateTime.Now:O}] realtime done rows={r.TotalProcessed}");
        }

        if (opt.EnableClsLimitUp)
        {
            var platePick = sp.GetRequiredService<PlateAnalysisPickDataService>();
            var n = await platePick.SyncClsLimitUpForDateAsync(DateTime.Today, ct).ConfigureAwait(false);
            _logger.LogInformation("财联社涨停同步完成：写入 {N}。", n);
            AppendFileLog($"[{DateTime.Now:O}] cls limit-up rows={n}");
        }

        if (opt.EnablePlateSync)
        {
            var plates = sp.GetRequiredService<PlateService>();
            var count = await plates.SyncPlatesFromLimitUpAsync().ConfigureAwait(false);
            _logger.LogInformation("板块成分增量完成：新增关系 {Count}。", count);
            AppendFileLog($"[{DateTime.Now:O}] plate stocks new={count}");
        }

        if (opt.EnablePlateDailyCalc)
        {
            var plates = sp.GetRequiredService<PlateService>();
            var n = await plates.CalcPlateDailyDataAsync((cur, tot, msg) =>
            {
                if (cur == 1 || cur == tot || cur % 10 == 0)
                {
                    _logger.LogDebug("板块日线 {Cur}/{Tot} {Msg}", cur, tot, msg);
                    AppendFileLog($"[{DateTime.Now:O}] plate-daily {cur}/{tot} {msg}");
                }
            }).ConfigureAwait(false);
            _logger.LogInformation("板块日线计算完成：{N} 条。", n);
            AppendFileLog($"[{DateTime.Now:O}] plate-daily done rows={n}");
        }

        _logger.LogInformation("盘后数据同步全部完成。");
        AppendFileLog($"[{DateTime.Now:O}] END pipeline ok");
    }

    private async Task<DateTime?> TryGetLastSuccessDateAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(_statePath))
                return null;
            await using var fs = File.OpenRead(_statePath);
            var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("LastSuccessDate", out var el) &&
                el.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(el.GetString(), out var d))
                return d.Date;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取同步状态文件失败，将视为未跑过。");
        }

        return null;
    }

    private async Task WriteLastSuccessDateAsync(DateTime date, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var json = JsonSerializer.Serialize(new { LastSuccessDate = date.ToString("yyyy-MM-dd") });
        await File.WriteAllTextAsync(_statePath, json, ct).ConfigureAwait(false);
    }

    private void AppendFileLog(string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_fileLogPath)!);
            File.AppendAllText(_fileLogPath, line + Environment.NewLine);
        }
        catch
        {
            // ignore disk errors
        }
    }

    private static int NormalizeHour(int h) => h is >= 0 and <= 23 ? h : 15;
    private static int NormalizeMinute(int m) => m is >= 0 and <= 59 ? m : 0;
}
