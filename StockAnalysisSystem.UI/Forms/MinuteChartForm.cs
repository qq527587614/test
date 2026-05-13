using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using StockAnalysisSystem.Core.RealtimeData;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.UI.Forms;

public partial class MinuteChartForm : Form
{
    /// <summary>交易时段内「从 9:30 起」的分钟坐标上界（含 15:00 对应 240）。</summary>
    private const int SessionMinuteMax = 240;
    private const int LeftPriceLabelWidth = 62;
    private const int BottomTimeBand = 26;
    private const float PriceAreaRatio = 0.72f;
    private const float VolumeAreaRatio = 0.22f;

    private readonly SinaMinuteChartService _chartService;
    private readonly string _stockCode;
    private readonly string _stockName;
    private readonly string _market;

    private Panel _chartPanel = null!;
    private Label _lblTitle = null!;
    private Label _lblPrice = null!;
    private Label _lblChange = null!;
    private Label _lblStatus = null!;
    private ComboBox _cbScale = null!;
    private Button _btnRefresh = null!;

    private List<MinuteChartData> _minuteData = new();
    private decimal _yesterdayClose;
    /// <summary>当日涨跌幅（百分点，如 9.98 表示 9.98%）。</summary>
    private decimal _dayChangePctPoints;
    private readonly bool _useMainBoardPctAxis;

    /// <summary>与当前 K 线对齐的横轴：按 <see cref="MinuteChartData.MinutesFromStart"/> 最小/最大拉伸到全宽。</summary>
    private int _xMinuteLo;
    private int _xMinuteHi;

    public MinuteChartForm(string stockCode, string stockName, decimal currentPrice, decimal changePercent)
    {
        _chartService = new SinaMinuteChartService();
        _stockCode = SinaMinuteChartService.NormalizeToCode6(stockCode);
        _stockName = stockName;
        _market = SinaMinuteChartService.ResolveExchangeMarket(_stockCode);
        _useMainBoardPctAxis = IsMainBoardTenPercentLimitBoard(_stockCode);

        _dayChangePctPoints = PercentPointNormalization.ToChangePercentPoints(changePercent, currentPrice);
        if (currentPrice > 0 && _dayChangePctPoints != 0)
            _yesterdayClose = currentPrice / (1 + _dayChangePctPoints / 100m);

        InitializeComponent();
        LoadDataAsync();
    }

    /// <summary>沪/深主板约 ±10% 涨跌停：60 沪市主板、00 深市主板（不含创业板 30、科创板 68 等）。</summary>
    private static bool IsMainBoardTenPercentLimitBoard(string code6)
    {
        if (string.IsNullOrEmpty(code6) || code6.Length < 2) return false;
        return code6.StartsWith("60", StringComparison.Ordinal)
               || code6.StartsWith("00", StringComparison.Ordinal);
    }

    private static decimal ChangePercentFromYesterday(decimal price, decimal yesterdayClose)
    {
        if (yesterdayClose <= 0) return 0;
        return (price - yesterdayClose) / yesterdayClose * 100m;
    }

