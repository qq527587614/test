using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core.DeepSeek;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.RealtimeData;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.UI.Forms;

    public partial class PlateAnalysisForm : Form
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IIndicatorRepository _indicatorRepository;
        private readonly DeepSeekClient _deepSeekClient;
        private TencentRealtimeService? _realtimeService;

    private DataGridView _dgvPlates = null!;
    private DataGridView _dgvStocks = null!;
    private TextBox _txtNewsInput = null!;
    private DataGridView _dgvMarketAnalysis = null!;
    private DateTimePicker _dtpDate = null!;
    private ToolStripLabel _lblStatus = null!;
    private SplitContainer _splitContainer = null!;
    private TabControl _tabControl = null!;

    // 板块统计数据模型
    private class PlateDataItem
    {
        public string PlateName { get; set; } = "";
        public int StockCount { get; set; }
        public int LimitUpCount { get; set; }  // 涨停数量
        public decimal AvgChange { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal AvgTurnover { get; set; }
        public List<StockDataItem> Stocks { get; set; } = new();
    }

    // 个股数据模型
    private class StockDataItem
    {
        public string StockCode { get; set; } = "";
        public string StockName { get; set; } = "";
        public decimal ClosePrice { get; set; }
        public decimal ChangePercent { get; set; }
        public decimal Amount { get; set; }
        public decimal? TurnoverRate { get; set; }
        public bool IsLimitUp { get; set; }  // 是否涨停
    }

    public PlateAnalysisForm(IServiceProvider serviceProvider, IIndicatorRepository indicatorRepository, DeepSeekClient deepSeekClient, TencentRealtimeService? realtimeService = null)
    {
        _serviceProvider = serviceProvider;
        _indicatorRepository = indicatorRepository;
        _deepSeekClient = deepSeekClient;
        _realtimeService = realtimeService;
        InitializeComponent();
        InitializeControls();
        LoadData();
    }

    private void InitializeComponent()
    {
        Text = "板块分析";
        Size = new Size(1400, 900);
        StartPosition = FormStartPosition.CenterParent;
    }

    private void InitializeControls()
    {
        // 顶部工具栏
        var toolStrip = new ToolStrip { Dock = DockStyle.Top };

        _dtpDate = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Value = DateTime.Today
        };
        _dtpDate.ValueChanged += (s, e) => LoadData();

        var btnRefresh = new ToolStripButton("刷新");
        btnRefresh.Click += (s, e) => LoadData();

        var btnSync = new ToolStripButton("同步涨停数据");
        btnSync.Click += async (s, e) => await SyncLimitUpDataAsync();

        _lblStatus = new ToolStripLabel("就绪");

        toolStrip.Items.Add(new ToolStripLabel("分析日期:"));
        toolStrip.Items.Add(new ToolStripControlHost(_dtpDate));
        toolStrip.Items.Add(new ToolStripButton("  "));
        toolStrip.Items.Add(btnRefresh);
        toolStrip.Items.Add(new ToolStripButton("  "));
        toolStrip.Items.Add(btnSync);
        toolStrip.Items.Add(new ToolStripLabel("  "));
        toolStrip.Items.Add(_lblStatus);

        Controls.Add(toolStrip);

        // TabControl
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill
        };

        // Tab 1: 板块分析
        var tabPage1 = new TabPage("板块分析");

        // 分割容器
        _splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 300,
            Panel1MinSize = 150
        };

        // 板块列表
        _dgvPlates = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            ColumnHeadersVisible = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        _dgvPlates.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

        _dgvPlates.Columns.Add(new DataGridViewTextBoxColumn { Name = "PlateName", HeaderText = "板块名称", Width = 150 });
        _dgvPlates.Columns.Add(new DataGridViewTextBoxColumn { Name = "StockCount", HeaderText = "家数", Width = 60 });
        _dgvPlates.Columns.Add(new DataGridViewTextBoxColumn { Name = "LimitUpCount", HeaderText = "涨停数", Width = 60 });
        _dgvPlates.Columns.Add(new DataGridViewTextBoxColumn { Name = "AvgChange", HeaderText = "平均涨幅(%)", Width = 90 });
        _dgvPlates.Columns.Add(new DataGridViewTextBoxColumn { Name = "TotalAmount", HeaderText = "总成交额(亿)", Width = 90 });
        _dgvPlates.Columns.Add(new DataGridViewTextBoxColumn { Name = "AvgTurnover", HeaderText = "平均换手(%)", Width = 90 });

        _dgvPlates.SelectionChanged += DgvPlates_SelectionChanged;

        _splitContainer.Panel1.Controls.Add(_dgvPlates);

        // 板块内个股列表
        _dgvStocks = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            ColumnHeadersVisible = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        _dgvStocks.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

        _dgvStocks.Columns.Add(new DataGridViewTextBoxColumn { Name = "StockCode", HeaderText = "代码", Width = 80 });
        _dgvStocks.Columns.Add(new DataGridViewTextBoxColumn { Name = "StockName", HeaderText = "名称", Width = 80 });
        _dgvStocks.Columns.Add(new DataGridViewTextBoxColumn { Name = "ClosePrice", HeaderText = "收盘价", Width = 80 });
        _dgvStocks.Columns.Add(new DataGridViewTextBoxColumn { Name = "ChangePercent", HeaderText = "涨幅(%)", Width = 80 });
        _dgvStocks.Columns.Add(new DataGridViewTextBoxColumn { Name = "TurnoverRate", HeaderText = "换手率(%)", Width = 80 });
        _dgvStocks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Amount", HeaderText = "成交额(亿)", Width = 100 });

        _splitContainer.Panel2.Controls.Add(_dgvStocks);
        tabPage1.Controls.Add(_splitContainer);

        // Tab 2: DeepSeek 市场分析
        var tabPage2 = new TabPage("DeepSeek 市场分析");

        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 200,
            Padding = new Padding(10)
        };

        var lblNews = new Label
        {
            Text = "市场消息:",
            Location = new Point(10, 10),
            AutoSize = true
        };

        _txtNewsInput = new TextBox
        {
            Location = new Point(10, 40),
            Size = new Size(600, 120),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };

        var btnAnalyze = new Button
        {
            Text = "分析",
            Location = new Point(630, 40),
            Size = new Size(100, 120),
            BackColor = Color.FromArgb(64, 169, 255),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnAnalyze.Click += async (s, e) => await AnalyzeMarketAsync();

        topPanel.Controls.AddRange(new Control[] { lblNews, _txtNewsInput, btnAnalyze });

        _dgvMarketAnalysis = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            ColumnHeadersVisible = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        _dgvMarketAnalysis.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

        _dgvMarketAnalysis.Columns.Add(new DataGridViewTextBoxColumn { Name = "PlateName", HeaderText = "板块名称", Width = 150 });
        _dgvMarketAnalysis.Columns.Add(new DataGridViewTextBoxColumn { Name = "Reason", HeaderText = "推荐理由", Width = 300 });
        _dgvMarketAnalysis.Columns.Add(new DataGridViewTextBoxColumn { Name = "Confidence", HeaderText = "信心度", Width = 100 });

        tabPage2.Controls.AddRange(new Control[] { topPanel, _dgvMarketAnalysis });

        _tabControl.TabPages.Add(tabPage1);
        _tabControl.TabPages.Add(tabPage2);

        Controls.Add(_tabControl);
    }

    private async void LoadData()
    {
        try
        {
            _lblStatus.Text = "加载中...";
            _dgvPlates.Rows.Clear();
            _dgvStocks.Rows.Clear();

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Core.AppDbContext>();

            var tradeDate = _dtpDate.Value.Date;
            ErrorLogger.Log(null, "PlateAnalysisForm.LoadData", $"查询日期: {tradeDate:yyyy-MM-dd}");

            // 查询涨停数据获取板块映射关系，然后查询所有股票日线数据计算板块涨幅
            var plateData = await Task.Run(() =>
            {
                // 1. 从涨停表获取板块映射（股票代码 -> 板块名称）
                // 使用 Date 属性比较，避免时间部分影响
                var limitUpStocks = dbContext.StockLimitUpAnalysis
                    .Select(s => new { s.code, s.plate_name })
                    .ToList();

                ErrorLogger.Log(null, "PlateAnalysisForm.LoadData", $"涨停表数据量: {limitUpStocks.Count}");

                // 建立股票代码到板块的映射字典（一个股票可能属于多个板块，用分号分隔）
                // 去除代码前2位字母后再存储
                var stockToPlate = limitUpStocks
                    .Where(s => !string.IsNullOrEmpty(s.code) && !string.IsNullOrEmpty(s.plate_name))
                    .Select(s => new { code = s.code.Length > 2 ? s.code.Substring(2) : s.code, plate_name = s.plate_name })
                    .GroupBy(s => s.code)
                    .ToDictionary(
                        g => g.Key,
                        g => string.Join(";", g.Select(x => x.plate_name).Distinct())
                    );

                if (stockToPlate.Count == 0)
                {
                    ErrorLogger.Log(null, "PlateAnalysisForm.LoadData", "涨停表中无数据");
                    return new List<PlateDataItem>();
                }

                ErrorLogger.Log(null, "PlateAnalysisForm.LoadData", $"板块映射数量: {stockToPlate.Count}");

                // 记录涨停股票代码集合（用于判断是否涨停）
                var limitUpStockCodes = new HashSet<string>(limitUpStocks.Select(s => 
                    s.code.Length > 2 ? s.code.Substring(2) : s.code));

                // 2. 查询当日所有股票的日线数据（包含涨跌幅）
                List<StockDataItem> dailyWithPlates;
                var isToday = tradeDate.Date == DateTime.Today;

                if (isToday && _realtimeService != null)
                {
                    // 当天：从实时服务获取数据
                    ErrorLogger.Log(null, "PlateAnalysisForm.LoadData", "使用实时数据");

                    var stockCodesInPlates = stockToPlate.Keys.ToList();
                    var stockInfoDict = dbContext.StockInfos
                        .Where(s => stockCodesInPlates.Contains(s.StockCode))
                        .ToDictionary(s => s.StockCode, s => s.StockName);

                    // 实时服务需要带前缀的股票代码（如 sz000001）
                    var stockCodesWithPrefix = stockCodesInPlates
                        .Select(code => code.StartsWith("6") ? "sh" + code : "sz" + code)
                        .ToList();

                    // 分批获取实时数据，每批最多100个
                    const int batchSize = 100;
                    var allRealtimeResults = new List<RealtimeStockData>();

                    for (int i = 0; i < stockCodesWithPrefix.Count; i += batchSize)
                    {
                        var batch = stockCodesWithPrefix.Skip(i).Take(batchSize).ToList();
                        var batchResults = _realtimeService.GetRealtimeDataAsync(batch).Result;
                        allRealtimeResults.AddRange(batchResults);
                    }

                    // 将带前缀的代码转回纯数字代码进行匹配
                    dailyWithPlates = allRealtimeResults
                        .Select(r => new 
                        {
                            StockCode = r.StockCode,
                            r.StockName,
                            r.CurrentPrice,
                            r.ChangePercent,
                            r.Amount,
                            r.TurnoverRate
                        })
                        .Where(r => stockCodesInPlates.Contains(r.StockCode))
                        .Select(r => new StockDataItem
                        {
                            StockCode = r.StockCode,
                            StockName = stockInfoDict.GetValueOrDefault(r.StockCode, ""),
                            ClosePrice = r.CurrentPrice,
                            ChangePercent = r.ChangePercent,
                            Amount = r.Amount,
                            TurnoverRate = r.TurnoverRate,
                            IsLimitUp = limitUpStockCodes.Contains(r.StockCode)
                        })
                        .ToList();
                }
                else
                {
                    // 非当天：从日线表获取数据
                    var dailyData = dbContext.StockDailyData
                        .Where(d => d.TradeDate.Date == tradeDate && d.ChangePercent.HasValue)
                        .ToList();

                    ErrorLogger.Log(null, "PlateAnalysisForm.LoadData", $"日线表数据量: {dailyData.Count}");

                    // 3. 查询股票基本信息（名称）
                    var stockCodesInPlates = stockToPlate.Keys.ToList();
                    var stockInfoDict = dbContext.StockInfos
                        .Where(s => stockCodesInPlates.Contains(s.StockCode))
                        .ToDictionary(s => s.StockCode, s => s.StockName);

                    // 4. 根据板块映射获取每个板块的股票数据
                    dailyWithPlates = dailyData
                        .Where(d => stockCodesInPlates.Contains(d.StockCode))
                        .Select(d => new StockDataItem
                        {
                            StockCode = d.StockCode,
                            StockName = stockInfoDict.GetValueOrDefault(d.StockCode, ""),
                            ClosePrice = d.ClosePrice,
                            ChangePercent = d.ChangePercent ?? 0,
                            Amount = d.Amount,
                            TurnoverRate = d.TurnoverRate,
                            IsLimitUp = limitUpStockCodes.Contains(d.StockCode)
                        })
                        .ToList();
                }

                ErrorLogger.Log(null, "PlateAnalysisForm.LoadData", $"匹配到日线数据的股票数: {dailyWithPlates.Count}");

                // 5. 按板块分组统计 - 使用日线数据的涨跌幅
                // 需要处理一个股票对应多个板块的情况（用分号分隔），将股票拆分到各板块
                var stocksWithPlates = dailyWithPlates
                    .SelectMany(d =>
                    {
                        var plates = stockToPlate.GetValueOrDefault(d.StockCode, "");
                        if (string.IsNullOrEmpty(plates)) return Enumerable.Empty<(string Plate, StockDataItem Stock)>();

                        return plates.Split(';', StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => (Plate: p.Trim(), Stock: d));
                    })
                    .ToList();

                var grouped = stocksWithPlates
                    .GroupBy(s => s.Plate)
                    .Select(g => new PlateDataItem
                    {
                        PlateName = g.Key,
                        StockCount = g.Count(),
                        LimitUpCount = g.Count(s => s.Stock.IsLimitUp),  // 计算涨停数量
                        AvgChange = g.Average(s => s.Stock.ChangePercent),
                        TotalAmount = g.Sum(s => s.Stock.Amount) / 10000,
                        AvgTurnover = g.Average(s => s.Stock.TurnoverRate ?? 0),
                        Stocks = g.Select(s => s.Stock).ToList()
                    })
                    .Where(p => p.StockCount >= 10) // 过滤股票数量少于10个的板块
                    .OrderByDescending(p => p.AvgChange)
                    .ToList();

                ErrorLogger.Log(null, "PlateAnalysisForm.LoadData", $"板块数量: {grouped.Count}");

                return grouped;
            });

            // 显示板块统计
            foreach (var plate in plateData)
            {
                var rowIndex = _dgvPlates.Rows.Add(
                    plate.PlateName,
                    plate.StockCount.ToString(),
                    plate.LimitUpCount.ToString(),
                    plate.AvgChange.ToString("F2"),
                    plate.TotalAmount.ToString("F2"),
                    plate.AvgTurnover.ToString("F2")
                );

                // 设置平均涨幅颜色
                var cell = _dgvPlates.Rows[rowIndex].Cells["AvgChange"];
                cell.Style.ForeColor = plate.AvgChange > 0 ? Color.Red : Color.Green;

                // 存储板块内的股票数据到Tag中
                _dgvPlates.Rows[rowIndex].Tag = plate.Stocks;
            }

            _lblStatus.Text = $"共 {plateData.Count} 个板块";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "加载失败";
            ErrorLogger.Log(ex, "PlateAnalysisForm.LoadData", $"日期: {_dtpDate.Value:yyyy-MM-dd}");
            MessageBox.Show($"加载数据失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DgvPlates_SelectionChanged(object? sender, EventArgs e)
    {
        _dgvStocks.Rows.Clear();

        if (_dgvPlates.SelectedRows.Count == 0) return;

        var selectedRow = _dgvPlates.SelectedRows[0];
        var stocks = selectedRow.Tag as List<StockDataItem>;

        if (stocks == null || stocks.Count == 0) return;

        // 按涨幅降序排列
        var sortedStocks = stocks.OrderByDescending(s => s.ChangePercent).ToList();

        foreach (var stock in sortedStocks)
        {
            var rowIndex = _dgvStocks.Rows.Add(
                stock.StockCode,
                stock.StockName,
                stock.ClosePrice.ToString("F2"),
                stock.ChangePercent.ToString("F2"),
                (stock.TurnoverRate ?? 0).ToString("F2"),
                (stock.Amount / 10000).ToString("F2")
            );

            // 设置涨幅颜色
            var cell = _dgvStocks.Rows[rowIndex].Cells["ChangePercent"];
            cell.Style.ForeColor = stock.ChangePercent > 0 ? Color.Red : Color.Green;
        }
    }

    /// <summary>
    /// 同步涨停数据
    /// </summary>
    private async Task SyncLimitUpDataAsync()
    {
        try
        {
            _lblStatus.Text = "同步中...";
            _lblStatus.ForeColor = Color.Blue;

            // 获取当前选择器的日期
            var selectedDate = _dtpDate.Value;
            var dateStr = selectedDate.ToString("yyyyMMdd");

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Core.AppDbContext>();

            var url = $"https://x-quote.cls.cn/v2/quote/a/plate/up_down_analysis?date={dateStr}";

            _lblStatus.Text = $"同步中... {dateStr}";

            var json = await GetHttpContentAsync(url);
            if (string.IsNullOrEmpty(json))
            {
                _lblStatus.Text = "同步失败";
                _lblStatus.ForeColor = Color.Red;
                ErrorLogger.Log(null, "PlateAnalysisForm.SyncLimitUpData", $"获取数据失败: {dateStr}");
                MessageBox.Show("获取数据失败，请检查网络连接", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var data = System.Text.Json.JsonSerializer.Deserialize<LimitUpApiResponse>(json);
            if (data?.data?.plate_stock == null || data.data.plate_stock.Length == 0)
            {
                _lblStatus.Text = "无数据";
                _lblStatus.ForeColor = Color.Orange;
                MessageBox.Show("该日期暂无涨停数据", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 删除当天数据重新插入
            var targetDate = DateTime.ParseExact(dateStr, "yyyyMMdd", null);
            var existingRecords = dbContext.StockLimitUpAnalysis
                .Where(s => s.analysis_date.Date == targetDate.Date)
                .ToList();
            dbContext.StockLimitUpAnalysis.RemoveRange(existingRecords);

            var stockList = new List<StockLimitUpAnalysis>();

            foreach (var plate in data.data.plate_stock)
            {
                if (plate.stock_list == null) continue;

                foreach (var stock in plate.stock_list)
                {
                    if (stock.time == "--") continue; // 过滤无效数据

                    var entity = new StockLimitUpAnalysis
                    {
                        code = stock.secu_code ?? "",
                        name = stock.secu_name ?? "",
                        close = stock.last_px,
                        pct_chg = stock.change,
                        turn = stock.cmc,
                        plate_code = plate.secu_code,
                        plate_name = plate.secu_name,
                        first_limit_up_time = stock.time,
                        last_limit_up_time = stock.time,
                        analysis_date = selectedDate,
                        created_time = DateTime.Now,
                        updated_time = DateTime.Now
                    };
                    stockList.Add(entity);
                }
            }

            if (stockList.Count > 0)
            {
                dbContext.StockLimitUpAnalysis.AddRange(stockList);
                await dbContext.SaveChangesAsync();
                ErrorLogger.Log(null, "PlateAnalysisForm.SyncLimitUpData", $"保存成功: {dateStr}, 数量: {stockList.Count}");
            }

            _lblStatus.Text = $"同步完成: {stockList.Count}条";
            _lblStatus.ForeColor = Color.Green;
            LoadData(); // 刷新数据

            MessageBox.Show($"涨停数据同步完成，共 {stockList.Count} 条", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "同步失败";
            _lblStatus.ForeColor = Color.Red;
            ErrorLogger.Log(ex, "PlateAnalysisForm.SyncLimitUpData", "");
            MessageBox.Show($"同步失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// HTTP GET请求
    /// </summary>
    private async Task<string> GetHttpContentAsync(string url)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            return await client.GetStringAsync(url);
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "PlateAnalysisForm.GetHttpContent", url);
            return "";
        }
    }

    // API响应模型
    private class LimitUpApiResponse
    {
        public LimitUpData? data { get; set; }
    }

    private class LimitUpData
    {
        public PlateData[]? plate_stock { get; set; }
    }

    private class PlateData
    {
        public string? secu_code { get; set; }
        public string? secu_name { get; set; }
        public decimal? change { get; set; }
        public string? up_reason { get; set; }
        public StockInfo[]? stock_list { get; set; }
    }

    private class StockInfo
    {
        public string? secu_code { get; set; }
        public string? secu_name { get; set; }
        public string? up_reason { get; set; }
        public decimal? change { get; set; }
        public string? time { get; set; }
        public decimal? cmc { get; set; }
        public decimal? last_px { get; set; }
    }

    /// <summary>
    /// DeepSeek 市场分析
    /// </summary>
    private async Task AnalyzeMarketAsync()
    {
        try
        {
            var newsContent = _txtNewsInput.Text.Trim();
            if (string.IsNullOrEmpty(newsContent))
            {
                MessageBox.Show("请输入市场消息", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _lblStatus.Text = "分析中...";
            _lblStatus.ForeColor = Color.Blue;

            var result = await _deepSeekClient.AnalyzeMarketAsync(newsContent);

            if (result == null)
            {
                MessageBox.Show("分析失败，请检查DeepSeek API配置", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _lblStatus.Text = "分析失败";
                _lblStatus.ForeColor = Color.Red;
                return;
            }

            _dgvMarketAnalysis.Rows.Clear();

            foreach (var plate in result.RecommendedPlates)
            {
                var rowIndex = _dgvMarketAnalysis.Rows.Add(
                    plate.PlateName,
                    plate.Reason,
                    plate.Confidence.ToString("P2")
                );

                var cell = _dgvMarketAnalysis.Rows[rowIndex].Cells["Confidence"];
                cell.Style.BackColor = plate.Confidence > 0.7m ? Color.LightGreen :
                                        plate.Confidence > 0.4m ? Color.LightYellow : Color.LightPink;
            }

            _lblStatus.Text = $"分析完成: 推荐了 {result.RecommendedPlates.Count} 个板块";
            _lblStatus.ForeColor = Color.Green;

            if (!string.IsNullOrEmpty(result.MarketTrend))
            {
                MessageBox.Show($"市场趋势: {result.MarketTrend}\n\n风险提示:\n{string.Join("\n", result.Risks)}",
                    "分析结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "分析失败";
            _lblStatus.ForeColor = Color.Red;
            ErrorLogger.Log(ex, "PlateAnalysisForm.AnalyzeMarket", "");
            MessageBox.Show($"分析失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
