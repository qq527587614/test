using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core.DailyPick;
using StockAnalysisSystem.Core.Repositories;

namespace StockAnalysisSystem.UI.Forms;

public partial class DailyPickForm : Form
{
    private readonly DailyPicker _picker;
    private readonly IDailyPickRepository _pickRepo;
    private readonly IServiceProvider _serviceProvider;

    public DailyPickForm(DailyPicker picker, IDailyPickRepository pickRepo, IServiceProvider serviceProvider)
    {
        _picker = picker;
        _pickRepo = pickRepo;
        _serviceProvider = serviceProvider;
        InitializeComponent();
        
        // 添加DataGridView列
        _dataGridView.Columns.Add("StockCode", "股票代码");
        _dataGridView.Columns.Add("StockName", "股票名称");
        _dataGridView.Columns.Add("Industry", "行业");
        _dataGridView.Columns.Add("StrategyName", "策略");
        _dataGridView.Columns.Add("Reason", "选股理由");
        _dataGridView.Columns.Add("DeepSeekScore", "DeepSeek评分");
        _dataGridView.Columns.Add("FinalScore", "最终得分");
        _dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        
        // 添加右键菜单
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("查看K线", null, (s, e) => MessageBox.Show("K线功能待实现"));
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
