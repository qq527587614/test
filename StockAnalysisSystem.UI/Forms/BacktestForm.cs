using Microsoft.Extensions.DependencyInjection;
using ScottPlot.WinForms;
using StockAnalysisSystem.Core.Backtest;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.Strategies;

namespace StockAnalysisSystem.UI.Forms;

public partial class BacktestForm : Form
{
    private readonly BacktestEngine _backtestEngine;
    private readonly IStrategyRepository _strategyRepo;
    private readonly IServiceProvider _serviceProvider;

    private TabControl _tabControl = null!;
    private ComboBox _cboStrategy = null!;
    private DateTimePicker _dtpStart = null!;
    private DateTimePicker _dtpEnd = null!;
    private NumericUpDown _numCapital = null!;
    private NumericUpDown _numCommission = null!;
    private NumericUpDown _numSlippage = null!;
    private Button _btnRun = null!;
    private ProgressBar _progressBar = null!;
    private DataGridView _dataGridView = null!;
    private FormsPlot _equityChart = null!;
    private System.Windows.Forms.Label _lblResult = null!;

    // 每日选股回测控件
    private DateTimePicker _dtpPickStart = null!;
    private DateTimePicker _dtpPickEnd = null!;
    private NumericUpDown _numSharesPerPick = null!;
    private NumericUpDown _numPickCommission = null!;
    private NumericUpDown _numPickSlippage = null!;
    private CheckedListBox _lstPickStrategies = null!;
    private Button _btnRunPickBacktest = null!;
    private ProgressBar _progressBarPick = null!;
    private DataGridView _dataGridViewPick = null!;
    private System.Windows.Forms.Label _lblPickResult = null!;

    private List<Strategy> _strategies = new();
    private Dictionary<string, int> _strategyIdMap = new();

    public BacktestForm(BacktestEngine backtestEngine, IStrategyRepository strategyRepo, IServiceProvider serviceProvider)
    {
        _backtestEngine = backtestEngine;
        _strategyRepo = strategyRepo;
        _serviceProvider = serviceProvider;
        InitializeComponent();
        InitializeControls();
        LoadStrategies();
    }

    private void InitializeControls()
    {
        Size = new Size(1200, 800);

        // 创建标签页控件
        _tabControl = new TabControl { Dock = DockStyle.Fill };

        // 标签页1：策略回测
        var tabPageStrategy = new TabPage("策略回测");
        CreateStrategyBacktestUI(tabPageStrategy);
        _tabControl.TabPages.Add(tabPageStrategy);

        // 标签页2：每日选股回测
        var tabPagePick = new TabPage("每日选股回测");
        CreatePickBacktestUI(tabPagePick);
        _tabControl.TabPages.Add(tabPagePick);

        Controls.Add(_tabControl);
        Text = "回测系统";
    }

