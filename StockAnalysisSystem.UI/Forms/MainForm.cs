using Microsoft.Extensions.DependencyInjection;

namespace StockAnalysisSystem.UI.Forms;

public partial class MainForm : Form
{
    private readonly IServiceProvider _serviceProvider;
    private readonly StatusStrip _statusStrip;
    private readonly ToolStripStatusLabel _statusLabel;
    private readonly ToolStripStatusLabel _dateLabel;
    private readonly Panel _mainPanel;

    public MainForm(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        InitializeComponent();

        // 初始化控件
        _statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel { Spring = true, Text = "就绪" };
        _dateLabel = new ToolStripStatusLabel { Text = $"数据日期: {DateTime.Today:yyyy-MM-dd}" };
        _statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel, _dateLabel });

        _mainPanel = new Panel { Dock = DockStyle.Fill };

        Controls.AddRange(new Control[] { _mainPanel, _statusStrip });

        Text = "股票分析系统";
        MinimumSize = new Size(1200, 800);

        CreateMenu();
        CreateToolbar();
    }

    private void CreateMenu()
    {
        var menuStrip = new MenuStrip();

        // 文件菜单
        var fileMenu = new ToolStripMenuItem("文件(&F)");
        fileMenu.DropDownItems.Add("退出(&X)", null, (s, e) => Close());
        menuStrip.Items.Add(fileMenu);

        // 策略管理菜单
        var strategyMenu = new ToolStripMenuItem("策略管理(&S)");
        strategyMenu.DropDownItems.Add("策略列表(&L)", null, ShowStrategyManager);
        strategyMenu.DropDownItems.Add("新建策略(&N)", null, ShowStrategyManager);
        menuStrip.Items.Add(strategyMenu);

        // 回测菜单
        var backtestMenu = new ToolStripMenuItem("回测(&B)");
        backtestMenu.DropDownItems.Add("新建回测(&N)", null, ShowBacktestForm);
        backtestMenu.DropDownItems.Add("历史记录(&H)", null, ShowBacktestForm);
        menuStrip.Items.Add(backtestMenu);

        // 优化菜单
        var optimizeMenu = new ToolStripMenuItem("优化(&O)");
        optimizeMenu.DropDownItems.Add("参数优化(&P)", null, ShowOptimizationForm);
        optimizeMenu.DropDownItems.Add("优化历史(&H)", null, ShowOptimizationForm);
        menuStrip.Items.Add(optimizeMenu);

        // 选股菜单
        var pickMenu = new ToolStripMenuItem("选股(&P)");
        pickMenu.DropDownItems.Add("每日选股(&D)", null, ShowDailyPickForm);
        pickMenu.DropDownItems.Add("选股历史(&H)", null, ShowPickHistoryForm);
        pickMenu.DropDownItems.Add(new ToolStripSeparator());
        pickMenu.DropDownItems.Add("板块分析(&B)", null, ShowPlateAnalysisForm);
        menuStrip.Items.Add(pickMenu);

        // K线图菜单
        var klineMenu = new ToolStripMenuItem("K线图(&K)");
        klineMenu.DropDownItems.Add("查看K线(&V)", null, ShowKLineForm);
        menuStrip.Items.Add(klineMenu);

        // 自选股菜单
        var favoriteMenu = new ToolStripMenuItem("自选股(&F)");
        favoriteMenu.DropDownItems.Add("我的自选(&M)", null, ShowFavoriteForm);
        menuStrip.Items.Add(favoriteMenu);

        // 数据菜单
        var dataMenu = new ToolStripMenuItem("数据(&D)");
        dataMenu.DropDownItems.Add("数据管理(&M)", null, ShowDataManagerForm);
        dataMenu.DropDownItems.Add("指标预计算(&C)", null, ShowDataManagerForm);
        menuStrip.Items.Add(dataMenu);

        // 帮助菜单
        var helpMenu = new ToolStripMenuItem("帮助(&H)");
        helpMenu.DropDownItems.Add("关于(&A)", null, (s, e) => 
            MessageBox.Show("股票分析系统 v1.0\n\n基于技术指标的智能选股与回测系统", "关于"));
        menuStrip.Items.Add(helpMenu);

        Controls.Add(menuStrip);
    }

    private void CreateToolbar()
    {
        var toolStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };

        toolStrip.Items.Add(new ToolStripButton("快速选股", null, QuickPick) { ToolTipText = "执行今日选股" });
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(new ToolStripButton("回测", null, ShowBacktestForm) { ToolTipText = "打开回测窗口" });
        toolStrip.Items.Add(new ToolStripButton("优化", null, ShowOptimizationForm) { ToolTipText = "打开优化窗口" });
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(new ToolStripButton("数据管理", null, ShowDataManagerForm) { ToolTipText = "打开数据管理窗口" });

        Controls.Add(toolStrip);
    }

    private void ShowInMainPanel(Form form)
    {
        _mainPanel.Controls.Clear();
        form.TopLevel = false;
        form.FormBorderStyle = FormBorderStyle.None;
        form.Dock = DockStyle.Fill;
        _mainPanel.Controls.Add(form);
        form.Show();
    }

    private void ShowStrategyManager(object? sender, EventArgs e)
    {
        // 注意：嵌入主面板的Form不使用using scope，因为Form需要持续存在
        // Form的生命周期由主面板控制，切换页面时会被清理
        var form = _serviceProvider.GetRequiredService<StrategyManagerForm>();
        ShowInMainPanel(form);
    }

    private void ShowBacktestForm(object? sender, EventArgs e)
    {
        var form = _serviceProvider.GetRequiredService<BacktestForm>();
        ShowInMainPanel(form);
    }

    private void ShowOptimizationForm(object? sender, EventArgs e)
    {
        var form = _serviceProvider.GetRequiredService<OptimizationForm>();
        ShowInMainPanel(form);
    }

    private void ShowDailyPickForm(object? sender, EventArgs e)
    {
        var form = _serviceProvider.GetRequiredService<DailyPickForm>();
        form.SetMode(false);  // 每日选股模式：可以刷新选股
        ShowInMainPanel(form);
    }

    private void ShowPickHistoryForm(object? sender, EventArgs e)
    {
        var form = _serviceProvider.GetRequiredService<DailyPickForm>();
        form.SetMode(true);   // 选股历史模式：只查询历史数据
        ShowInMainPanel(form);
    }

    private void ShowDataManagerForm(object? sender, EventArgs e)
    {
        var form = _serviceProvider.GetRequiredService<DataManagerForm>();
        ShowInMainPanel(form);
    }

    private void ShowFavoriteForm(object? sender, EventArgs e)
    {
        var form = _serviceProvider.GetRequiredService<FavoriteForm>();
        ShowInMainPanel(form);
    }

    private void ShowPlateAnalysisForm(object? sender, EventArgs e)
    {
        var form = _serviceProvider.GetRequiredService<PlateAnalysisForm>();
        ShowInMainPanel(form);
    }

    private void ShowKLineForm(object? sender, EventArgs e)
    {
        var stockCode = InputBox.Show("请输入股票代码", "股票代码", "sh600000");
        if (string.IsNullOrWhiteSpace(stockCode))
        {
            return;
        }

        // 验证股票代码格式
        stockCode = stockCode.Trim().ToLower();
        //if (!stockCode.StartsWith("sh") && !stockCode.StartsWith("sz"))
        //{
        //    MessageBox.Show("股票代码格式错误，请使用 sh 或 sz 开头，如 sh600000 或 sz000001",
        //        "格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        //    return;
        //}

        using var scope = _serviceProvider.CreateScope();
        var kLineService = scope.ServiceProvider.GetRequiredService<Core.Services.IKLineDataService>();
        var form = new KLineForm(_serviceProvider, kLineService, stockCode);
        form.Show();
    }

    private async void QuickPick(object? sender, EventArgs e)
    {
        _statusLabel.Text = "正在执行选股...";
        
        try
        {
            // QuickPick是短期操作，可以使用scope
            using var scope = _serviceProvider.CreateScope();
            var picker = scope.ServiceProvider.GetRequiredService<Core.DailyPick.DailyPicker>();
            
            var progress = new Progress<string>(msg => _statusLabel.Text = msg);
            var results = await picker.PickAsync(DateTime.Today, null, false, progress);
            
            MessageBox.Show($"选股完成，共选出 {results.Count} 只股票", "选股结果");
            ShowDailyPickForm(sender, e);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"选股失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        
        _statusLabel.Text = "就绪";
    }

    public void UpdateStatus(string message)
    {
        _statusLabel.Text = message;
    }
}