    private void InitializeComponent()
    {
        Text = $"{_stockName} ({_stockCode}) 分时图";
        Size = new Size(900, 600);
        StartPosition = FormStartPosition.CenterParent;

        var topPanel = new Panel { Dock = DockStyle.Top, Height = 85, Padding = new Padding(10) };

        _lblTitle = new Label
        {
            Text = $"{_stockName} ({_stockCode})",
            Font = new Font("微软雅黑", 14, FontStyle.Bold),
            Left = 10,
            Top = 10,
            AutoSize = true
        };

        _lblPrice = new Label
        {
            Text = "当前价: --",
            Font = new Font("微软雅黑", 12),
            Left = 10,
            Top = 45,
            AutoSize = true
        };

        _lblChange = new Label
        {
            Text = "涨跌幅: --",
            Font = new Font("微软雅黑", 12),
            Left = 180,
            Top = 45,
            AutoSize = true
        };

        var lblScale = new Label { Text = "周期:", Left = 450, Top = 48, Width = 40 };
        _cbScale = new ComboBox
        {
            Left = 490,
            Top = 44,
            Width = 80,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cbScale.Items.AddRange(new[] { "1分钟", "5分钟", "15分钟", "30分钟", "60分钟" });
        _cbScale.SelectedIndex = 0;
        _cbScale.SelectedIndexChanged += CbScale_SelectedIndexChanged;

        _btnRefresh = new Button { Text = "刷新", Left = 580, Top = 42, Width = 60, Height = 26 };
        _btnRefresh.Click += (_, _) => LoadDataAsync();

        _lblStatus = new Label
        {
            Text = "加载中...",
            Left = 650,
            Top = 48,
            Width = 420,
            AutoSize = false,
            ForeColor = Color.Gray
        };

        topPanel.Controls.AddRange(new Control[] { _lblTitle, _lblPrice, _lblChange, lblScale, _cbScale, _btnRefresh, _lblStatus });

        _chartPanel = new DoubleBufferedChartPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(18, 18, 22),
            Padding = new Padding(0)
        };
        _chartPanel.Paint += ChartPanel_Paint;

        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.FromArgb(30, 30, 30) };
        var lblInfo = new Label
        {
            Text = _useMainBoardPctAxis
                ? "数据来源: 新浪财经 | 黄虚线=VWAP(Σ成交额/Σ成交量) | 灰=昨收(0%) | 蓝=开盘 | 左轴=相对昨收涨跌幅(00/60 主板 ±10% 刻度)"
                : "数据来源: 新浪财经 | 黄虚线=VWAP(累计成交额/累计成交量) | 灰=昨收 | 蓝=首根开盘价",
            Dock = DockStyle.Fill,
            ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            Font = new Font("微软雅黑", 8f)
        };
        bottomPanel.Controls.Add(lblInfo);