    private void CreateStrategyBacktestUI(Control parent)
    {
        // 参数设置面板
        var settingsPanel = new GroupBox { Text = "回测参数", Dock = DockStyle.Top, Height = 120, Padding = new Padding(10) };
        
        int y = 25;
        var lblStrategy = new Label { Text = "策略:", Left = 20, Top = y, Width = 60 };
        _cboStrategy = new ComboBox { Left = 80, Top = y - 3, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        
        var lblStart = new Label { Text = "开始日期:", Left = 300, Top = y, Width = 70 };
        _dtpStart = new DateTimePicker { Left = 380, Top = y - 3, Width = 150, Value = DateTime.Today.AddYears(-1) };
        
        var lblEnd = new Label { Text = "结束日期:", Left = 550, Top = y, Width = 70 };
        _dtpEnd = new DateTimePicker { Left = 630, Top = y - 3, Width = 150, Value = DateTime.Today };

        y += 35;
        var lblCapital = new Label { Text = "初始资金:", Left = 20, Top = y, Width = 60 };
        _numCapital = new NumericUpDown { Left = 80, Top = y - 3, Width = 120, Minimum = 10000, Maximum = 100000000, Value = 1000000, ThousandsSeparator = true };
        
        var lblCommission = new Label { Text = "手续费率:", Left = 220, Top = y, Width = 60 };
        _numCommission = new NumericUpDown { Left = 280, Top = y - 3, Width = 80, Minimum = 0, Maximum = 1, Value = (decimal)0.00025, DecimalPlaces = 5, Increment = (decimal)0.0001 };
        
        var lblSlippage = new Label { Text = "滑点:", Left = 380, Top = y, Width = 60 };
        _numSlippage = new NumericUpDown { Left = 440, Top = y - 3, Width = 80, Minimum = 0, Maximum = 1, Value = (decimal)0.001, DecimalPlaces = 4, Increment = (decimal)0.0001 };

        _btnRun = new Button { Text = "开始回测", Left = 600, Top = y - 3, Width = 100, Height = 28, BackColor = Color.LightBlue };
        _btnRun.Click += BtnRun_Click;

        _progressBar = new ProgressBar { Left = 720, Top = y, Width = 200, Height = 25 };

        settingsPanel.Controls.AddRange(new Control[] {
            lblStrategy, _cboStrategy, lblStart, _dtpStart, lblEnd, _dtpEnd,
            lblCapital, _numCapital, lblCommission, _numCommission, lblSlippage, _numSlippage,
            _btnRun, _progressBar
        });

        // 结果面板
        var resultPanel = new Panel { Dock = DockStyle.Fill };

        // 上部：结果统计
        var statsPanel = new GroupBox { Text = "回测结果", Dock = DockStyle.Top, Height = 100 };
        _lblResult = new Label { Dock = DockStyle.Fill, Padding = new Padding(10), Font = new Font("Microsoft YaHei", 10) };
        statsPanel.Controls.Add(_lblResult);

        // 中部：资金曲线图
        var chartPanel = new GroupBox { Text = "资金曲线", Dock = DockStyle.Top, Height = 300 };
        _equityChart = new FormsPlot { Dock = DockStyle.Fill };
        chartPanel.Controls.Add(_equityChart);

        // 下部：交易记录
        var tradePanel = new GroupBox { Text = "交易记录", Dock = DockStyle.Fill };
        _dataGridView = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false };
        _dataGridView.Columns.Add("StockCode", "股票代码");
        _dataGridView.Columns.Add("StockName", "股票名称");
        _dataGridView.Columns.Add("StrategyName", "策略名称");
        _dataGridView.Columns.Add("BuyDate", "买入日期");
        _dataGridView.Columns.Add("BuyPrice", "买入价格");
        _dataGridView.Columns.Add("Shares", "数量");
        _dataGridView.Columns.Add("SellDate", "卖出日期");
        _dataGridView.Columns.Add("SellPrice", "卖出价格");
        _dataGridView.Columns.Add("ProfitLoss", "盈亏");
        _dataGridView.Columns.Add("ProfitLossPercent", "盈亏%");
        _dataGridView.Columns.Add("HoldingDays", "持仓天数");
        _dataGridView.Columns.Add("Reason", "卖出原因");
        tradePanel.Controls.Add(_dataGridView);

        // 重要：添加顺序决定显示顺序（从下往上：Fill -> Top -> Top）
        resultPanel.Controls.Add(tradePanel);  // 底部：交易记录（Fill）
        resultPanel.Controls.Add(chartPanel);   // 中部：资金曲线
        resultPanel.Controls.Add(statsPanel);  // 上部：回测结果

        parent.Controls.AddRange(new Control[] { resultPanel, settingsPanel });
    }

