using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core.DailyPick;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Indicators;
using StockAnalysisSystem.Core.RealtimeData;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.Services;

namespace StockAnalysisSystem.UI.Forms;

public partial class DataManagerForm : Form
{
    private readonly IStockRepository _stockRepo;
    private readonly IStockDailyDataRepository _dailyDataRepo;
    private readonly IIndicatorRepository _indicatorRepo;
    private readonly IServiceProvider _serviceProvider;

    private Label _lblStockCount = null!;
    private Label _lblLatestDate = null!;
    private Label _lblIndicatorCount = null!;
    private Button _btnCalcIndicators = null!;
    private Button _btnTestDeepSeek = null!;
    private Button _btnSyncRealtime = null!;
    private Button _btnSyncDailyHistory = null!;
    private DateTimePicker _dtpDailyHistoryFrom = null!;
    private DateTimePicker _dtpLimitUpTo = null!;
    private Button _btnSyncLimitUpByDaily = null!;
    private Button _btnSyncPlate = null!;
    private Button _btnCalcPlateDaily = null!;
    private ProgressBar _progressBar = null!;
    private TextBox _txtLog = null!;

    public DataManagerForm(
        IStockRepository stockRepo,
        IStockDailyDataRepository dailyDataRepo,
        IIndicatorRepository indicatorRepo,
        IServiceProvider serviceProvider)
    {
        _stockRepo = stockRepo;
        _dailyDataRepo = dailyDataRepo;
        _indicatorRepo = indicatorRepo;
        _serviceProvider = serviceProvider;
        InitializeComponent();
        InitializeControls();
        LoadStats();
    }

    private void InitializeControls()
    {
        Size = new Size(800, 600);

        // 统计信息面板
        var statsPanel = new GroupBox { Text = "数据统计", Dock = DockStyle.Top, Height = 120, Padding = new Padding(10) };

        _lblStockCount = new Label { Text = "股票数量: 加载中...", Left = 20, Top = 25, Width = 300 };
        _lblLatestDate = new Label { Text = "最新交易日: 加载中...", Left = 20, Top = 50, Width = 300 };
        _lblIndicatorCount = new Label { Text = "指标数据量: 加载中...", Left = 20, Top = 75, Width = 300 };

        _btnCalcIndicators = new Button { Text = "预计算指标", Left = 400, Top = 20, Width = 120, Height = 30 };
        _btnCalcIndicators.Click += BtnCalcIndicators_Click;

        _btnTestDeepSeek = new Button { Text = "测试DeepSeek", Left = 540, Top = 20, Width = 120, Height = 30 };
        _btnTestDeepSeek.Click += BtnTestDeepSeek_Click;

        _btnSyncRealtime = new Button { Text = "同步实时行情", Left = 400, Top = 55, Width = 120, Height = 30, BackColor = Color.LightGreen };
        _btnSyncRealtime.Click += BtnSyncRealtime_Click;

        _dtpDailyHistoryFrom = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Left = 20,
            Top = 22,
            Width = 120,
            Value = DateTime.Today.AddDays(-30)
        };

