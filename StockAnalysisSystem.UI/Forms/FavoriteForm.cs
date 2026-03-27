using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.RealtimeData;
using StockAnalysisSystem.Core.Services;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.UI.Forms;

public partial class FavoriteForm : Form
{
    private readonly IServiceProvider _serviceProvider;
    private readonly StockFavoriteService _favoriteService;
    private readonly TencentRealtimeService _realtimeService;
    private System.Windows.Forms.Timer _refreshTimer = null!;
    private List<StockFavorite> _cachedFavorites = new();

    private DataGridView _dgvFavorites = null!;
    private ToolStrip _toolStrip = null!;
    private ToolStripButton _btnAdd = null!;
    private ToolStripButton _btnRemove = null!;
    private ToolStripButton _btnRefresh = null!;
    private ToolStripLabel _lblStatus = null!;
    private Label _lblNoData = null!;

    public FavoriteForm(IServiceProvider serviceProvider, StockFavoriteService favoriteService, TencentRealtimeService realtimeService)
    {
        _serviceProvider = serviceProvider;
        _favoriteService = favoriteService;
        _realtimeService = realtimeService;
        InitializeComponent();
        InitializeControls();
        LoadDataAsync();
        StartAutoRefresh();
    }

    private void InitializeComponent()
    {
        Text = "自选股";
        Size = new Size(900, 600);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
    }

    private void InitializeControls()
    {
        // 工具栏
        _toolStrip = new ToolStrip { Dock = DockStyle.Top };

        _btnAdd = new ToolStripButton("添加自选");
        _btnAdd.Click += BtnAdd_Click;
        _toolStrip.Items.Add(_btnAdd);

        _btnRemove = new ToolStripButton("删除自选");
        _btnRemove.Click += BtnRemove_Click;
        _toolStrip.Items.Add(_btnRemove);

        _toolStrip.Items.Add(new ToolStripSeparator());

        _btnRefresh = new ToolStripButton("刷新行情");
        _btnRefresh.Click += BtnRefresh_Click;
        _toolStrip.Items.Add(_btnRefresh);

        _lblStatus = new ToolStripLabel("就绪");
        _toolStrip.Items.Add(new ToolStripLabel(" "));
        _toolStrip.Items.Add(_lblStatus);

        // 数据表格
        _dgvFavorites = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false
        };

