using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core.DailyPick;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.Services;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.UI.Forms;

public partial class DailyPickForm : Form
{
    private readonly DailyPicker _picker;
    private readonly IDailyPickRepository _pickRepo;
    private readonly IServiceProvider _serviceProvider;
    private readonly StockFavoriteService _favoriteService;

    public DailyPickForm(DailyPicker picker, IDailyPickRepository pickRepo, IServiceProvider serviceProvider, StockFavoriteService favoriteService)
    {
        _picker = picker;
        _pickRepo = pickRepo;
        _serviceProvider = serviceProvider;
        _favoriteService = favoriteService;
        InitializeComponent();
        
        // 添加DataGridView列
        _dataGridView.Columns.Add("StockCode", "股票代码");
        _dataGridView.Columns.Add("StockName", "股票名称");
        _dataGridView.Columns.Add("Industry", "行业");
        _dataGridView.Columns.Add("StrategyName", "策略");
        _dataGridView.Columns.Add("Reason", "选股理由");
        _dataGridView.Columns.Add("DeepSeekScore", "DeepSeek评分");
        _dataGridView.Columns.Add("FinalScore", "最终得分");

        // 添加操作列（按钮）
        var btnColumn = new DataGridViewButtonColumn
        {
            Name = "ActionColumn",
            HeaderText = "操作",
            Text = "加入自选",
            UseColumnTextForButtonValue = true,
            Width = 80
        };
        _dataGridView.Columns.Add(btnColumn);
        _dataGridView.CellContentClick += DataGridView_CellContentClick;

        _dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        
        // 添加右键菜单
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("查看K线", null, (s, e) => MessageBox.Show("K线功能待实现"));
        contextMenu.Items.Add("加入自选", null, AddToFavorite);
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("导出选中", null, ExportSelected);
        _dataGridView.ContextMenuStrip = contextMenu;
        
        LoadData();
    }

    private async void LoadData()
    {
        try
        {
            var results = await _picker.GetHistoryAsync(_dtpDate.Value);
            DisplayResults(results);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载数据失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void BtnRefresh_Click(object? sender, EventArgs e)
    {
        _btnRefresh.Enabled = false;
        _btnRefresh.Text = "选股中...";
        _dataGridView.Rows.Clear();

        try
        {
            var progress = new Progress<string>(msg =>
            {
                this.Invoke(() => { _lblStats.Text = msg; });
            });

            var results = await _picker.PickAsync(_dtpDate.Value, null, _chkDeepSeek.Checked, progress);
            DisplayResults(results);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"选股失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnRefresh.Enabled = true;
            _btnRefresh.Text = "刷新选股";
        }
    }

    private void BtnAddFavorite_Click(object? sender, EventArgs e)
    {
        AddToFavorite(sender, e);
    }

    private void DisplayResults(List<DailyPickResult> results)
    {
        _dataGridView.Rows.Clear();
        foreach (var r in results)
        {
            _dataGridView.Rows.Add(
                r.StockCode, r.StockName, r.Industry,
                r.StrategyName, r.Reason,
                r.DeepSeekScore?.ToString("F1") ?? "-",
                r.FinalScore.ToString("F1")
            );
        }

        _lblStats.Text = $"共选出 {results.Count} 只股票, 平均得分: {(results.Count > 0 ? results.Average(r => r.FinalScore).ToString("F1") : "-")}";
    }

    private async void AddToFavorite(object? sender, EventArgs e)
    {
        if (_dataGridView.SelectedRows.Count == 0)
        {
            MessageBox.Show("请先选择要加入自选的股票", "提示");
            return;
        }

        var successCount = 0;
        var failCount = 0;
        var existedCount = 0;

        foreach (DataGridViewRow row in _dataGridView.SelectedRows)
        {
            var stockCode = row.Cells["StockCode"].Value?.ToString();
            if (string.IsNullOrEmpty(stockCode)) continue;

            try
            {
                var result = await _favoriteService.AddFavoriteAsync(stockCode);
                if (result == "添加成功")
                {
                    successCount++;
                }
                else if (result == "股票已在自选股中")
                {
                    existedCount++;
                }
                else
                {
                    failCount++;
                }
            }
            catch (Exception ex)
            {
                failCount++;
                var fullError = ex.Message;
                if (ex.InnerException != null)
                {
                    fullError += "\n内部错误: " + ex.InnerException.Message;
                }
                ErrorLogger.Log(ex, "DailyPickForm.AddToFavorite", $"股票代码: {stockCode}, 错误: {fullError}");
            }
        }

        var msg = "";
        if (successCount > 0) msg += $"成功添加 {successCount} 只股票到自选。";
        if (existedCount > 0) msg += (msg.Length > 0 ? " " : "") + $"{existedCount} 只股票已在自选中。";
        if (failCount > 0) msg += (msg.Length > 0 ? " " : "") + $"添加失败 {failCount} 只股票 (请查看日志)";

        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void DataGridView_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        // 检查是否点击了按钮列
        if (e.RowIndex < 0) return;
        if (e.ColumnIndex < 0) return;

        var column = _dataGridView.Columns[e.ColumnIndex];
        if (column is DataGridViewButtonColumn && column.Name == "ActionColumn")
        {
            var row = _dataGridView.Rows[e.RowIndex];
            var stockCode = row.Cells["StockCode"].Value?.ToString();
            var stockName = row.Cells["StockName"].Value?.ToString();

            if (string.IsNullOrEmpty(stockCode))
            {
                MessageBox.Show("股票代码无效", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                var result = await _favoriteService.AddFavoriteAsync(stockCode);
                if (result == "添加成功")
                {
                    MessageBox.Show($"已添加 {stockName}({stockCode}) 到自选", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (result == "股票已在自选股中")
                {
                    MessageBox.Show($"{stockName}({stockCode}) 已在自选股中", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(result, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                var errorMsg = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMsg += "\n内部错误: " + ex.InnerException.Message;
                }
                MessageBox.Show($"添加失败: {errorMsg}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void ExportSelected(object? sender, EventArgs e)
    {
        if (_dataGridView.SelectedRows.Count == 0)
        {
            MessageBox.Show("请先选择要导出的股票", "提示");
            return;
        }

        using var saveDialog = new SaveFileDialog
        {
            Filter = "CSV文件|*.csv",
            FileName = $"选股结果_{_dtpDate.Value:yyyyMMdd}.csv"
        };

        if (saveDialog.ShowDialog() == DialogResult.OK)
        {
            using var writer = new StreamWriter(saveDialog.FileName);
            writer.WriteLine("股票代码,股票名称,行业,策略,理由,DeepSeek评分,最终得分");
            foreach (DataGridViewRow row in _dataGridView.SelectedRows)
            {
                writer.WriteLine($"\"{row.Cells[0].Value}\",\"{row.Cells[1].Value}\",\"{row.Cells[2].Value}\"," +
                               $"\"{row.Cells[3].Value}\",\"{row.Cells[4].Value}\"," +
                               $"\"{row.Cells[5].Value}\",\"{row.Cells[6].Value}\"");
            }
            MessageBox.Show($"导出成功: {saveDialog.FileName}", "提示");
        }
    }
}
