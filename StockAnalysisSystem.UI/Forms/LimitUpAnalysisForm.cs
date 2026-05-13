using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core.Services;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.UI.Forms;

/// <summary>
/// 涨停复盘：涨停表 + 日线换手/成交额 + 东财热度，输出板块热点与次日关注池（启发式）。
/// </summary>
public sealed class LimitUpAnalysisForm : Form
{
    private readonly IServiceProvider _serviceProvider;
    private DateTimePicker _dtp = null!;
    private Button _btn = null!;
    private Label _lblStatus = null!;
    private RichTextBox _rtb = null!;
    private DataGridView _grid = null!;

    public LimitUpAnalysisForm(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        Text = "涨停分析";
        Size = new Size(1280, 760);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 520);

        _lblStatus = new Label { AutoSize = true, Location = new Point(240, 12), Text = "就绪" };
        _dtp = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Value = DateTime.Today,
            Location = new Point(8, 8),
            Width = 120
        };
        _btn = new Button { Text = "分析", Location = new Point(136, 6), Width = 88, Height = 28 };
        _btn.Click += async (_, _) => await RunAsync();

        var top = new Panel { Dock = DockStyle.Top, Height = 40 };
        top.Controls.AddRange(new Control[] { _dtp, _btn, _lblStatus });

        _rtb = new RichTextBox
        {
            Dock = DockStyle.Top,
            Height = 220,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Microsoft YaHei UI", 9f),
            BackColor = SystemColors.Window,
            DetectUrls = false
        };

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Code6", HeaderText = "代码", Width = 72 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Role", HeaderText = "角色", Width = 88 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Score", HeaderText = "综合分", Width = 72 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "MinuteScore", HeaderText = "分时质量", Width = 72 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "HotRank", HeaderText = "热度排名", Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "FirstSeal", HeaderText = "首封时间", Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pct", HeaderText = "涨幅%", Width = 72 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Turn", HeaderText = "日换手%", Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Amount", HeaderText = "成交额(万)", Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Plates", HeaderText = "板块", Width = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "MinuteNote", HeaderText = "分时备注", Width = 260 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tips", HeaderText = "说明", Width = 220 });
        _grid.CellDoubleClick += Grid_CellDoubleClick;

        Controls.Add(_grid);
        Controls.Add(_rtb);
        Controls.Add(top);

        _rtb.Text = "选择交易日期后点击「分析」。若选「今天」且当前早于 15:00：涨停名单取上一交易日，分时与日 K 用今日实时。摘要含热点、综合分 Top、近窗题材持续性、次日观察启发式。维护需求见 docs/USER_REQUIREMENTS_LOG.md。双击行打开分时图。";
    }

    private async Task RunAsync()
    {
        _btn.Enabled = false;
        _lblStatus.Text = "分析中（热度+分时并发，请稍候）…";
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<LimitUpReviewService>();
            var r = await svc.AnalyzeAsync(_dtp.Value.Date, CancellationToken.None).ConfigureAwait(true);

            _rtb.Text = r.NarrativeSummary;

            _grid.Rows.Clear();
            foreach (var s in r.Stocks)
            {
                var i = _grid.Rows.Add(
                    s.Code6,
                    s.Name,
                    s.Role,
                    s.CompositeScore.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    s.MinuteQualityScore?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "-",
                    s.HotRank?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-",
                    s.FirstSealTime ?? "-",
                    s.PctChg?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "-",
                    s.DailyTurnPct?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "-",
                    s.DailyAmountWan?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "-",
                    s.Plates,
                    s.MinuteQualityNote ?? "",
                    s.Tips);
                _grid.Rows[i].Tag = s;
            }

            _lblStatus.Text = $"完成：{r.Stocks.Count} 只涨停";
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, nameof(LimitUpAnalysisForm), nameof(RunAsync));
            MessageBox.Show(this, $"分析失败：{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            _lblStatus.Text = "失败";
        }
        finally
        {
            _btn.Enabled = true;
        }
    }

    private void Grid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_grid.Rows[e.RowIndex].Tag is not LimitUpReviewStockRow s) return;

        var px = s.DailyClose ?? 0m;
        var pct = s.PctChg ?? 0m;
        if (px == 0m) px = 1m;

        using var f = new MinuteChartForm(s.Code6, s.Name, px, pct);
        f.ShowDialog(this);
    }
}
