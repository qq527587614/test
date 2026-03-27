using ScottPlot;
using ScottPlot.WinForms;
using StockAnalysisSystem.Core.Models;
using StockAnalysisSystem.Core.Services;

namespace StockAnalysisSystem.UI.Forms;

public partial class KLineForm : Form
{
    private readonly IKLineDataService _kLineDataService;
    private readonly IServiceProvider _serviceProvider;
    private string _stockCode;
    private PeriodType _currentPeriod;
    private FormsPlot _plotControl = null!;
    private List<KLineData> _kLineData = new();

    public KLineForm(IServiceProvider serviceProvider, IKLineDataService kLineDataService, string stockCode)
    {
        _serviceProvider = serviceProvider;
        _kLineDataService = kLineDataService;
        _stockCode = stockCode;
        _currentPeriod = PeriodType.Daily;

        InitializeComponent();
        InitializePlot();
        InitializeUI();
    }

    private void InitializeUI()
    {
        this.Text = $"股票K线图 - {_stockCode}";
        this.Size = new Size(1200, 800);
        this.MinimumSize = new Size(800, 600);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.lblStockCode.Text = $"股票代码: {_stockCode}";
    }

    private void InitializePlot()
    {
        _plotControl = new FormsPlot
        {
            Dock = DockStyle.Fill
        };

        // 配置图表
        _plotControl.Plot.Axes.DateTimeTicksBottom();
        _plotControl.Plot.Title($"股票K线图 - {_stockCode}");
        _plotControl.Plot.YLabel("价格");
        _plotControl.Plot.XLabel("日期");

        this.Controls.Add(_plotControl);
        _plotControl.BringToFront();
    }

    private async void KLineForm_Load(object sender, EventArgs e)
    {
        await LoadKLineDataAsync();
    }

    private async Task LoadKLineDataAsync()
    {
        try
        {
            UpdateStatus("正在加载K线数据...");

            var kLineData = await _kLineDataService.GetKLineDataAsync(_stockCode, _currentPeriod, 100);

            if (kLineData == null || kLineData.Count == 0)
            {
                MessageBox.Show($"未找到股票 {_stockCode} 的K线数据", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("无数据");
                return;
            }

            _kLineData = kLineData;
            UpdateChart(kLineData);
            UpdateStatus($"已加载 {kLineData.Count} 条{_currentPeriod}数据 | {kLineData.First().Date:yyyy-MM-dd} ~ {kLineData.Last().Date:yyyy-MM-dd}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载K线数据失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateStatus("加载失败");
        }
    }

    private void UpdateChart(List<KLineData> kLineData)
    {
        // 清除现有图表
        _plotControl.Plot.Clear();

        // 转换为数组
        var dates = kLineData.Select(d => d.Date.ToOADate()).ToArray();
        var opens = kLineData.Select(d => (double)d.Open).ToArray();
        var highs = kLineData.Select(d => (double)d.High).ToArray();
        var lows = kLineData.Select(d => (double)d.Low).ToArray();
        var closes = kLineData.Select(d => (double)d.Close).ToArray();

        // 添加收盘价线（散点图）
        var closeLine = _plotControl.Plot.Add.Scatter(dates, closes);
        closeLine.Color = new ScottPlot.Color(0, 0, 0); // 黑色
        closeLine.LineWidth = 2;
        closeLine.MarkerSize = 2;
        closeLine.LegendText = "收盘价";

        // 添加开盘价线
        var openLine = _plotControl.Plot.Add.Scatter(dates, opens);
        openLine.Color = new ScottPlot.Color(255, 165, 0); // 橙色
        openLine.LineWidth = 1;
        openLine.MarkerSize = 1;
        openLine.LegendText = "开盘价";

        // 添加最高价和最低价线
        var highLine = _plotControl.Plot.Add.Scatter(dates, highs);
        highLine.Color = new ScottPlot.Color(255, 0, 0); // 红色
        highLine.LineWidth = 1;
        highLine.MarkerSize = 1;
        highLine.LegendText = "最高价";

        var lowLine = _plotControl.Plot.Add.Scatter(dates, lows);
        lowLine.Color = new ScottPlot.Color(0, 128, 0); // 绿色
        lowLine.LineWidth = 1;
        lowLine.MarkerSize = 1;
        lowLine.LegendText = "最低价";

        // 配置坐标轴
        _plotControl.Plot.Axes.DateTimeTicksBottom();

        // 刷新图表
        _plotControl.Refresh();
    }

    private async void BtnDaily_Click(object? sender, EventArgs e)
    {
        if (_currentPeriod == PeriodType.Daily) return;

        _currentPeriod = PeriodType.Daily;
        btnDaily.Checked = true;
        btnWeekly.Checked = false;
        btnMonthly.Checked = false;

        await LoadKLineDataAsync();
    }

    private async void BtnWeekly_Click(object? sender, EventArgs e)
    {
        if (_currentPeriod == PeriodType.Weekly) return;

        _currentPeriod = PeriodType.Weekly;
        btnDaily.Checked = false;
        btnWeekly.Checked = true;
        btnMonthly.Checked = false;

        await LoadKLineDataAsync();
    }

    private async void BtnMonthly_Click(object? sender, EventArgs e)
    {
        if (_currentPeriod == PeriodType.Monthly) return;

        _currentPeriod = PeriodType.Monthly;
        btnDaily.Checked = false;
        btnWeekly.Checked = false;
        btnMonthly.Checked = true;

        await LoadKLineDataAsync();
    }

    private async void BtnRefresh_Click(object? sender, EventArgs e)
    {
        await LoadKLineDataAsync();
    }

    private void UpdateStatus(string message)
    {
        if (this.statusStrip1 != null && this.toolStripStatusLabel1 != null)
        {
            this.toolStripStatusLabel1.Text = message;
        }
    }
}
