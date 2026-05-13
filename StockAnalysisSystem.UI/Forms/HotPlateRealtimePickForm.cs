using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core.Services;

namespace StockAnalysisSystem.UI.Forms;

/// <summary>
/// 热门板块实时分析选股：当日涨停推断热点 + 近 30 日同题材涨停股 + 日线/分时/实时启发式排序（非投资建议）。
/// </summary>
public sealed class HotPlateRealtimePickForm : Form
{
    private readonly IServiceProvider _sp;

    private DateTimePicker _dtpSession = null!;
    private NumericUpDown _numHotPlates = null!;
    private NumericUpDown _numMaxCand = null!;
    private NumericUpDown _numMaxOut = null!;
    private Button _btnRun = null!;
    private DataGridView _grid = null!;
    private TextBox _txtNarrative = null!;
    private TextBox _txtLog = null!;

    public HotPlateRealtimePickForm(IServiceProvider sp)
    {
        _sp = sp;
        InitializeUi();
    }

    private void InitializeUi()
    {
        Dock = DockStyle.Fill;
        Text = "热门板块实时分析选股";

        var top = new Panel { Dock = DockStyle.Top, Height = 100, Padding = new Padding(10) };

        _dtpSession = new DateTimePicker { Left = 10, Top = 12, Width = 130, Format = DateTimePickerFormat.Short };
        _numHotPlates = new NumericUpDown { Left = 200, Top = 10, Width = 50, Minimum = 3, Maximum = 20, Value = 8 };
        _numMaxCand = new NumericUpDown { Left = 330, Top = 10, Width = 55, Minimum = 10, Maximum = 200, Value = 48 };
        _numMaxOut = new NumericUpDown { Left = 480, Top = 10, Width = 50, Minimum = 5, Maximum = 50, Value = 15 };

        _btnRun = new Button { Left = 560, Top = 8, Width = 140, Height = 30, Text = "分析选股" };
        _btnRun.Click += async (_, _) => await RunAsync();

        top.Controls.AddRange(new Control[]
        {
            new Label { Left = 10, Top = 0, Width = 120, Text = "分析日（通常今天）" },
            _dtpSession,
            new Label { Left = 160, Top = 0, Width = 120, Text = "热点板块数上限" },
            _numHotPlates,
            new Label { Left = 290, Top = 0, Width = 120, Text = "候选分析上限" },
            _numMaxCand,
            new Label { Left = 440, Top = 0, Width = 120, Text = "输出条数" },
            _numMaxOut,
            _btnRun
        });

        var warn = new Label
        {
            Left = 10,
            Top = 48,
            Width = 900,
            Height = 44,
            Text =
                "免责声明：本页为程序启发式筛选与文字标签；已排除 ST 股及 ST 板块、其他/其它等归类题材。依据涨停表、日线、分时与公开行情接口，不构成投资建议。请自行核对基本面、公告与交易规则。双击表格一行可打开该股分时图。"
        };
        top.Controls.Add(warn);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AutoGenerateColumns = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        };
        ConfigurePickGridColumns();
        _grid.CellFormatting += PickGrid_CellFormatting;
        _grid.CellDoubleClick += PickGrid_CellDoubleClick;

        _txtNarrative = new TextBox
        {
            Dock = DockStyle.Bottom,
            Height = 140,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(FontFamily.GenericMonospace, 9f)
        };

        _txtLog = new TextBox
        {
            Dock = DockStyle.Bottom,
            Height = 72,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical
        };

        var splitBottom = new Panel { Dock = DockStyle.Bottom, Height = 216 };
        splitBottom.Controls.Add(_txtLog);
        _txtLog.Dock = DockStyle.Bottom;
        splitBottom.Controls.Add(_txtNarrative);
        _txtNarrative.Dock = DockStyle.Fill;

        var fill = new Panel { Dock = DockStyle.Fill };
        fill.Controls.Add(_grid);
        fill.Controls.Add(splitBottom);

        Controls.Add(fill);
        Controls.Add(top);