        Controls.Add(_chartPanel);
        Controls.Add(bottomPanel);
        Controls.Add(topPanel);
    }

    private sealed class DoubleBufferedChartPanel : Panel
    {
        public DoubleBufferedChartPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    private void CbScale_SelectedIndexChanged(object? sender, EventArgs e) => LoadDataAsync();

    private int GetSelectedScale() => _cbScale.SelectedIndex switch
    {
        0 => 1,
        1 => 5,
        2 => 15,
        3 => 30,
        4 => 60,
        _ => 1
    };

    private async void LoadDataAsync()
    {
        try
        {
            _lblStatus.Text = "加载中...";
            _btnRefresh.Enabled = false;

            if (string.IsNullOrEmpty(_stockCode) || _stockCode.Length != 6 || !_stockCode.All(char.IsDigit))
            {
                _lblStatus.Text = "股票代码无效，无法拉取分时";
                _chartPanel.Invalidate();
                return;
            }

            var scale = GetSelectedScale();
            var (data, error) = await _chartService.GetMinuteChartDataAsync(_stockCode, scale, null);

            if (!string.IsNullOrEmpty(error))
            {
                _lblStatus.Text = error;
                ErrorLogger.LogDiagnostics("minute_chart", "MinuteChartForm 拉取失败", $"code={_stockCode} market={_market}\n{error}");
                _chartPanel.Invalidate();
                return;
            }

            if (data.Count == 0)
            {
                _lblStatus.Text = "暂无数据";
                _chartPanel.Invalidate();
                return;
            }

            // 去掉 15:00 之后的 K 线（接口偶发盘后/无效时间戳）；再按时间排序
            var sessionBars = data.Where(d => !IsBarAfterRegularSession(d)).ToList();
            _minuteData = sessionBars
                .OrderBy(d => d.MinutesFromStart)
                .ThenBy(d => d.Time ?? "", StringComparer.Ordinal)
                .ToList();

            if (_minuteData.Count == 0)
            {
                _lblStatus.Text = "暂无数据";
                _chartPanel.Invalidate();
                return;
            }

            var mins = _minuteData.Select(d => d.MinutesFromStart).ToList();
            _xMinuteLo = Math.Max(0, mins.Min());
            _xMinuteHi = Math.Min(SessionMinuteMax, Math.Max(_xMinuteLo + 1, mins.Max()));

            var latest = _minuteData[^1];
            _lblPrice.Text = $"当前价: {latest.Close:F2}";

            if (_yesterdayClose > 0 && latest.Close > 0)
                _dayChangePctPoints = ChangePercentFromYesterday(latest.Close, _yesterdayClose);

            _lblChange.Text = $"涨跌幅: {_dayChangePctPoints:+0.00;-0.00}%";
            _lblChange.ForeColor = _dayChangePctPoints >= 0 ? Color.FromArgb(220, 80, 80) : Color.FromArgb(80, 200, 120);

            _lblStatus.Text = $"共 {_minuteData.Count} 根K线 | 横轴 {_xMinuteLo}–{_xMinuteHi} 分";
            _chartPanel.Invalidate();
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "加载失败";
            ErrorLogger.Log(ex, "MinuteChartForm.LoadDataAsync", $"股票: {_stockCode}, 市场: {_market}");
            MessageBox.Show($"加载数据失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnRefresh.Enabled = true;
        }
    }

    /// <summary>是否应视为「常规交易结束」之后的无效/盘后 K 线（不含 15:00 当根）。</summary>
    private static bool IsBarAfterRegularSession(MinuteChartData d)
    {
        if (d.MinutesFromStart > SessionMinuteMax)
            return true;
        if (TryParseBarClockMinutes(d.Time, out var clock))
            return clock > 15 * 60;
        return false;
    }

    private static bool TryParseBarClockMinutes(string? time, out int clockMinutes)
    {
        clockMinutes = 0;
        if (string.IsNullOrWhiteSpace(time)) return false;
        var tail = time.Contains(' ')
            ? time.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1]
            : time;
        var seg = tail.Split(':');
        if (seg.Length < 2) return false;
        if (!int.TryParse(seg[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)) return false;
        if (!int.TryParse(seg[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mi)) return false;
        clockMinutes = h * 60 + mi;
        return true;
    }

    private float XFromMinutes(RectangleF chartRect, int minutesFromStart)
    {
        var span = Math.Max(1, _xMinuteHi - _xMinuteLo);
        var t = (minutesFromStart - _xMinuteLo) / (float)span;
        t = Math.Clamp(t, 0f, 1f);
        return chartRect.Left + t * chartRect.Width;
    }

    private static (decimal Min, decimal Max) GetPriceBounds(IReadOnlyList<MinuteChartData> data, decimal yesterdayClose)
    {
        var list = new List<decimal>(data.Count * 4 + 4);
        foreach (var d in data)
        {
            list.Add(d.High);
            list.Add(d.Low);
            list.Add(d.Close);
            list.Add(d.AvgPrice);
        }

        if (data.Count > 0)
            list.Add(data[0].Open);

        if (yesterdayClose > 0)
            list.Add(yesterdayClose);

        var min = list.Min();
        var max = list.Max();
        var pad = (max - min);
        if (pad == 0) pad = Math.Abs(min) * 0.01m;
        if (pad == 0) pad = 0.01m;
        min -= pad * 0.08m;
        max += pad * 0.08m;
        return (min, max);
    }

    private static float YFromLinearAxis(RectangleF chartRect, decimal value, decimal minV, decimal maxV)
    {
        var range = maxV - minV;
        if (range <= 0) return chartRect.Top + chartRect.Height / 2f;
        var t = (double)((value - minV) / range);
        t = Math.Clamp(t, 0, 1);
        return chartRect.Bottom - (float)t * chartRect.Height;
    }

    private void ChartPanel_Paint(object? sender, PaintEventArgs e)
    {
        if (_minuteData.Count == 0) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        var client = _chartPanel.ClientRectangle;
        using (var bg = new SolidBrush(_chartPanel.BackColor))
            g.FillRectangle(bg, client);

        var left = LeftPriceLabelWidth;
        var rightPad = 10;
        var topPad = 8;
        var innerH = Math.Max(80, client.Height - topPad - BottomTimeBand);
        var priceH = innerH * PriceAreaRatio;
        var gap = 6f;
        var volH = innerH * VolumeAreaRatio;
        var chartRect = new RectangleF(left, topPad, Math.Max(20f, client.Width - left - rightPad), priceH);
        var volumeRect = new RectangleF(left, chartRect.Bottom + gap, chartRect.Width, volH);

        // 00/60 主板：纵轴为相对昨收的涨跌幅（%），固定 ±10% 刻度；其余：纵轴为价格
        var usePctAxis = _useMainBoardPctAxis && _yesterdayClose > 0;
        decimal minA, maxA;
        if (usePctAxis)
        {
            minA = -10m;
            maxA = 10m;
        }
        else
        {
            (minA, maxA) = GetPriceBounds(_minuteData, _yesterdayClose);
        }

        float YAt(decimal price)
        {
            var axisVal = usePctAxis ? ChangePercentFromYesterday(price, _yesterdayClose) : price;
            return YFromLinearAxis(chartRect, axisVal, minA, maxA);
        }

        using var axisFont = new Font("微软雅黑", 8f);
        using var titleFont = new Font("微软雅黑", 8.5f, FontStyle.Bold);
        using var gridPen = new Pen(Color.FromArgb(45, 90, 90, 95), 1f);
        using var minorGridPen = new Pen(Color.FromArgb(28, 60, 60, 65), 1f);

        // 1) 价格区水平网格
        const int gridLines = 5;
        for (var i = 0; i <= gridLines; i++)
        {
            var y = chartRect.Top + chartRect.Height / gridLines * i;
            var pen = i == 0 || i == gridLines ? gridPen : minorGridPen;
            g.DrawLine(pen, chartRect.Left, y, chartRect.Right, y);
            var axisTick = maxA - (maxA - minA) / gridLines * i;
            var label = usePctAxis ? $"{axisTick:+0.0;-0.0}%" : axisTick.ToString("F2");
            var sz = g.MeasureString(label, axisFont);
            g.DrawString(label, axisFont, Brushes.DimGray, chartRect.Left - sz.Width - 4, y - sz.Height / 2);
        }

        // 2) 昨收、开盘参考线
        if (_yesterdayClose > 0)
        {
            var yy = YAt(_yesterdayClose);
            using var pen = new Pen(Color.FromArgb(140, 140, 140), 1f) { DashStyle = DashStyle.Dash };
            g.DrawLine(pen, chartRect.Left, yy, chartRect.Right, yy);
            var ycNote = usePctAxis ? $"昨收 {_yesterdayClose:F2} (0%)" : $"昨收 {_yesterdayClose:F2}";
            g.DrawString(ycNote, axisFont, Brushes.Gray, chartRect.Right - 110, yy - 14);
        }

        if (_minuteData.Count > 0)
        {
            var open = _minuteData[0].Open;
            var oy = YAt(open);
            using var pen = new Pen(Color.FromArgb(120, 100, 160, 230), 1f) { DashStyle = DashStyle.Dash };
            g.DrawLine(pen, chartRect.Left, oy, chartRect.Right, oy);
            var openNote = usePctAxis && _yesterdayClose > 0
                ? $"开盘 {open:F2} ({ChangePercentFromYesterday(open, _yesterdayClose):+0.00;-0.00}%)"
                : $"开盘 {open:F2}";
            g.DrawString(openNote, axisFont, Brushes.SteelBlue, chartRect.Right - 110, oy + 2);
        }

        // 3) 价格折线 + 昨收轴上填充
        var pts = new List<PointF>(_minuteData.Count);
        for (var i = 0; i < _minuteData.Count; i++)
        {
            var d = _minuteData[i];
            pts.Add(new PointF(XFromMinutes(chartRect, d.MinutesFromStart), YAt(d.Close)));
        }

        if (pts.Count > 1)
        {
            var up = _minuteData[^1].Close >= _minuteData[0].Open;
            var fillTop = _yesterdayClose > 0
                ? YAt(_yesterdayClose)
                : chartRect.Top + chartRect.Height * 0.5f;

            var poly = new List<PointF>(pts.Count + 2);
            poly.Add(new PointF(pts[0].X, fillTop));
            poly.AddRange(pts);
            poly.Add(new PointF(pts[^1].X, fillTop));

            using var fill = new SolidBrush(up ? Color.FromArgb(28, 220, 60, 60) : Color.FromArgb(28, 60, 200, 120));
            g.FillPolygon(fill, poly.ToArray());

            using var linePen = new Pen(up ? Color.FromArgb(230, 230, 80, 80) : Color.FromArgb(230, 80, 200, 120), 1.8f);
            g.DrawLines(linePen, pts.ToArray());
        }

        // 4) 累计均价 VWAP（黄虚线）
        if (_minuteData.Count > 1)
        {
            var avgPts = new List<PointF>(_minuteData.Count);
            for (var i = 0; i < _minuteData.Count; i++)
            {
                var d = _minuteData[i];
                avgPts.Add(new PointF(XFromMinutes(chartRect, d.MinutesFromStart), YAt(d.AvgPrice)));
            }

            using var avgPen = new Pen(Color.FromArgb(220, 230, 200, 80), 1.4f) { DashStyle = DashStyle.Dash };
            g.DrawLines(avgPen, avgPts.ToArray());
        }

        // 5) 量能
        var maxVol = _minuteData.Max(d => d.Volume);
        if (maxVol > 0 && volumeRect.Height > 4)
        {
            using var volBg = new SolidBrush(Color.FromArgb(22, 30, 30, 35));
            g.FillRectangle(volBg, volumeRect);

            using var sep = new Pen(Color.FromArgb(70, 80, 80, 88), 1f);
            g.DrawLine(sep, chartRect.Left, volumeRect.Top - 2, chartRect.Right, volumeRect.Top - 2);

            var n = _minuteData.Count;
            var barW = Math.Max(1f, volumeRect.Width / Math.Max(n, SessionMinuteMax) * 0.85f);

            for (var i = 0; i < n; i++)
            {
                var d = _minuteData[i];
                var cx = XFromMinutes(volumeRect, d.MinutesFromStart);
                var h = (float)(d.Volume / maxVol) * volumeRect.Height;
                var y = volumeRect.Bottom - h;
                var upBar = d.Close >= d.Open;
                using var b = new SolidBrush(upBar ? Color.FromArgb(160, 220, 70, 70) : Color.FromArgb(160, 70, 190, 110));
                g.FillRectangle(b, cx - barW / 2f, y, barW, h);
            }

            g.DrawString($"量 max {maxVol:0}", axisFont, Brushes.DimGray, volumeRect.Left, volumeRect.Top + 2);
        }

        // 6) 时间竖线（与横轴映射一致；同一 X 只画一根，避免 11:30/13:00 重合重复）
        using var timePen = new Pen(Color.FromArgb(100, 120, 125, 130), 1f);
        var fixedTimes = new[] { 930, 1000, 1030, 1100, 1130, 1300, 1330, 1400, 1430, 1500 };
        var timeLabels = new[] { "09:30", "10:00", "10:30", "11:00", "11:30", "13:00", "13:30", "14:00", "14:30", "15:00" };

        float? lastTickX = null;
        for (var i = 0; i < fixedTimes.Length; i++)
        {
            var ms = WallClockToMinutesFromStart(fixedTimes[i]);
            if (ms < _xMinuteLo || ms > _xMinuteHi) continue;
            var x = XFromMinutes(chartRect, ms);
            if (lastTickX.HasValue && Math.Abs(x - lastTickX.Value) < 1.5f)
                continue;
            lastTickX = x;
            g.DrawLine(timePen, x, chartRect.Top, x, volumeRect.Bottom);
            g.DrawString(timeLabels[i], axisFont, Brushes.LightGray, x - 16, volumeRect.Bottom + 4);
        }

        g.DrawLine(timePen, volumeRect.Left, volumeRect.Bottom + 2, volumeRect.Right, volumeRect.Bottom + 2);

        // 7) 最新价圆点
        if (pts.Count > 0)
        {
            var last = _minuteData[^1];
            var lx = XFromMinutes(chartRect, last.MinutesFromStart);
            var ly = YAt(last.Close);
            var upDot = last.Close >= _minuteData[0].Open;
            using var dot = new SolidBrush(upDot ? Color.FromArgb(255, 240, 90, 90) : Color.FromArgb(255, 90, 220, 120));
            g.FillEllipse(dot, lx - 4, ly - 4, 8, 8);
            var dotLabel = usePctAxis && _yesterdayClose > 0
                ? $"{last.Close:F2} ({ChangePercentFromYesterday(last.Close, _yesterdayClose):+0.00;-0.00}%)"
                : last.Close.ToString("F2");
            g.DrawString(dotLabel, titleFont, dot, lx - 48, ly - 22);
        }
    }

    /// <summary>墙上时钟 HHmm → 从 9:30 起的连续分钟序号（午休压缩在 120 处对接下午）。</summary>
    private static int WallClockToMinutesFromStart(int hhmm)
    {
        var hm = hhmm / 100 * 60 + hhmm % 100;
        if (hhmm <= 1130)
            return hm - (9 * 60 + 30);
        return 120 + (hm - (13 * 60));
    }
}
