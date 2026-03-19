using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core.Backtest;
using StockAnalysisSystem.Core.Optimization;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.Strategies;

namespace StockAnalysisSystem.UI.Forms;

public partial class OptimizationForm : Form
{
    private readonly ParameterOptimizer _optimizer;
    private readonly IStrategyRepository _strategyRepo;
    private readonly IServiceProvider _serviceProvider;

    private ComboBox _cboStrategyType = null!;
    private DateTimePicker _dtpStart = null!;
    private DateTimePicker _dtpEnd = null!;
    private ComboBox _cboFitness = null!;
    private NumericUpDown _numIterations = null!;
    private Button _btnRun = null!;
    private ProgressBar _progressBar = null!;
    private DataGridView _dataGridView = null!;
    private Label _lblBestResult = null!;

    public OptimizationForm(ParameterOptimizer optimizer, IStrategyRepository strategyRepo, IServiceProvider serviceProvider)
    {
        _optimizer = optimizer;
        _strategyRepo = strategyRepo;
        _serviceProvider = serviceProvider;
        InitializeComponent();
        InitializeControls();
    }

    private void InitializeControls()
    {
        Size = new Size(1000, 700);

        // 参数设置面板
        var settingsPanel = new GroupBox { Text = "优化参数", Dock = DockStyle.Top, Height = 150, Padding = new Padding(10) };
        
        int y = 25;
        var lblType = new Label { Text = "策略类型:", Left = 20, Top = y, Width = 70 };
        _cboStrategyType = new ComboBox { Left = 100, Top = y - 3, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        _cboStrategyType.Items.AddRange(StrategyFactory.GetSupportedTypes().ToArray());
        _cboStrategyType.SelectedIndex = 0;

        var lblFitness = new Label { Text = "适应度函数:", Left = 320, Top = y, Width = 70 };
        _cboFitness = new ComboBox { Left = 400, Top = y - 3, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        _cboFitness.Items.AddRange(new object[] { "年化收益率", "夏普比率", "卡玛比率", "综合评分" });
        _cboFitness.SelectedIndex = 0;

        y += 35;
        var lblStart = new Label { Text = "开始日期:", Left = 20, Top = y, Width = 70 };
        _dtpStart = new DateTimePicker { Left = 100, Top = y - 3, Width = 150, Value = DateTime.Today.AddYears(-2) };

        var lblEnd = new Label { Text = "结束日期:", Left = 270, Top = y, Width = 70 };
        _dtpEnd = new DateTimePicker { Left = 350, Top = y - 3, Width = 150, Value = DateTime.Today };

        y += 35;
        var lblIterations = new Label { Text = "迭代次数:", Left = 20, Top = y, Width = 70 };
        _numIterations = new NumericUpDown { Left = 100, Top = y - 3, Width = 100, Minimum = 10, Maximum = 10000, Value = 100 };

        _btnRun = new Button { Text = "开始优化", Left = 250, Top = y - 3, Width = 100, Height = 28, BackColor = Color.LightGreen };
        _btnRun.Click += BtnRun_Click;

        _progressBar = new ProgressBar { Left = 370, Top = y, Width = 200, Height = 25 };

        _lblBestResult = new Label { Left = 20, Top = y + 40, Width = 600, Font = new Font("Microsoft YaHei", 9) };

        settingsPanel.Controls.AddRange(new Control[] {
            lblType, _cboStrategyType, lblFitness, _cboFitness,
            lblStart, _dtpStart, lblEnd, _dtpEnd,
            lblIterations, _numIterations, _btnRun, _progressBar, _lblBestResult
        });

        // 结果面板
        var resultPanel = new GroupBox { Text = "优化结果", Dock = DockStyle.Fill };
        _dataGridView = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false };
        _dataGridView.Columns.Add("Iteration", "迭代");
        _dataGridView.Columns.Add("Parameters", "参数");
        _dataGridView.Columns.Add("Fitness", "适应度");
        _dataGridView.Columns.Add("Return", "收益率%");
        _dataGridView.Columns.Add("Drawdown", "最大回撤%");
        _dataGridView.Columns.Add("Sharpe", "夏普比率");
        _dataGridView.Columns.Add("WinRate", "胜率%");
        resultPanel.Controls.Add(_dataGridView);

        Controls.AddRange(new Control[] { resultPanel, settingsPanel });

        Text = "参数优化";
    }

    private async void BtnRun_Click(object? sender, EventArgs e)
    {
        var strategyType = _cboStrategyType.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(strategyType))
        {
            MessageBox.Show("请选择策略类型", "提示");
            return;
        }

        _btnRun.Enabled = false;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _dataGridView.Rows.Clear();

        try
        {
            // 获取默认参数范围
            var defaultParams = StrategyFactory.GetDefaultParameters(strategyType);
            var parameterRanges = new Dictionary<string, ParameterRange>();

            foreach (var kvp in defaultParams)
            {
                var val = Convert.ToInt32(kvp.Value);
                parameterRanges[kvp.Key] = new ParameterRange
                {
                    Min = Math.Max(1, val - 10),
                    Max = val + 10,
                    Step = 1
                };
            }

            var fitness = _cboFitness.SelectedIndex switch
            {
                0 => FitnessFunction.AnnualReturn,
                1 => FitnessFunction.SharpeRatio,
                2 => FitnessFunction.CalmarRatio,
                _ => FitnessFunction.Composite
            };

            var progress = new Progress<OptimizationProgress>(p =>
            {
                this.Invoke(() =>
                {
                    _lblBestResult.Text = $"当前迭代: {p.CurrentIteration}/{p.TotalIterations}, 最优适应度: {p.BestFitness:F4}";
                });
            });

            var result = await _optimizer.RandomSearchAsync(
                strategyType, parameterRanges,
                _dtpStart.Value, _dtpEnd.Value,
                (int)_numIterations.Value, fitness, null, progress);

            // 显示结果
            foreach (var record in result.Iterations.OrderByDescending(r => r.Fitness))
            {
                _dataGridView.Rows.Add(
                    record.Iteration,
                    string.Join(", ", record.Parameters.Select(p => $"{p.Key}={p.Value}")),
                    record.Fitness.ToString("F4"),
                    record.TotalReturn.ToString("F2"),
                    record.MaxDrawdown.ToString("F2"),
                    record.SharpeRatio.ToString("F2"),
                    record.WinRate.ToString("F1")
                );
            }

            if (result.BestBacktestResult != null)
            {
                _lblBestResult.Text = $"最优参数: {string.Join(", ", result.BestParameters.Select(p => $"{p.Key}={p.Value}"))}\n" +
                                     $"年化收益: {result.BestBacktestResult.AnnualReturn:F2}%, " +
                                     $"最大回撤: {result.BestBacktestResult.MaxDrawdown:F2}%, " +
                                     $"夏普比率: {result.BestBacktestResult.SharpeRatio:F2}";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"优化失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnRun.Enabled = true;
            _progressBar.Style = ProgressBarStyle.Blocks;
        }
    }
}