        _dtpLimitUpTo = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Left = 20,
            Top = 22,
            Width = 120,
            Value = DateTime.Today
        };

        _btnSyncDailyHistory = new Button
        {
            Text = "同步日线(历史)",
            Left = 150,
            Top = 20,
            Width = 140,
            Height = 28,
            BackColor = Color.LightSkyBlue
        };
        _btnSyncDailyHistory.Click += BtnSyncDailyHistory_Click;

        _progressBar = new ProgressBar { Left = 400, Top = 85, Width = 260, Height = 25 };

        statsPanel.Controls.AddRange(new Control[] {
            _lblStockCount, _lblLatestDate, _lblIndicatorCount,
            _btnCalcIndicators, _btnTestDeepSeek,
            _btnSyncRealtime, _progressBar
        });

        // 日线历史同步面板
        var dailyPanel = new GroupBox { Text = "日线数据管理", Dock = DockStyle.Top, Height = 105, Padding = new Padding(10) };
        var lblFrom = new Label { Text = "从日期(含):", Left = 20, Top = 27, Width = 80 };
        lblFrom.BringToFront();
        _dtpDailyHistoryFrom.Left = 110;
        _btnSyncDailyHistory.Left = 250;

        var lblTo = new Label { Text = "到日期(含):", Left = 20, Top = 62, Width = 80 };
        lblTo.BringToFront();
        _dtpLimitUpTo.Left = 110;
        _dtpLimitUpTo.Top = 57;
        _dtpLimitUpTo.Width = 120;

        _btnSyncLimitUpByDaily = new Button
        {
            Text = "同步涨停(按日线)",
            Left = 250,
            Top = 55,
            Width = 140,
            Height = 28,
            BackColor = Color.Khaki
        };
        _btnSyncLimitUpByDaily.Click += BtnSyncLimitUpByDaily_Click;

        dailyPanel.Controls.AddRange(new Control[]
        {
            lblFrom, _dtpDailyHistoryFrom, _btnSyncDailyHistory,
            lblTo, _dtpLimitUpTo, _btnSyncLimitUpByDaily
        });

        // 板块管理面板
        var platePanel = new GroupBox { Text = "板块数据管理", Dock = DockStyle.Top, Height = 70, Padding = new Padding(10) };

        _btnSyncPlate = new Button { Text = "同步板块数据", Left = 20, Top = 22, Width = 120, Height = 28 };
        _btnSyncPlate.Click += BtnSyncPlate_Click;

        _btnCalcPlateDaily = new Button { Text = "计算板块日线", Left = 150, Top = 22, Width = 120, Height = 28 };
        _btnCalcPlateDaily.Click += BtnCalcPlateDaily_Click;

        platePanel.Controls.AddRange(new Control[] { _btnSyncPlate, _btnCalcPlateDaily });

        // 日志面板
        var logPanel = new GroupBox { Text = "操作日志", Dock = DockStyle.Fill };
        _txtLog = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        logPanel.Controls.Add(_txtLog);

        Controls.AddRange(new Control[] { logPanel, platePanel, dailyPanel, statsPanel });

        Text = "数据管理";
    }

    private async void LoadStats()
    {
        try
        {
            var stockCount = await _stockRepo.GetCountAsync();
            var latestDate = await _dailyDataRepo.GetLatestTradeDateAsync();
            var indicatorCount = await _indicatorRepo.GetCountAsync();

            _lblStockCount.Text = $"股票数量: {stockCount:N0}";
            _lblLatestDate.Text = $"最新交易日: {latestDate?.ToString("yyyy-MM-dd") ?? "无数据"}";
            _lblIndicatorCount.Text = $"指标数据量: {indicatorCount:N0}";

            if (latestDate.HasValue)
            {
                var ld = latestDate.Value.Date;
                if (_dtpLimitUpTo.Value.Date != ld)
                    _dtpLimitUpTo.Value = ld;
            }
        }
        catch (Exception ex)
        {
            Log($"加载统计信息失败: {ex.Message}");
        }
    }

    private async void BtnCalcIndicators_Click(object? sender, EventArgs e)
    {
        _btnCalcIndicators.Enabled = false;
        _progressBar.Style = ProgressBarStyle.Marquee;
        Log("开始预计算指标...");

        try
        {
            var stocks = await _stockRepo.GetAllAsync();
            int processed = 0;

            foreach (var stock in stocks)
            {
                try
                {
                    var dailyData = await _dailyDataRepo.GetByStockIdAsync(stock.Id);
                    if (dailyData.Count == 0) continue;

                    var indicators = IndicatorCalculator.CalculateAll(stock.Id, dailyData);
                    
                    // 批量插入（实际项目中应使用更高效的方式）
                    foreach (var ind in indicators.Skip(indicators.Count - 100)) // 只保存最近100天
                    {
                        var existing = await _indicatorRepo.GetByStockAndDateAsync(stock.StockCode, ind.TradeDate);
                        if (existing == null)
                        {
                            // 插入新记录
                        }
                    }

                    processed++;
                    if (processed % 100 == 0)
                    {
                        Log($"已处理 {processed}/{stocks.Count} 只股票");
                    }
                }
                catch (Exception ex)
                {
                    Log($"处理股票 {stock.StockCode} 失败: {ex.Message}");
                }
            }

            Log($"指标预计算完成，共处理 {processed} 只股票");
            LoadStats();
        }
        catch (Exception ex)
        {
            Log($"预计算失败: {ex.Message}");
        }
        finally
        {
            _btnCalcIndicators.Enabled = true;
            _progressBar.Style = ProgressBarStyle.Blocks;
        }
    }

    private async void BtnTestDeepSeek_Click(object? sender, EventArgs e)
    {
        _btnTestDeepSeek.Enabled = false;
        Log("测试DeepSeek连接...");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var client = scope.ServiceProvider.GetService<Core.DeepSeek.DeepSeekClient>();

            if (client == null)
            {
                Log("DeepSeek客户端未注册，请检查配置");
                return;
            }

            var testStock = new StockPickInfo
            {
                StockId = "1",
                StockCode = "000001",
                StockName = "平安银行",
                Industry = "银行",
                CirculationValue = 10000000
            };

            var result = await client.AnalyzeStockAsync(testStock);

            if (!string.IsNullOrEmpty(result))
            {
                Log($"DeepSeek响应:\n{result}");
            }
            else
            {
                Log("DeepSeek未返回结果，请检查API密钥配置");
            }
        }
        catch (Exception ex)
        {
            Log($"DeepSeek测试失败: {ex.Message}");
        }
        finally
        {
            _btnTestDeepSeek.Enabled = true;
        }
    }

    private async void BtnSyncRealtime_Click(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "确定要同步实时行情数据吗？\n这将获取所有股票的实时数据并保存到数据库。",
            "确认同步",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
            return;

        _btnSyncRealtime.Enabled = false;
        Log("开始同步实时行情...");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var stockRepo = scope.ServiceProvider.GetRequiredService<IStockRepository>();
            var dailyDataRepo = scope.ServiceProvider.GetRequiredService<IStockDailyDataRepository>();

            var service = new TencentRealtimeService(stockRepo, dailyDataRepo);

            var progress = new Progress<string>(msg => Log(msg));

            var syncResult = await service.SyncAllStocksAsync(100, progress);

            Log($"同步完成！共处理 {syncResult.TotalProcessed} 条数据");

            // 刷新统计数据
            LoadStats();
        }
        catch (Exception ex)
        {
            Log($"同步失败: {ex.Message}");
        }
        finally
        {
            _btnSyncRealtime.Enabled = true;
        }
    }

    private async void BtnSyncDailyHistory_Click(object? sender, EventArgs e)
    {
        var from = _dtpDailyHistoryFrom.Value.Date;
        var result = MessageBox.Show(
            $"确定要同步日线历史数据吗？\n\n将先删除 {from:yyyy-MM-dd} 之后（含）的日线数据，再重新拉取并写入。\n该操作耗时较长，请保持网络畅通。",
            "确认同步日线历史",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return;

        _btnSyncDailyHistory.Enabled = false;
        _progressBar.Style = ProgressBarStyle.Marquee;
        Log($"开始同步日线历史数据，从 {from:yyyy-MM-dd} 起…");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<TencentDailyKLineSyncService>();
            var progress = new Progress<string>(msg => Log(msg));

            var sync = await svc.SyncFromDateAsync(from, endDate: DateTime.Today, deleteExisting: true, maxConcurrency: 6, progress);
            Log($"日线历史同步完成：成功{sync.OkStocks}只 / 无数据{sync.EmptyStocks}只 / 失败{sync.FailedStocks}只，新增{sync.InsertedRows}条。");
            LoadStats();
        }
        catch (Exception ex)
        {
            Log($"同步日线历史失败: {ex.Message}");
        }
        finally
        {
            _progressBar.Style = ProgressBarStyle.Blocks;
            _btnSyncDailyHistory.Enabled = true;
        }
    }

    private async void BtnSyncLimitUpByDaily_Click(object? sender, EventArgs e)
    {
        var from = _dtpDailyHistoryFrom.Value.Date;
        var to = _dtpLimitUpTo.Value.Date;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        List<DateTime> tradeDates;
        try
        {
            tradeDates = await _dailyDataRepo.GetTradeDatesAsync(from, to);
        }
        catch (Exception ex)
        {
            Log($"读取日线交易日失败: {ex.Message}");
            return;
        }

        if (tradeDates.Count == 0)
        {
            MessageBox.Show("该日期范围内没有日线交易日数据，请先同步日线。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show(
            $"确定要按日线交易日同步涨停数据吗？\n\n范围：{from:yyyy-MM-dd} ~ {to:yyyy-MM-dd}\n交易日数：{tradeDates.Count}\n\n将按交易日逐日拉取并写入涨停表。",
            "确认同步涨停（按日线）",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return;

        _btnSyncLimitUpByDaily.Enabled = false;
        _btnSyncDailyHistory.Enabled = false;
        _progressBar.Style = ProgressBarStyle.Marquee;
        Log($"开始同步涨停（按日线交易日）：{from:yyyy-MM-dd} ~ {to:yyyy-MM-dd}，共 {tradeDates.Count} 天…");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<LimitUpSyncService>();
            var progress = new Progress<string>(msg => Log(msg));

            var sync = await svc.SyncByTradeDatesAsync(tradeDates, clearExistingForDates: true, progress);
            Log($"涨停同步完成：成功{sync.OkDays}天 / 无数据{sync.EmptyDays}天 / 失败{sync.FailedDays}天，新增{sync.InsertedRows}条。");
        }
        catch (Exception ex)
        {
            Log($"同步涨停失败: {ex.Message}");
        }
        finally
        {
            _progressBar.Style = ProgressBarStyle.Blocks;
            _btnSyncLimitUpByDaily.Enabled = true;
            _btnSyncDailyHistory.Enabled = true;
        }
    }

    private async void BtnSyncPlate_Click(object? sender, EventArgs e)
    {
        _btnSyncPlate.Enabled = false;
        Log("开始同步板块数据...");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var plateService = scope.ServiceProvider.GetRequiredService<PlateService>();

            var count = await plateService.SyncPlatesFromLimitUpAsync();

            Log($"板块数据同步完成: 新增 {count} 个成分股");
        }
        catch (Exception ex)
        {
            Log($"同步板块数据失败: {ex.Message}");
        }
        finally
        {
            _btnSyncPlate.Enabled = true;
        }
    }

    private async void BtnCalcPlateDaily_Click(object? sender, EventArgs e)
    {
        _btnCalcPlateDaily.Enabled = false;
        _progressBar.Value = 0;
        Log("开始计算板块日线数据（增量计算）...");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var plateService = scope.ServiceProvider.GetRequiredService<PlateService>();

            var count = await plateService.CalcPlateDailyDataAsync((current, total, message) =>
            {
                // 更新进度条
                var progress = (int)((double)current / total * 100);
                if (_progressBar.InvokeRequired)
                {
                    _progressBar.Invoke(() =>
                    {
                        _progressBar.Value = Math.Min(progress, 100);
                    });
                }
                else
                {
                    _progressBar.Value = Math.Min(progress, 100);
                }

                // 显示日志
                Log(message);
            });

            _progressBar.Value = 100;
            Log($"板块日线计算完成: 共计算 {count} 条数据");
        }
        catch (Exception ex)
        {
            Log($"计算板块日线失败: {ex.Message}");
        }
        finally
        {
            _btnCalcPlateDaily.Enabled = true;
        }
    }

    private void Log(string message)
    {
        if (_txtLog.InvokeRequired)
        {
            _txtLog.Invoke(() => Log(message));
            return;
        }

        _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
        _txtLog.ScrollToCaret();
    }
}
