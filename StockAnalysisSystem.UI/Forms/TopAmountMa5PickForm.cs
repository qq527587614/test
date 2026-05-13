using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core.Services;

namespace StockAnalysisSystem.UI.Forms;

public sealed class TopAmountMa5PickForm : Form
{
    private readonly IServiceProvider _sp;

    private DateTimePicker _dtpPickDate = null!;
    private NumericUpDown _numTopN = null!;
    private NumericUpDown _numMinChg = null!;
    private NumericUpDown _numMaxChg = null!;
    private NumericUpDown _numPullTol = null!;
    private NumericUpDown _numMinTurn = null!;
    private NumericUpDown _numMaxTurn = null!;
    private NumericUpDown _numMaxCapYi = null!;
    private NumericUpDown _numMinAmountWan = null!;
    private NumericUpDown _numMaxPicksPerDay = null!;
    private Button _btnPick = null!;
    private Button _btnBacktest = null!;
    private Button _btnExportBacktest = null!;
    private DataGridView _grid = null!;
    private TextBox _txtLog = null!;

    private DateTimePicker _dtpStart = null!;
    private DateTimePicker _dtpEnd = null!;

    private IReadOnlyList<TopAmountMa5BacktestDetailRow>? _lastBacktestDetails;

    public TopAmountMa5PickForm(IServiceProvider sp)
    {
        _sp = sp;
        InitializeUI();
    }

    private void InitializeUI()
    {
        Dock = DockStyle.Fill;

        var panel = new Panel { Dock = DockStyle.Top, Height = 110, Padding = new Padding(10) };

        _dtpPickDate = new DateTimePicker { Left = 10, Top = 10, Width = 130 };
        _numTopN = new NumericUpDown { Left = 160, Top = 10, Width = 60, Minimum = 1, Maximum = 200, Value = 20 };
        _numMinChg = new NumericUpDown { Left = 250, Top = 10, Width = 60, Minimum = -20, Maximum = 20, DecimalPlaces = 2, Value = 5 };
        _numMaxChg = new NumericUpDown { Left = 340, Top = 10, Width = 60, Minimum = -20, Maximum = 50, DecimalPlaces = 2, Value = 9.9m };
        _numPullTol = new NumericUpDown { Left = 440, Top = 10, Width = 60, Minimum = 0, Maximum = 0.2m, DecimalPlaces = 3, Increment = 0.001m, Value = 0.01m };

        _numMinTurn = new NumericUpDown { Left = 520, Top = 10, Width = 55, Minimum = 0, Maximum = 100, DecimalPlaces = 2, Value = 8m };
        _numMaxTurn = new NumericUpDown { Left = 610, Top = 10, Width = 55, Minimum = 0, Maximum = 100, DecimalPlaces = 2, Value = 15m };
        _numMaxCapYi = new NumericUpDown { Left = 700, Top = 10, Width = 70, Minimum = 1, Maximum = 5000, DecimalPlaces = 0, Value = 300m };
        _numMinAmountWan = new NumericUpDown { Left = 780, Top = 10, Width = 80, Minimum = 0, Maximum = 9_999_999, DecimalPlaces = 0, Value = 0m, ThousandsSeparator = true };

        _btnPick = new Button { Left = 520, Top = 8, Width = 120, Height = 28, Text = "按日选股" };
        _btnPick.Click += async (_, _) => await RunPickAsync();

        var lbl1 = new Label { Left = 10, Top = 40, Width = 140, Text = "回测区间(收盘→次日收盘)" };
        _dtpStart = new DateTimePicker { Left = 160, Top = 38, Width = 130 };
        _dtpEnd = new DateTimePicker { Left = 300, Top = 38, Width = 130 };
        _numMaxPicksPerDay = new NumericUpDown { Left = 440, Top = 38, Width = 60, Minimum = 1, Maximum = 50, Value = 3 };
        _btnBacktest = new Button { Left = 520, Top = 36, Width = 100, Height = 28, Text = "回测" };
        _btnBacktest.Click += async (_, _) => await RunBacktestAsync();

        _btnExportBacktest = new Button { Left = 628, Top = 36, Width = 120, Height = 28, Text = "导出回测明细", Enabled = false };
        _btnExportBacktest.Click += (_, _) => ExportLastBacktestDetails();

        panel.Controls.AddRange(new Control[]
        {
            new Label { Left = 10, Top = 0, Width = 120, Text = "选股日期" },
            _dtpPickDate,
            new Label { Left = 160, Top = 0, Width = 90, Text = "TopN成交额" },
            _numTopN,
            new Label { Left = 250, Top = 0, Width = 90, Text = "涨幅>=%" },
            _numMinChg,
            new Label { Left = 340, Top = 0, Width = 90, Text = "涨幅<=%" },
            _numMaxChg,
            new Label { Left = 440, Top = 0, Width = 110, Text = "回踩容忍" },
            _numPullTol,
            new Label { Left = 520, Top = 0, Width = 90, Text = "换手% >= " },
            _numMinTurn,
            new Label { Left = 610, Top = 0, Width = 90, Text = "换手% <= " },
            _numMaxTurn,
            new Label { Left = 700, Top = 0, Width = 90, Text = "市值<亿" },
            _numMaxCapYi,
            new Label { Left = 780, Top = 0, Width = 90, Text = "额>=万" },
            _numMinAmountWan,
            _btnPick,
            lbl1,
            _dtpStart,
            _dtpEnd,
            new Label { Left = 440, Top = 66, Width = 110, Text = "每日最多" },
            _numMaxPicksPerDay,
            _btnBacktest,
            _btnExportBacktest
        });

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        _txtLog = new TextBox
        {
            Dock = DockStyle.Bottom,
            Height = 110,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical
        };

        Controls.AddRange(new Control[] { _grid, _txtLog, panel });

        _dtpPickDate.Value = DateTime.Today.AddDays(-1);
        _dtpStart.Value = DateTime.Today.AddMonths(-3);
        _dtpEnd.Value = DateTime.Today.AddDays(-1);
    }

