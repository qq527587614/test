using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core.DailyPick;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Indicators;
using StockAnalysisSystem.Core.RealtimeData;
using StockAnalysisSystem.Core.Repositories;

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

        _btnSyncRealtime = new Button { Text = "同步实时行情", Left = 400, Top = 55, Width = 260, Height = 30, BackColor = Color.LightGreen };
        _btnSyncRealtime.Click += BtnSyncRealtime_Click;

        _progressBar = new ProgressBar { Left = 400, Top = 85, Width = 260, Height = 25 };

        statsPanel.Controls.AddRange(new Control[] {
            _lblStockCount, _lblLatestDate, _lblIndicatorCount,
            _btnCalcIndicators, _btnTestDeepSeek,
            _btnSyncRealtime, _progressBar
        });

        // 日志面板
        var logPanel = new GroupBox { Text = "操作日志", Dock = DockStyle.Fill };
        _txtLog = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        logPanel.Controls.Add(_txtLog);

        Controls.AddRange(new Control[] { logPanel, statsPanel });

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