        _dtpSession.Value = DateTime.Today;
    }

    private void ConfigurePickGridColumns()
    {
        _grid.Columns.Clear();

        void AddText(string dataProperty, string header, int? widthPx = null, string? format = null)
        {
            var col = new DataGridViewTextBoxColumn
            {
                DataPropertyName = dataProperty,
                HeaderText = header,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            if (widthPx.HasValue)
                col.Width = widthPx.Value;
            if (!string.IsNullOrEmpty(format))
                col.DefaultCellStyle.Format = format;
            _grid.Columns.Add(col);
        }

        AddText(nameof(HotPlateRealtimePickRow.Code6), "代码", 72);
        AddText(nameof(HotPlateRealtimePickRow.Name), "名称", 100);
        AddText(nameof(HotPlateRealtimePickRow.MatchedPlate), "匹配题材", 120);
        AddText(nameof(HotPlateRealtimePickRow.LastLimitUpDate), "最近涨停日", 108, "yyyy-MM-dd");
        AddText(nameof(HotPlateRealtimePickRow.LimitUpDayCountIn30), "30日涨停天", 92);
        AddText(nameof(HotPlateRealtimePickRow.RealtimeChangePct), "当前涨幅%", 88, "0.##");
        AddText(nameof(HotPlateRealtimePickRow.DailyScore), "日线分", 72, "0.#");
        AddText(nameof(HotPlateRealtimePickRow.MinuteScore), "分时", 64, "0.#");
        AddText(nameof(HotPlateRealtimePickRow.HotRank), "热度排名", 80);
        AddText(nameof(HotPlateRealtimePickRow.Composite), "综合分", 72, "0.#");
        AddText(nameof(HotPlateRealtimePickRow.BuyTag), "推荐", 88);
        var noteCol = new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(HotPlateRealtimePickRow.MinuteNote),
            HeaderText = "分时备注",
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            MinimumWidth = 120
        };
        _grid.Columns.Add(noteCol);
        var rationaleCol = new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(HotPlateRealtimePickRow.Rationale),
            HeaderText = "理由",
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 160
        };
        _grid.Columns.Add(rationaleCol);

        foreach (DataGridViewColumn col in _grid.Columns)
        {
            if (col.DataPropertyName == nameof(HotPlateRealtimePickRow.Rationale))
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            else
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells;
        }
    }

    private static void PickGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || sender is not DataGridView grid || e.ColumnIndex < 0 || e.ColumnIndex >= grid.Columns.Count)
            return;
        var name = grid.Columns[e.ColumnIndex].DataPropertyName;
        if (e.Value is null &&
            (name == nameof(HotPlateRealtimePickRow.RealtimeChangePct)
             || name == nameof(HotPlateRealtimePickRow.MinuteScore)
             || name == nameof(HotPlateRealtimePickRow.HotRank)))
        {
            e.Value = "-";
            e.FormattingApplied = true;
        }

        if (name == nameof(HotPlateRealtimePickRow.MinuteNote) && e.Value is string s && string.IsNullOrWhiteSpace(s))
        {
            e.Value = "-";
            e.FormattingApplied = true;
        }

    }

    private void PickGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
            return;
        if (_grid.Rows[e.RowIndex].DataBoundItem is not HotPlateRealtimePickRow row)
            return;

        var px = row.CurrentPrice ?? 0m;
        var pct = row.RealtimeChangePct ?? 0m;
        if (px <= 0)
            px = 1m;

        using var f = new MinuteChartForm(row.Code6, row.Name, px, pct);
        f.ShowDialog(this);
    }

    private void Log(string msg)
    {
        if (_txtLog.InvokeRequired)
        {
            _txtLog.Invoke(() => Log(msg));
            return;
        }

        _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
    }

    private async Task RunAsync()
    {
        _btnRun.Enabled = false;
        try
        {
            Log("开始…");
            using var scope = _sp.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<HotPlateRealtimePickService>();
            var opt = new HotPlateRealtimePickOptions
            {
                SessionDate = _dtpSession.Value.Date,
                MaxHotPlates = (int)_numHotPlates.Value,
                MaxCandidatesToAnalyze = (int)_numMaxCand.Value,
                MaxResults = (int)_numMaxOut.Value
            };

            var progress = new Progress<string>(Log);
            var result = await svc.AnalyzeAsync(opt, progress);

            _txtNarrative.Text = result.Narrative;
            _grid.DataSource = result.Rows.ToList();
            Log($"热点板块：{string.Join("、", result.HotPlates)}；输出 {result.Rows.Count} 条。");
        }
        catch (Exception ex)
        {
            Log($"失败：{ex.Message}");
        }
        finally
        {
            _btnRun.Enabled = true;
        }
    }
}