    private TopAmountMa5PickOptions BuildOptions()
    {
        return new TopAmountMa5PickOptions
        {
            TopNByAmount = (int)_numTopN.Value,
            MinChangePct = _numMinChg.Value,
            MaxChangePct = _numMaxChg.Value,
            PullbackTolerance = _numPullTol.Value,
            MinTurnoverRate = _numMinTurn.Value,
            MaxTurnoverRate = _numMaxTurn.Value,
            MaxTotalMarketCapYi = _numMaxCapYi.Value,
            MinAmountWan = _numMinAmountWan.Value
        };
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

    private async Task RunPickAsync()
    {
        _btnPick.Enabled = false;
        try
        {
            using var scope = _sp.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<TopAmountMa5PickService>();
            var rows = await svc.PickAsync(_dtpPickDate.Value.Date, BuildOptions());

            _grid.DataSource = rows;
            Log($"选股完成：{rows.Count} 条（满足 Top成交额 + 回踩MA5 + 涨幅过滤）");
        }
        catch (Exception ex)
        {
            Log($"选股失败：{ex.Message}");
        }
        finally
        {
            _btnPick.Enabled = true;
        }
    }

    private async Task RunBacktestAsync()
    {
        _btnBacktest.Enabled = false;
        _btnExportBacktest.Enabled = false;
        try
        {
            using var scope = _sp.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<TopAmountMa5PickService>();
            var result = await svc.BacktestCloseToCloseWithDetailsAsync(_dtpStart.Value.Date, _dtpEnd.Value.Date, BuildOptions(),
                maxPicksPerDay: (int)_numMaxPicksPerDay.Value);

            _lastBacktestDetails = result.Details;
            var sum = result.Summary;
            Log($"回测完成：交易日={sum.TradingDays} 有选股日={sum.DaysWithPicks} picks={sum.Picks} trades={sum.Trades} 胜率={sum.WinRate:P2} 平均收益={sum.AvgReturnPct:0.###}% 总收益(累加)={sum.TotalReturnPct:0.###}%（明细 {result.Details.Count} 条，可点「导出回测明细」）");
        }
        catch (Exception ex)
        {
            _lastBacktestDetails = null;
            Log($"回测失败：{ex.Message}");
        }
        finally
        {
            _btnBacktest.Enabled = true;
            _btnExportBacktest.Enabled = _lastBacktestDetails is { Count: > 0 };
        }
    }

    private void ExportLastBacktestDetails()
    {
        if (_lastBacktestDetails is not { Count: > 0 } rows)
        {
            Log("没有可导出的回测明细，请先执行一次「回测」。");
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "导出回测明细",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            DefaultExt = "csv",
            FileName = $"TopAmountMa5_回测明细_{_dtpStart.Value:yyyyMMdd}_{_dtpEnd.Value:yyyyMMdd}.csv"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            WriteBacktestDetailsCsv(dlg.FileName, rows);
            Log($"已导出 {rows.Count} 条明细：{dlg.FileName}");
        }
        catch (Exception ex)
        {
            Log($"导出失败：{ex.Message}");
        }
    }

    private static void WriteBacktestDetailsCsv(string path, IReadOnlyList<TopAmountMa5BacktestDetailRow> rows)
    {
        var inv = CultureInfo.InvariantCulture;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        writer.WriteLine(string.Join(',',
            "选股日",
            "计划卖出日",
            "代码",
            "名称",
            "买入有效收盘",
            "卖出有效收盘",
            "收益率%",
            "有次日K线",
            "成交额",
            "涨幅%",
            "MA5",
            "开盘",
            "最高",
            "最低",
            "换手率%",
            "估算流通市值(亿)",
            "StockId",
            "选股说明"));

        foreach (var r in rows)
        {
            writer.WriteLine(string.Join(',',
                r.PickDate.ToString("yyyy-MM-dd", inv),
                r.PlannedSellDate.ToString("yyyy-MM-dd", inv),
                Csv(r.StockCode),
                Csv(r.StockName),
                r.BuyClose.ToString(inv),
                r.SellClose?.ToString(inv) ?? "",
                r.ReturnPct?.ToString("0.####", inv) ?? "",
                r.HasNextDayBar ? "1" : "0",
                r.Amount.ToString(inv),
                r.ChangePct?.ToString("0.####", inv) ?? "",
                r.MA5?.ToString("0.####", inv) ?? "",
                r.OpenPrice.ToString(inv),
                r.HighPrice.ToString(inv),
                r.LowPrice.ToString(inv),
                r.TurnoverRate?.ToString("0.####", inv) ?? "",
                r.EstCirculationCapYi?.ToString("0.##", inv) ?? "",
                Csv(r.StockId),
                Csv(r.Reason)));
        }
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        var t = s;
        if (t.Contains('"', StringComparison.Ordinal))
            t = t.Replace("\"", "\"\"", StringComparison.Ordinal);
        if (t.Contains(',') || t.Contains('\n') || t.Contains('\r') || t.Contains('"'))
            return $"\"{t}\"";
        return t;
    }
}