        _dgvFavorites.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "StockCode",
            HeaderText = "股票代码",
            Width = 80
        });

        _dgvFavorites.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "StockName",
            HeaderText = "股票名称",
            Width = 100
        });

        _dgvFavorites.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "CurrentPrice",
            HeaderText = "当前价格",
            Width = 90
        });

        _dgvFavorites.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ChangePercent",
            HeaderText = "涨跌幅(%)",
            Width = 90
        });

        _dgvFavorites.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "TurnoverRate",
            HeaderText = "换手率(%)",
            Width = 80
        });

        // 操作列 - 实时行情按钮
        var btnColumn = new DataGridViewButtonColumn
        {
            Name = "Action",
            HeaderText = "操作",
            Text = "分时图",
            UseColumnTextForButtonValue = true,
            Width = 70
        };
        _dgvFavorites.Columns.Add(btnColumn);

        _dgvFavorites.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "AddedDate",
            HeaderText = "添加日期",
            Width = 100
        });

        _dgvFavorites.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Remark",
            HeaderText = "备注",
            Width = 100
        });

        // 绑定单元格点击事件
        _dgvFavorites.CellContentClick += DgvFavorites_CellContentClick;

        // 绑定双击事件 - 打开K线图
        _dgvFavorites.CellDoubleClick += DgvFavorites_CellDoubleClick;

        // 无数据提示
        _lblNoData = new Label
        {
            Text = "暂无自选股，请点击\"添加自选\"按钮添加",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gray,
            Visible = false
        };

        Controls.Add(_dgvFavorites);
        Controls.Add(_lblNoData);
        Controls.Add(_toolStrip);
    }

    private void DgvFavorites_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.ColumnIndex < 0 || e.RowIndex < 0) return;

        var column = _dgvFavorites.Columns[e.ColumnIndex];
        if (column is DataGridViewButtonColumn && column.Name == "Action")
        {
            var row = _dgvFavorites.Rows[e.RowIndex];
            var stockCode = row.Cells["StockCode"].Value?.ToString();
            var stockName = row.Cells["StockName"].Value?.ToString() ?? "";
            var currentPrice = row.Cells["CurrentPrice"].Value?.ToString();
            var changePercent = row.Cells["ChangePercent"].Value?.ToString();

            if (string.IsNullOrEmpty(stockCode)) return;

            decimal price = 0;
            decimal change = 0;
            decimal.TryParse(currentPrice, out price);
            decimal.TryParse(changePercent, out change);

            // 打开分时图窗口
            using var chartForm = new MinuteChartForm(stockCode, stockName, price, change);
            chartForm.ShowDialog(this);
        }
    }

    private void DgvFavorites_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;

        var row = _dgvFavorites.Rows[e.RowIndex];
        var stockCode = row.Cells["StockCode"].Value?.ToString();

        if (string.IsNullOrEmpty(stockCode)) return;

        // 打开K线图窗口
        using var scope = _serviceProvider.CreateScope();
        var kLineService = scope.ServiceProvider.GetRequiredService<Core.Services.IKLineDataService>();
        using var kLineForm = new KLineForm(_serviceProvider, kLineService, stockCode);
        kLineForm.ShowDialog(this);
    }

    private async void LoadDataAsync()
    {
        try
        {
            _lblStatus.Text = "加载中...";
            _dgvFavorites.DataSource = null;
            _dgvFavorites.Rows.Clear();

            var favorites = await _favoriteService.GetFavoritesWithRealtimeDataAsync();

            if (favorites.Count == 0)
            {
                _dgvFavorites.Visible = false;
                _lblNoData.Visible = true;
                _lblStatus.Text = "共 0 只自选股";
                return;
            }

            _dgvFavorites.Visible = true;
            _lblNoData.Visible = false;

            // 缓存自选股列表，用于后续刷新
            _cachedFavorites = favorites;

            foreach (var f in favorites)
            {
                var changePercent = f.ChangePercent?.ToString("F2") ?? "-";
                var turnoverRate = f.TurnoverRate?.ToString("F2") ?? "-";
                var currentPrice = f.CurrentPrice?.ToString("F2") ?? "-";

                var rowIndex = _dgvFavorites.Rows.Add();

                // 按列名显式设置每个单元格的值
                _dgvFavorites.Rows[rowIndex].Cells["StockCode"].Value = f.StockCode;
                _dgvFavorites.Rows[rowIndex].Cells["StockName"].Value = f.StockName ?? "-";
                _dgvFavorites.Rows[rowIndex].Cells["CurrentPrice"].Value = currentPrice;
                _dgvFavorites.Rows[rowIndex].Cells["ChangePercent"].Value = changePercent;
                _dgvFavorites.Rows[rowIndex].Cells["TurnoverRate"].Value = turnoverRate;
                _dgvFavorites.Rows[rowIndex].Cells["AddedDate"].Value = f.AddedDate.ToString("yyyy-MM-dd");
                _dgvFavorites.Rows[rowIndex].Cells["Remark"].Value = f.Remark ?? "";

                // 设置涨跌幅颜色
                if (f.ChangePercent.HasValue)
                {
                    var cell = _dgvFavorites.Rows[rowIndex].Cells["ChangePercent"];
                    if (f.ChangePercent > 0)
                        cell.Style.ForeColor = Color.Red;
                    else if (f.ChangePercent < 0)
                        cell.Style.ForeColor = Color.Green;
                }
            }

            _lblStatus.Text = $"共 {favorites.Count} 只自选股";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "加载失败";
            ErrorLogger.Log(ex, "FavoriteForm.LoadDataAsync", "加载自选股数据失败");
            MessageBox.Show($"加载数据失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void BtnAdd_Click(object? sender, EventArgs e)
    {
        using var dialog = new Form
        {
            Text = "添加自选股",
            Size = new Size(400, 180),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var lblCode = new Label { Text = "股票代码:", Left = 20, Top = 25, Width = 80 };
        var txtCode = new TextBox { Left = 100, Top = 22, Width = 250, PlaceholderText = "如: 002229 或 sz002229" };

        var lblRemark = new Label { Text = "备注:", Left = 20, Top = 60, Width = 80 };
        var txtRemark = new TextBox { Left = 100, Top = 57, Width = 250 };

        var btnOK = new Button { Text = "添加", Left = 200, Top = 100, Width = 80, Height = 30 };
        var btnCancel = new Button { Text = "取消", Left = 290, Top = 100, Width = 80, Height = 30 };

        btnOK.Click += async (s, args) =>
        {
            var code = txtCode.Text.Trim();
            if (string.IsNullOrEmpty(code))
            {
                MessageBox.Show("请输入股票代码", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnOK.Enabled = false;
            btnOK.Text = "添加中...";

            try
            {
                var result = await _favoriteService.AddFavoriteAsync(code, txtRemark.Text.Trim());
                if (result == "添加成功")
                {
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                    LoadDataAsync();
                }
                else
                {
                    MessageBox.Show(result, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnOK.Enabled = true;
                    btnOK.Text = "添加";
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.Log(ex, "FavoriteForm.BtnAdd_Click", $"添加股票失败: {txtCode.Text}");
                MessageBox.Show($"添加失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnOK.Enabled = true;
                btnOK.Text = "添加";
            }
        };

        btnCancel.Click += (s, args) =>
        {
            dialog.DialogResult = DialogResult.Cancel;
            dialog.Close();
        };

        dialog.Controls.AddRange(new Control[] { lblCode, txtCode, lblRemark, txtRemark, btnOK, btnCancel });
        dialog.ShowDialog(this);
    }

    private async void BtnRemove_Click(object? sender, EventArgs e)
    {
        if (_dgvFavorites.SelectedRows.Count == 0)
        {
            MessageBox.Show("请先选择要删除的自选股", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var stockCode = _dgvFavorites.SelectedRows[0].Cells["StockCode"].Value?.ToString();
        if (string.IsNullOrEmpty(stockCode))
            return;

        var result = MessageBox.Show($"确定要删除自选股 {stockCode} 吗？", "确认删除",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            try
            {
                await _favoriteService.RemoveFavoriteAsync(stockCode);
                LoadDataAsync();
            }
            catch (Exception ex)
            {
                ErrorLogger.Log(ex, "FavoriteForm.BtnRemove_Click", $"删除股票失败: {stockCode}");
                MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private async void BtnRefresh_Click(object? sender, EventArgs e)
    {
        LoadDataAsync();
    }

    private void StartAutoRefresh()
    {
        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 5000 // 5秒
        };
        _refreshTimer.Tick += async (s, e) => await RefreshPricesAsync();
        _refreshTimer.Start();
    }

    private async Task RefreshPricesAsync()
    {
        if (_cachedFavorites.Count == 0) return;

        try
        {
            // 构建股票代码列表（带前缀）
            var stockCodes = new List<string>();
            foreach (var f in _cachedFavorites)
            {
                var code6 = f.StockCode.PadLeft(6, '0');
                var prefix = code6.StartsWith("60") || code6.StartsWith("68") ? "sh" : "sz";
                stockCodes.Add($"{prefix}{code6}");
            }

            // 批量获取实时数据
            var realtimeData = await _realtimeService.GetRealtimeDataAsync(stockCodes);

            // 创建实时数据映射
            var realtimeMap = new Dictionary<string, RealtimeStockData>();
            foreach (var rd in realtimeData)
            {
                var code = rd.StockCode;
                if (code.StartsWith("sz") || code.StartsWith("sh"))
                {
                    code = code.Substring(2);
                }
                realtimeMap[code] = rd;
            }

            // 更新DataGridView中的价格和涨跌幅
            foreach (DataGridViewRow row in _dgvFavorites.Rows)
            {
                var stockCode = row.Cells["StockCode"].Value?.ToString() ?? "";
                if (realtimeMap.TryGetValue(stockCode, out var rd))
                {
                    row.Cells["CurrentPrice"].Value = rd.CurrentPrice != 0 ? rd.CurrentPrice.ToString("F2") : "-";
                    row.Cells["ChangePercent"].Value = rd.ChangePercent != 0 ? rd.ChangePercent.ToString("F2") : "0.00";
                    row.Cells["TurnoverRate"].Value = rd.TurnoverRate != 0 ? rd.TurnoverRate.ToString("F2") : "-";

                    // 设置涨跌幅颜色
                    var cell = row.Cells["ChangePercent"];
                    cell.Style.ForeColor = rd.ChangePercent > 0 ? Color.Red : (rd.ChangePercent < 0 ? Color.Lime : Color.White);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "FavoriteForm.RefreshPricesAsync", "刷新行情失败");
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        base.OnFormClosed(e);
    }
}