    private void CreatePickBacktestUI(Control parent)
    {
        // 参数设置面板
        var settingsPanel = new GroupBox { Text = "每日选股回测参数", Dock = DockStyle.Top, Height = 180, Padding = new Padding(10) };

        int y = 25;
        var lblStart = new Label { Text = "开始日期:", Left = 20, Top = y, Width = 70 };
        _dtpPickStart = new DateTimePicker { Left = 90, Top = y - 3, Width = 150, Value = DateTime.Today.AddMonths(-3) };

        var lblEnd = new Label { Text = "结束日期:", Left = 260, Top = y, Width = 70 };
        _dtpPickEnd = new DateTimePicker { Left = 330, Top = y - 3, Width = 150, Value = DateTime.Today };

        y += 35;
        var lblStrategies = new Label { Text = "选择策略:", Left = 20, Top = y, Width = 70 };
        _lstPickStrategies = new CheckedListBox
        {
            Left = 90,
            Top = y - 3,
            Width = 600,
            Height = 50,
            CheckOnClick = true,
            SelectionMode = SelectionMode.One
        };

        y += 60;
        var lblShares = new Label { Text = "买入股数:", Left = 20, Top = y, Width = 70 };
        _numSharesPerPick = new NumericUpDown { Left = 90, Top = y - 3, Width = 100, Minimum = 100, Maximum = 10000, Value = 100, Increment = 100 };

        var lblCommission = new Label { Text = "手续费率:", Left = 210, Top = y, Width = 60 };
        _numPickCommission = new NumericUpDown { Left = 270, Top = y - 3, Width = 80, Minimum = 0, Maximum = 1, Value = (decimal)0.00025, DecimalPlaces = 5, Increment = (decimal)0.0001 };

        var lblSlippage = new Label { Text = "滑点:", Left = 370, Top = y, Width = 60 };
        _numPickSlippage = new NumericUpDown { Left = 430, Top = y - 3, Width = 80, Minimum = 0, Maximum = 1, Value = (decimal)0.001, DecimalPlaces = 4, Increment = (decimal)0.0001 };

        _btnRunPickBacktest = new Button { Text = "开始回测", Left = 540, Top = y - 3, Width = 100, Height = 28, BackColor = Color.LightGreen };
        _btnRunPickBacktest.Click += BtnRunPickBacktest_Click;

        _progressBarPick = new ProgressBar { Left = 660, Top = y, Width = 200, Height = 25 };

        settingsPanel.Controls.AddRange(new Control[] {
            lblStart, _dtpPickStart, lblEnd, _dtpPickEnd,
            lblStrategies, _lstPickStrategies,
            lblShares, _numSharesPerPick, lblCommission, _numPickCommission, lblSlippage, _numPickSlippage,
            _btnRunPickBacktest, _progressBarPick
        });

        // 结果面板
        var resultPanel = new Panel { Dock = DockStyle.Fill };

        // 上部：结果统计
        var statsPanel = new GroupBox { Text = "回测结果", Dock = DockStyle.Top, Height = 80 };
        _lblPickResult = new Label { Dock = DockStyle.Fill, Padding = new Padding(10), Font = new Font("Microsoft YaHei", 10) };
        statsPanel.Controls.Add(_lblPickResult);

        // 下部：交易记录
        var tradePanel = new GroupBox { Text = "交易记录", Dock = DockStyle.Fill };
        _dataGridViewPick = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false };
        _dataGridViewPick.Columns.Add("StockCode", "股票代码");
        _dataGridViewPick.Columns.Add("StockName", "股票名称");
        _dataGridViewPick.Columns.Add("StrategyName", "策略名称");
        _dataGridViewPick.Columns.Add("BuyDate", "买入日期");
        _dataGridViewPick.Columns.Add("BuyPrice", "买入价格");
        _dataGridViewPick.Columns.Add("Shares", "数量");
        _dataGridViewPick.Columns.Add("SellDate", "卖出日期");
        _dataGridViewPick.Columns.Add("SellPrice", "卖出价格");
        _dataGridViewPick.Columns.Add("ProfitLoss", "盈亏");
        _dataGridViewPick.Columns.Add("ProfitLossPercent", "盈亏%");
        _dataGridViewPick.Columns.Add("HoldingDays", "持仓天数");
        tradePanel.Controls.Add(_dataGridViewPick);

        // 添加控件
        resultPanel.Controls.Add(tradePanel);  // 底部：交易记录（Fill）
        resultPanel.Controls.Add(statsPanel);  // 上部：回测结果

