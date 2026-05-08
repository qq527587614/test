using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core;
using StockAnalysisSystem.Core.Services;
using StockAnalysisSystem.Core.Utils;
using System.Text.Json;

namespace StockAnalysisSystem.UI.Forms;

/// <summary>
/// 热点选股：按「(实时价−涨停假设MA5)/实时价×100%」升序；负值距五日线单元格显示绿色。
/// </summary>
public class HotSpotPickForm : Form
{
    private readonly HotSpotLimitUpMa5Picker _picker;
    private readonly StockAnalysisSystem.Core.RealtimeData.TencentRealtimeService _realtime;
    private readonly IServiceProvider _serviceProvider;
    private DataGridView _grid = null!;
    private Button _btnRun = null!;
    private DateTimePicker _dtpAsOf = null!;
    private Label _lblHint = null!;
    private Label _lblStatus = null!;
    private readonly System.Windows.Forms.Timer _realtimeTimer;
    private bool _realtimeRefreshBusy;
    private static readonly HttpClient _eastMoneyHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    public HotSpotPickForm(HotSpotLimitUpMa5Picker picker, StockAnalysisSystem.Core.RealtimeData.TencentRealtimeService realtime, IServiceProvider serviceProvider)
    {
        _picker = picker;
        _realtime = realtime;
        _serviceProvider = serviceProvider;
        Text = "热点选股";
        Size = new Size(1100, 640);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(800, 400);

        _lblHint = new Label
        {
            AutoSize = false,
            Padding = new Padding(8, 8, 8, 4),
            Text = "条件：涨停表近 30 日内有涨停；窗口内首板（首次涨停日）不能为评估日当天；自窗口内首次涨停日起至评估日前一交易日，MA5 不破天数占比≥80%（日线口径价）；评估日当天开盘五日线为（前 4 日收盘口径 + 今开）/5，要求现价不低于该线；区间内从未跌破 MA10（日线口径价）；仅显示东方财富热度排名前 200。" +
                   "排序：(实时价−涨停假设MA5)/实时价×100%，数值越小越靠前；负值表示实时价低于涨停假设五日线，该列显示为绿色。无实时行情时不参与排序，排在末尾。"
        };

        var panelTop = new Panel { Height = 40 };
        _dtpAsOf = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Value = DateTime.Today,
            Location = new Point(8, 8),
            Width = 120
        };
        _btnRun = new Button
        {
            Text = "刷新",
            Location = new Point(140, 6),
            Width = 88,
            Height = 28
        };
        _btnRun.Click += async (_, _) => await RunPickAsync();
        _lblStatus = new Label
        {
            AutoSize = true,
            Location = new Point(240, 12),
            Text = "就绪"
        };
        panelTop.Controls.AddRange(new Control[] { _dtpAsOf, _btnRun, _lblStatus });

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            RowHeadersVisible = false,
            ColumnHeadersVisible = true,
            ColumnHeadersHeight = 28,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "StockCode", HeaderText = "代码", Width = 72 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "StockName", HeaderText = "名称", Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "HotRank", HeaderText = "热度排名", Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "FirstLimit", HeaderText = "窗口内首涨停日", Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RecentLimit", HeaderText = "最近涨停日", Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "PeriodGain", HeaderText = "周期涨幅%", Width = 88 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "FromHigh", HeaderText = "距高点回落%", Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Plates", HeaderText = "板块", Width = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LastTrade", HeaderText = "最新日线", Width = 95 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LimitPx", HeaderText = "今日涨停价", Width = 95 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ma5", HeaderText = "含涨停假设MA5", Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RtPx", HeaderText = "实时价", Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RtOpen", HeaderText = "今开", Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RtVsOpenPct", HeaderText = "现价-今开%", Width = 95 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RtPct", HeaderText = "实时涨幅%", Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RtLow", HeaderText = "实时最低", Width = 85 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RtMa5", HeaderText = "开盘MA5", Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Dist", HeaderText = "距涨停MA5%", Width = 90 });
        _grid.CellFormatting += Grid_CellFormatting;

        _realtimeTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _realtimeTimer.Tick += RealtimeTimer_Tick;
        FormClosed += (_, _) =>
        {
            _realtimeTimer.Stop();
            _realtimeTimer.Tick -= RealtimeTimer_Tick;
            _realtimeTimer.Dispose();
        };

        // 用 TableLayoutPanel 分区，避免多个 Dock=Top + Fill 叠放时列头被盖住
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _lblHint.Dock = DockStyle.Fill;
        panelTop.Dock = DockStyle.Fill;
        _grid.Dock = DockStyle.Fill;
        root.Controls.Add(_lblHint, 0, 0);
        root.Controls.Add(panelTop, 0, 1);
        root.Controls.Add(_grid, 0, 2);
        Controls.Add(root);

        Shown += async (_, _) => await RunPickAsync();
    }

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var col = _grid.Columns[e.ColumnIndex];
        if (col == null || col.Name != "Dist") return;
        var cellStyle = e.CellStyle ?? _grid.DefaultCellStyle;
        if (cellStyle == null) return;
        var raw = e.Value?.ToString();
        var defaultFg = _grid.DefaultCellStyle?.ForeColor ?? ForeColor;
        if (string.IsNullOrWhiteSpace(raw) || raw == "-")
        {
            cellStyle.ForeColor = defaultFg;
            return;
        }

        if (decimal.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out var v) && v < 0)
            cellStyle.ForeColor = Color.Green;
        else
            cellStyle.ForeColor = defaultFg;
    }

    private static string ToTencentQuoteCode(string code6)
    {
        if (string.IsNullOrWhiteSpace(code6) || code6.Length < 2)
            return "sz" + code6;
        if (code6.StartsWith("60", StringComparison.Ordinal) || code6.StartsWith("68", StringComparison.Ordinal))
            return "sh" + code6;
        if (code6.StartsWith("00", StringComparison.Ordinal) || code6.StartsWith("30", StringComparison.Ordinal))
            return "sz" + code6;
        if (code6.StartsWith("43", StringComparison.Ordinal) || code6.StartsWith("83", StringComparison.Ordinal) ||
            code6.StartsWith("87", StringComparison.Ordinal) || code6.StartsWith("92", StringComparison.Ordinal))
            return "bj" + code6;
        return "sz" + code6;
    }

    private sealed class HotSpotGridRowTag
    {
        public required HotSpotPickRow Pick { get; init; }
        public required string Plates { get; init; }
        public int? HotRank { get; set; }
    }

    private static async Task<Dictionary<string, int>> FetchEastMoneyHotRankTop1000Async()
    {
        // returns SECURITY_CODE -> POPULARITY_RANK
        const string url = "https://data.eastmoney.com/dataapi/xuangu/list" +
                           "?st=CHANGE_RATE&sr=-1&ps=1000&p=1&sty=SECUCODE%2CSECURITY_CODE%2CSECURITY_NAME_ABBR%2CNEW_PRICE%2CCHANGE_RATE%2CVOLUME_RATIO%2CHIGH_PRICE%2CLOW_PRICE%2CPRE_CLOSE_PRICE%2CVOLUME%2CDEAL_AMOUNT%2CTURNOVERRATE%2CPOPULARITY_RANK" +
                           "&filter=(POPULARITY_RANK%3E0)(POPULARITY_RANK%3C%3D1000)&source=SELECT_SECURITIES&client=WEB";

        var json = await _eastMoneyHttp.GetStringAsync(url).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, int>(StringComparer.Ordinal);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
            return new Dictionary<string, int>(StringComparer.Ordinal);

        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("SECURITY_CODE", out var codeEl)) continue;
            if (!item.TryGetProperty("POPULARITY_RANK", out var rankEl)) continue;
            var code = codeEl.GetString();
            if (string.IsNullOrWhiteSpace(code)) continue;
            if (rankEl.ValueKind != JsonValueKind.Number) continue;
            if (!rankEl.TryGetInt32(out var rank)) continue;
            dict[code] = rank;
        }

        return dict;
    }

    private async void RealtimeTimer_Tick(object? sender, EventArgs e)
    {
        if (!Visible || _grid.Rows.Count == 0 || _realtimeRefreshBusy)
            return;
        _realtimeRefreshBusy = true;
        _realtimeTimer.Stop();
        try
        {
            await RefreshRealtimeColumnsAsync();
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "HotSpotPickForm.RealtimeTimer_Tick", "");
        }
        finally
        {
            _realtimeRefreshBusy = false;
            if (Visible && _grid.Rows.Count > 0)
                _realtimeTimer.Start();
        }
    }

    private async Task RefreshRealtimeColumnsAsync()
    {
        var rowPicks = new List<(DataGridViewRow Row, HotSpotPickRow Pick)>(_grid.Rows.Count);
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is HotSpotGridRowTag t)
                rowPicks.Add((row, t.Pick));
        }

        if (rowPicks.Count == 0)
            return;

        var qcodes = rowPicks
            .Select(x => x.Pick.StockCode)
            .Distinct(StringComparer.Ordinal)
            .Select(ToTencentQuoteCode)
            .ToList();

        Dictionary<string, StockAnalysisSystem.Core.RealtimeData.RealtimeStockData> rtDict;
        try
        {
            var rt = await _realtime.GetRealtimeDataAsync(qcodes);
            rtDict = rt
                .Where(x => !string.IsNullOrWhiteSpace(x.StockCode))
                .ToDictionary(x => x.StockCode, x => x, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "HotSpotPickForm.RefreshRealtimeColumnsAsync", "");
            return;
        }

        foreach (var (row, pick) in rowPicks)
        {
            rtDict.TryGetValue(pick.StockCode, out var rt);
            var rtPx = rt?.CurrentPrice;
            decimal? openMa5 = null;
            if (rt != null && rt.OpenPrice > 0m)
                openMa5 = (pick.Prev4CloseSum + rt.OpenPrice) / 5m;

            decimal? dist = null;
            if (rtPx.HasValue && rtPx.Value > 0)
                dist = (rtPx.Value - pick.Ma5WithTodayLimit) / rtPx.Value * 100m;

            row.Cells["RtPx"].Value = rtPx.HasValue && rtPx.Value > 0 ? rtPx.Value.ToString("F2") : "-";
            row.Cells["RtOpen"].Value = rt != null && rt.OpenPrice > 0m ? rt.OpenPrice.ToString("F2") : "-";
            if (rtPx.HasValue && rtPx.Value > 0m && rt != null && rt.OpenPrice > 0m)
                row.Cells["RtVsOpenPct"].Value = ((rtPx.Value - rt.OpenPrice) / rt.OpenPrice * 100m).ToString("F2");
            else
                row.Cells["RtVsOpenPct"].Value = "-";
            row.Cells["RtPct"].Value = rt != null ? rt.ChangePercent.ToString("F2") : "-";
            row.Cells["RtLow"].Value = rt != null ? rt.LowPrice.ToString("F2") : "-";
            row.Cells["RtMa5"].Value = openMa5.HasValue ? openMa5.Value.ToString("F2") : "-";
            row.Cells["Dist"].Value = dist.HasValue ? dist.Value.ToString("F2") : "-";
        }

        // 过滤：实时最低价必须 < 含涨停假设MA5，否则不显示（定时刷新时也保持该条件）。
        var toRemove = new List<DataGridViewRow>();
        foreach (var (row, pick) in rowPicks)
        {
            rtDict.TryGetValue(pick.StockCode, out var rt);
            if (rt == null || rt.LowPrice <= 0m || rt.LowPrice >= pick.Ma5WithTodayLimit)
                toRemove.Add(row);
        }

        foreach (var r in toRemove)
            _grid.Rows.Remove(r);

        if (_grid.Columns["Dist"] is { } distCol)
            _grid.InvalidateColumn(distCol.Index);
    }

    private async Task RunPickAsync()
    {
        _realtimeTimer.Stop();
        _btnRun.Enabled = false;
        _grid.Rows.Clear();
        try
        {
            var progress = new Progress<string>(msg =>
            {
                void Set() => _lblStatus.Text = msg;
                if (InvokeRequired) Invoke(Set); else Set();
            });

            var rows = await _picker.PickAsync(_dtpAsOf.Value.Date, progress);

            // 东方财富热度排名（Top1000）；失败不影响主流程
            Dictionary<string, int> hotRankByCode;
            try
            {
                _lblStatus.Text = "获取热度排名（东方财富）…";
                hotRankByCode = await FetchEastMoneyHotRankTop1000Async();
            }
            catch (Exception ex)
            {
                ErrorLogger.Log(ex, "HotSpotPickForm.RunPickAsync.HotRank", "");
                hotRankByCode = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            // 仅显示热度排名前 200；不在榜单中的也不显示（避免冷门股）。
            const int hotRankThreshold = 200;
            rows = rows
                .Where(r => hotRankByCode.TryGetValue(r.StockCode, out var rank) && rank > 0 && rank <= hotRankThreshold)
                .ToList();

            // 批量拉取实时行情（与板块分析一致的腾讯接口）
            var rtDict = new Dictionary<string, StockAnalysisSystem.Core.RealtimeData.RealtimeStockData>(StringComparer.Ordinal);
            try
            {
                var qcodes = rows
                    .Select(r => ToTencentQuoteCode(r.StockCode))
                    .Distinct()
                    .ToList();
                var rt = await _realtime.GetRealtimeDataAsync(qcodes);
                rtDict = rt
                    .Where(x => !string.IsNullOrWhiteSpace(x.StockCode))
                    .ToDictionary(x => x.StockCode, x => x, StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                ErrorLogger.Log(ex, "HotSpotPickForm.RunPickAsync.Realtime", "");
            }

            // 关联涨停表，聚合板块信息（与 DailyPickForm 一致：涨停表 code 带 sh/sz 前缀）
            var plateDict = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var stockCodes = rows.Select(r => r.StockCode).Distinct().ToList();
                if (stockCodes.Count > 0)
                {
                    var stockCodesWithPrefix = stockCodes
                        .Select(ToTencentQuoteCode)
                        .ToList();

                    var limitUpRecords = dbContext.StockLimitUpAnalysis
                        .Where(s => stockCodesWithPrefix.Contains(s.code))
                        .ToList();

                    plateDict = limitUpRecords
                        .GroupBy(s => s.code.Length > 2 ? s.code.Substring(2) : s.code)
                        .ToDictionary(
                            g => g.Key,
                            g => string.Join(", ",
                                g.Where(p => !string.IsNullOrEmpty(p.plate_name))
                                    .Select(p => p.plate_name!)
                                    .Distinct()));
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.Log(ex, "HotSpotPickForm.RunPickAsync.Plates", "");
            }

            var ordered = rows
                .Select(r =>
                {
                    rtDict.TryGetValue(r.StockCode, out var rt);
                    var px = rt?.CurrentPrice ?? 0m;
                    decimal? distRtVsLimitMa5 = null;
                    if (px > 0)
                        distRtVsLimitMa5 = (px - r.Ma5WithTodayLimit) / px * 100m;
                    return (Row: r, Rt: rt, Dist: distRtVsLimitMa5);
                })
                .OrderBy(x => x.Dist.HasValue ? x.Dist!.Value : decimal.MaxValue)
                .ThenBy(x => x.Row.StockCode)
                .ToList();

            var shown = 0;
            foreach (var x in ordered)
            {
                var r = x.Row;
                var rt = x.Rt;

                // 过滤：实时最低价必须 < 含涨停假设MA5；无实时行情/无最低价时不显示（无法判断）。
                if (rt == null || rt.LowPrice <= 0m || rt.LowPrice >= r.Ma5WithTodayLimit)
                    continue;

                var rtPx = rt?.CurrentPrice;
                var openMa5 = (rt != null && rt.OpenPrice > 0m)
                    ? (r.Prev4CloseSum + rt.OpenPrice) / 5m
                    : (decimal?)null;

                var distStr = x.Dist.HasValue ? x.Dist.Value.ToString("F2") : "-";
                var platesCell = plateDict.TryGetValue(r.StockCode, out var plates) && !string.IsNullOrWhiteSpace(plates)
                    ? plates
                    : "-";

                var idx = _grid.Rows.Add(
                    r.StockCode,
                    r.StockName,
                    hotRankByCode.TryGetValue(r.StockCode, out var hr) ? hr.ToString() : "-",
                    r.FirstLimitUpInWindow.ToString("yyyy-MM-dd"),
                    r.RecentLimitUpDate.ToString("yyyy-MM-dd"),
                    r.PeriodGainPercent.ToString("F2"),
                    r.PullbackFromPeriodHighPercent.ToString("F2"),
                    platesCell,
                    r.LastTradeDate.ToString("yyyy-MM-dd"),
                    r.TodayLimitPrice.ToString("F2"),
                    r.Ma5WithTodayLimit.ToString("F2"),
                    rtPx.HasValue && rtPx.Value > 0 ? rtPx.Value.ToString("F2") : "-",
                    rt != null && rt.OpenPrice > 0m ? rt.OpenPrice.ToString("F2") : "-",
                    (rt != null && rt.OpenPrice > 0m && rtPx.HasValue && rtPx.Value > 0m)
                        ? ((rtPx.Value - rt.OpenPrice) / rt.OpenPrice * 100m).ToString("F2")
                        : "-",
                    rt != null ? rt.ChangePercent.ToString("F2") : "-",
                    rt != null ? rt.LowPrice.ToString("F2") : "-",
                    openMa5.HasValue ? openMa5.Value.ToString("F2") : "-",
                    distStr);
                _grid.Rows[idx].Tag = new HotSpotGridRowTag
                {
                    Pick = r,
                    Plates = platesCell,
                    HotRank = hotRankByCode.TryGetValue(r.StockCode, out var rank) ? rank : null
                };
                shown++;
            }

            void Done()
            {
                _lblStatus.Text = $"共 {shown} 只（已过滤：实时最低价<涨停MA5；按距涨停MA5%升序；实时行情每 5 秒刷新）";
                if (shown > 0)
                    _realtimeTimer.Start();
            }
            if (InvokeRequired) Invoke(Done); else Done();
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "HotSpotPickForm.RunPickAsync", "");
            MessageBox.Show($"加载失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _lblStatus.Text = "失败";
            _realtimeTimer.Stop();
        }
        finally
        {
            _btnRun.Enabled = true;
        }
    }
}