        parent.Controls.AddRange(new Control[] { resultPanel, settingsPanel });
    }

    private async void LoadStrategies()
    {
        try
        {
            _strategies = await _strategyRepo.GetActiveAsync();
            _strategyIdMap.Clear();

            // 填充策略下拉框（用于策略回测）
            _cboStrategy.DisplayMember = "Name";
            _cboStrategy.ValueMember = "Id";
            _cboStrategy.DataSource = _strategies;

            // 填充策略多选框（用于组合策略回测）
            _lstPickStrategies.Items.Clear();
            foreach (var strategy in _strategies)
            {
                _lstPickStrategies.Items.Add(strategy.Name);
                _strategyIdMap[strategy.Name] = strategy.Id;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载策略失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void BtnRun_Click(object? sender, EventArgs e)
    {
        if (_cboStrategy.SelectedItem == null)
        {
            MessageBox.Show("请选择策略", "提示");
            return;
        }

        var strategy = (Strategy)_cboStrategy.SelectedItem;
        var strategyInstance = StrategyFactory.CreateFromJson(strategy.StrategyType, strategy.Parameters);
        if (strategyInstance == null)
        {
            MessageBox.Show("无法创建策略实例", "错误");
            return;
        }

        _btnRun.Enabled = false;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 50;

        try
        {
            var settings = new BacktestSettings
            {
                InitialCapital = _numCapital.Value,
                Commission = _numCommission.Value,
                Slippage = _numSlippage.Value
            };

            var progress = new Progress<BacktestProgress>(p =>
            {
                this.Invoke(() => {
                    // 只在回测进行中显示进度消息，完成后不覆盖结果
                    if (p.CurrentStep < p.TotalSteps)
                    {
                        _lblResult.Text = p.Message;
                    }
                });
            });

            var result = await _backtestEngine.RunAsync(
                strategyInstance, _dtpStart.Value, _dtpEnd.Value, settings, null, progress);

            DisplayResult(result);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"回测失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnRun.Enabled = true;
            _progressBar.Style = ProgressBarStyle.Blocks;
        }
    }

    private async void BtnRunPickBacktest_Click(object? sender, EventArgs e)
    {
        _btnRunPickBacktest.Enabled = false;
        _progressBarPick.Style = ProgressBarStyle.Marquee;
        _progressBarPick.MarqueeAnimationSpeed = 50;

        try
        {
            // 获取用户选择的策略ID列表
            var selectedStrategyIds = _lstPickStrategies.CheckedItems
                .Cast<string>()
                .Select(name => _strategyIdMap[name])
                .ToList();

            var settings = new DailyPickBacktestSettings
            {
                StartDate = _dtpPickStart.Value,
                EndDate = _dtpPickEnd.Value,
                SharesPerPick = (int)_numSharesPerPick.Value,
                Commission = _numPickCommission.Value,
                Slippage = _numPickSlippage.Value,
                StrategyIds = selectedStrategyIds.Count > 0 ? selectedStrategyIds : null
            };

            var progress = new Progress<string>(msg =>
            {
                this.Invoke(() => _lblPickResult.Text = msg);
            });

            var result = await _backtestEngine.RunDailyPickBacktestAsync(
                _dtpPickStart.Value, _dtpPickEnd.Value, settings, progress);

            DisplayPickBacktestResult(result);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"回测失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnRunPickBacktest.Enabled = true;
            _progressBarPick.Style = ProgressBarStyle.Blocks;
        }
    }

    private void DisplayResult(BacktestResult result)
    {
        // 显示统计结果
        _lblResult.Text = $"总收益率: {result.TotalReturn:F2}%  |  " +
                         $"年化收益: {result.AnnualReturn:F2}%  |  " +
                         $"最大回撤: {result.MaxDrawdown:F2}%  |  " +
                         $"夏普比率: {result.SharpeRatio:F2}  |  " +
                         $"胜率: {result.WinRate:F1}%  |  " +
                         $"交易次数: {result.TradeCount}";

        // 绘制资金曲线
        _equityChart.Plot.Clear();
        
        if (result.EquityCurve != null && result.EquityCurve.Count > 0)
        {
            var dates = result.EquityCurve.Select(e => e.Date.ToOADate()).ToArray();
            var equities = result.EquityCurve.Select(e => (double)e.Equity).ToArray();
            
            // 添加资金曲线（使用散点图）
            var scatter = _equityChart.Plot.Add.Scatter(dates, equities);
            scatter.Color = new ScottPlot.Color(0, 0, 255);  // 蓝色
            scatter.LineWidth = 2;
            
            _equityChart.Plot.XLabel("日期");
            _equityChart.Plot.YLabel("资金");
            _equityChart.Plot.Title("资金曲线");
        }
        else
        {
            _equityChart.Plot.Title("无资金曲线数据");
        }
        
        _equityChart.Refresh();

        // 显示交易记录
        _dataGridView.Rows.Clear();
        foreach (var trade in result.Trades)
        {
            _dataGridView.Rows.Add(
                trade.StockCode, trade.StockName, trade.StrategyName,
                trade.BuyDate.ToString("yyyy-MM-dd"), trade.BuyPrice.ToString("F2"),
                trade.Shares,
                trade.SellDate?.ToString("yyyy-MM-dd"), trade.SellPrice?.ToString("F2"),
                trade.ProfitLoss.ToString("F2"), trade.ProfitLossPercent.ToString("F2") + "%",
                trade.HoldingDays, trade.SellReason
            );
        }
    }

    private void DisplayPickBacktestResult(BacktestResult result)
    {
        // 显示统计结果
        _lblPickResult.Text = $"胜率: {result.WinRate:F1}%  |  " +
                              $"总交易次数: {result.TradeCount}  |  " +
                              $"盈利次数: {result.WinCount}  |  " +
                              $"亏损次数: {result.LossCount}  |  " +
                              $"总盈亏: {result.FinalEquity:F2}元  |  " +
                              $"平均每笔盈亏: {result.FinalEquity / result.TradeCount:F2}元";

        // 显示交易记录
        _dataGridViewPick.Rows.Clear();
        foreach (var trade in result.Trades)
        {
            _dataGridViewPick.Rows.Add(
                trade.StockCode, trade.StockName, trade.StrategyName,
                trade.BuyDate.ToString("yyyy-MM-dd"), trade.BuyPrice.ToString("F2"),
                trade.Shares,
                trade.SellDate?.ToString("yyyy-MM-dd"), trade.SellPrice?.ToString("F2"),
                trade.ProfitLoss.ToString("F2"), trade.ProfitLossPercent.ToString("F2") + "%",
                trade.HoldingDays
            );
        }
    }
}
