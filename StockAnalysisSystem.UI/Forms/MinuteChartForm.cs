using System.Drawing.Drawing2D;
using StockAnalysisSystem.Core.RealtimeData;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.UI.Forms;

public partial class MinuteChartForm : Form
{
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
    private decimal _yesterdayClose = 0;  // 昨收价，用于计算涨跌
    private decimal _dayChangePercent = 0;  // 当日涨跌幅（来自实时数据）

    public MinuteChartForm(string stockCode, string stockName, decimal currentPrice, decimal changePercent)
    {
        _chartService = new SinaMinuteChartService();
        _stockCode = NormalizeCode(stockCode);
        _stockName = stockName;

        // 保存当日涨跌幅（来自实时数据）
        _dayChangePercent = changePercent;

        // 根据代码判断市场
        _market = _stockCode.StartsWith("60") || _stockCode.StartsWith("68") ? "SH" : "SZ";

        // 计算昨收价
        if (currentPrice != 0 && changePercent != 0)
        {
            _yesterdayClose = currentPrice / (1 + changePercent / 100);
        }

        InitializeComponent();
        LoadDataAsync();
    }

    private string NormalizeCode(string code)
    {
        return code.Replace("sz", "").Replace("sh", "").Trim();
    }

    private void InitializeComponent()
    {
        Text = $"{_stockName} ({_stockCode}) 分时图";
        Size = new Size(900, 600);
        StartPosition = FormStartPosition.CenterParent;

        // 标题栏
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

        // 切换周期
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
        _btnRefresh.Click += (s, e) => LoadDataAsync();

        _lblStatus = new Label
        {
            Text = "加载中...",
            Left = 650,
            Top = 48,
            Width = 200,
            ForeColor = Color.Gray
        };

        topPanel.Controls.AddRange(new Control[] { _lblTitle, _lblPrice, _lblChange, lblScale, _cbScale, _btnRefresh, _lblStatus });

        // 图表面板
        _chartPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            Padding = new Padding(40, 20, 20, 40)
        };
        _chartPanel.Paint += ChartPanel_Paint;

        // 底部信息
        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.FromArgb(30, 30, 30) };
        var lblInfo = new Label
        {
            Text = "数据来源: 新浪财经",
            Dock = DockStyle.Fill,
            ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0)
        };
        bottomPanel.Controls.Add(lblInfo);

        Controls.Add(_chartPanel);
        Controls.Add(bottomPanel);
        Controls.Add(topPanel);
    }

    private void CbScale_SelectedIndexChanged(object? sender, EventArgs e)
    {
        LoadDataAsync();
    }

    private int GetSelectedScale()
    {
        return _cbScale.SelectedIndex switch
        {
            0 => 1,
            1 => 5,
            2 => 15,
            3 => 30,
            4 => 60,
            _ => 1
        };
    }

    private async void LoadDataAsync()
    {
        try
        {
            _lblStatus.Text = "加载中...";
            _btnRefresh.Enabled = false;

            var scale = GetSelectedScale();
            var (data, error) = await _chartService.GetMinuteChartDataAsync(_stockCode, _market, scale, null);

            if (!string.IsNullOrEmpty(error))
            {
                _lblStatus.Text = error;
                _chartPanel.Invalidate();
                _btnRefresh.Enabled = true;
                return;
            }

            if (data.Count == 0)
            {
                _lblStatus.Text = "暂无数据";
                _chartPanel.Invalidate();
                _btnRefresh.Enabled = true;
                return;
            }

            _minuteData = data;

            // 更新价格显示
            // 使用当日总体涨跌幅（来自实时数据），而不是分时图的最后一个数据点
            var latest = _minuteData.Last();
            var changePercent = _dayChangePercent;
            var change = _yesterdayClose > 0 ? latest.Close - _yesterdayClose : latest.Change;

            _lblPrice.Text = $"当前价: {latest.Close:F2}";
            _lblChange.Text = $"涨跌幅: {changePercent:+0.00;-0.00}%";
            _lblChange.ForeColor = changePercent >= 0 ? Color.Red : Color.Lime;

            _lblStatus.Text = $"共 {_minuteData.Count} 条数据";
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

    private void ChartPanel_Paint(object? sender, PaintEventArgs e)
    {
        if (_minuteData.Count == 0) return;

        var g = e.Graphics;
        var rect = _chartPanel.ClientRectangle;

        // 设置坐标区域
        // 左边60像素给价格标签，底部30像素给时间标签
        // 顶部区域给价格图，底部1/4区域给量能图
        var totalHeight = rect.Height - 60;
        var priceChartHeight = (float)(totalHeight * 0.72);  // 价格图占72%
        var volumeChartHeight = (float)(totalHeight * 0.22); // 量能图占22%
        var chartRect = new RectangleF(rect.Left + 60, rect.Top + 10, rect.Width - 80, priceChartHeight);
        var volumeRect = new RectangleF(rect.Left + 60, chartRect.Bottom + 10, rect.Width - 80, volumeChartHeight);

        // 计算价格范围
        var prices = _minuteData.Select(d => d.High).Concat(_minuteData.Select(d => d.Low)).ToList();
        var minPrice = prices.Min();
        var maxPrice = prices.Max();

        // 扩展价格范围
        var priceRange = maxPrice - minPrice;
        if (priceRange == 0) priceRange = minPrice * 0.02m;
        minPrice -= priceRange * 0.1m;
        maxPrice += priceRange * 0.1m;

        // 昨收价线
        var yesterdayCloseY = _yesterdayClose > 0
            ? chartRect.Bottom - (float)((_yesterdayClose - minPrice) / (maxPrice - minPrice)) * chartRect.Height
            : chartRect.Bottom - chartRect.Height / 2;

        // 绘制昨收价线（灰色虚线）
        if (_yesterdayClose > 0)
        {
            using var pen = new Pen(Color.FromArgb(100, 128, 128, 128)) { DashStyle = DashStyle.Dash };
            g.DrawLine(pen, chartRect.Left, yesterdayCloseY, chartRect.Right, yesterdayCloseY);

            // 昨收价标签
            using var brush = new SolidBrush(Color.Gray);
            g.DrawString($"昨收 {_yesterdayClose:F2}", new Font("Arial", 8), brush, chartRect.Right - 70, yesterdayCloseY - 10);
        }

        // 绘制分时线
        if (_minuteData.Count > 1)
        {
            using var linePen = new Pen(Color.FromArgb(255, 238, 105, 88), 2);  // 上涨红色
            using var fillBrush = new SolidBrush(Color.FromArgb(50, 255, 0, 0));

            var points = new List<PointF>();
            var fillPoints = new List<PointF>();

            for (int i = 0; i < _minuteData.Count; i++)
            {
                var x = chartRect.Left + (float)_minuteData[i].MinutesFromStart / 240 * chartRect.Width;
                var y = chartRect.Bottom - (float)((_minuteData[i].Close - minPrice) / (maxPrice - minPrice)) * chartRect.Height;
                points.Add(new PointF(x, y));
                fillPoints.Add(new PointF(x, y));
            }

            // 填充区域
            if (points.Count > 0)
            {
                fillPoints.Insert(0, new PointF(points[0].X, chartRect.Bottom));
                fillPoints.Add(new PointF(points[points.Count - 1].X, chartRect.Bottom));

                var isUp = _minuteData.Last().Close >= _minuteData.First().Open;
                fillBrush.Color = isUp ? Color.FromArgb(30, 255, 50, 50) : Color.FromArgb(30, 50, 255, 50);
                linePen.Color = isUp ? Color.Red : Color.Lime;

                g.FillPolygon(fillBrush, fillPoints.ToArray());
                g.DrawLines(linePen, points.ToArray());
            }

            // 绘制整日均价线（黄色）
            using var avgPen = new Pen(Color.Yellow, 2) { DashStyle = DashStyle.Dash };
            var avgPoints = new List<PointF>();
            for (int i = 0; i < _minuteData.Count; i++)
            {
                var x = chartRect.Left + (float)_minuteData[i].MinutesFromStart / 240 * chartRect.Width;
                var avgY = chartRect.Bottom - (float)((_minuteData[i].AvgPrice - minPrice) / (maxPrice - minPrice)) * chartRect.Height;
                avgPoints.Add(new PointF(x, avgY));
            }
            if (avgPoints.Count > 1)
            {
                g.DrawLines(avgPen, avgPoints.ToArray());
            }
        }

        // 绘制零轴线（今日开盘价）
        if (_minuteData.Count > 0)
        {
            var openPrice = _minuteData.First().Open;
            var zeroY = chartRect.Bottom - (float)((openPrice - minPrice) / (maxPrice - minPrice)) * chartRect.Height;

            using var zeroPen = new Pen(Color.Blue, 1) { DashStyle = DashStyle.Dash };
            g.DrawLine(zeroPen, chartRect.Left, zeroY, chartRect.Right, zeroY);

            using var zeroBrush = new SolidBrush(Color.Blue);
            g.DrawString($"开盘 {openPrice:F2}", new Font("Arial", 8), zeroBrush, chartRect.Right - 80, zeroY - 10);
        }

        // 绘制价格网格和标签
        using var gridPen = new Pen(Color.FromArgb(40, 255, 255, 255));
        using var labelBrush = new SolidBrush(Color.Gray);

        var gridLines = 5;
        for (int i = 0; i <= gridLines; i++)
        {
            var y = chartRect.Top + chartRect.Height / gridLines * i;
            var price = maxPrice - (maxPrice - minPrice) / gridLines * i;

            g.DrawLine(gridPen, chartRect.Left, y, chartRect.Right, y);
            g.DrawString(price.ToString("F2"), new Font("Arial", 8), labelBrush, 2, y - 6);
        }

        // 绘制量能图
        var maxVolume = _minuteData.Max(d => d.Volume);
        if (maxVolume > 0)
        {
            // 绘制量能区域背景
            using var bgBrush = new SolidBrush(Color.FromArgb(20, 255, 255, 255));
            g.FillRectangle(bgBrush, volumeRect);

            // 绘制量能分隔线
            using var volumeSepPen = new Pen(Color.FromArgb(80, 255, 255, 255));
            g.DrawLine(volumeSepPen, chartRect.Left, volumeRect.Top - 5, chartRect.Right, volumeRect.Top - 5);

            // 绘制量能柱状图
            for (int i = 0; i < _minuteData.Count; i++)
            {
                var x = volumeRect.Left + (float)_minuteData[i].MinutesFromStart / 240 * volumeRect.Width;
                var barWidth = Math.Max(2, volumeRect.Width / _minuteData.Count - 1);
                var volumeHeight = (float)(_minuteData[i].Volume / maxVolume) * volumeRect.Height;
                var y = volumeRect.Bottom - volumeHeight;

                // 根据涨跌设置颜色
                var barColor = _minuteData[i].Close >= _minuteData[i].Open
                    ? Color.FromArgb(180, 255, 50, 50)   // 涨红色
                    : Color.FromArgb(180, 50, 255, 50);  // 跌绿色
                using var barBrush = new SolidBrush(barColor);
                g.FillRectangle(barBrush, x - barWidth / 2, y, barWidth, volumeHeight);
            }

            // 绘制量能均量线
            var avgVolume = (float)maxVolume * 0.3f;
            var avgVolumeY = volumeRect.Bottom - avgVolume;
            using var avgVolPen = new Pen(Color.Yellow, 1) { DashStyle = DashStyle.Dash };
            g.DrawLine(avgVolPen, volumeRect.Left, avgVolumeY, volumeRect.Right, avgVolumeY);

            // 绘制量能标签
            using var volLabelBrush = new SolidBrush(Color.Gray);
            g.DrawString($"量能 {maxVolume / 10000:F0}万", new Font("Arial", 8), volLabelBrush, 2, volumeRect.Top + 2);
        }

        // 绘制时间轴（横轴）- 固定时间刻度 9:30 - 15:00
        using var timePen = new Pen(Color.FromArgb(150, 255, 255, 255), 1);
        using var timeBrush = new SolidBrush(Color.White);

        // 固定时间点：9:30, 10:00, 10:30, 11:00, 13:00, 13:30, 14:00, 14:30, 15:00 (11:30和13:00合并)
        var fixedTimes = new[] { 930, 1000, 1030, 1100, 1300, 1330, 1400, 1430, 1500 };
        var timeLabels = new[] { "09:30", "10:00", "10:30", "11:00", "13:00", "13:30", "14:00", "14:30", "15:00" };

        // 交易时段总分钟数 = 上午120分钟 + 下午120分钟 = 240分钟
        const int totalTradingMinutes = 240;

        for (int i = 0; i < fixedTimes.Length; i++)
        {
            var time = fixedTimes[i];
            int minutesFromStart;

            // 把时间格式 (如 930, 1000, 1030) 转换成从午夜开始的分钟数
            // 例如: 930 -> 9*60+30=570, 1000 -> 10*60+0=600
            int timeInMinutes = (time / 100) * 60 + (time % 100);

            if (time < 1300)
            {
                // 上午时段 (9:30 - 11:30)
                minutesFromStart = timeInMinutes - 570;
            }
            else
            {
                // 下午时段 (13:00 - 15:00)
                // 下午时间 = 120(上午) + (当前分钟 - 780)，合并午休
                // 13:00 -> 120 + 0 = 120 (与11:30位置相同，合并)
                // 13:30 -> 120 + 30 = 150
                // 15:00 -> 120 + 120 = 240
                minutesFromStart = 120 + (timeInMinutes - 780);
            }

            // 计算x坐标
            var x = chartRect.Left + (float)minutesFromStart / totalTradingMinutes * chartRect.Width;

            // 绘制垂直刻度线（穿过价格图和量能图）
            g.DrawLine(timePen, x, chartRect.Top - 5, x, volumeRect.Bottom + 15);

            // 绘制时间标签（放在量能图下方）
            g.DrawString(timeLabels[i], new Font("Arial", 8), timeBrush, x - 15, volumeRect.Bottom + 2);
        }

        // 绘制底部时间轴线
        g.DrawLine(timePen, volumeRect.Left, volumeRect.Bottom + 12, volumeRect.Right, volumeRect.Bottom + 12);

        // 绘制最新价格
        var latestData = _minuteData.Last();
        var lastX = chartRect.Left + (float)latestData.MinutesFromStart / 240 * chartRect.Width;
        var lastY = chartRect.Bottom - (float)((latestData.Close - minPrice) / (maxPrice - minPrice)) * chartRect.Height;

        // 最新价圆点
        using var dotBrush = new SolidBrush(latestData.Close >= latestData.Open ? Color.Red : Color.Lime);
        g.FillEllipse(dotBrush, lastX - 4, lastY - 4, 8, 8);

        // 最新价标签
        var priceLabel = latestData.Close.ToString("F2");
        g.DrawString(priceLabel, new Font("Arial", 9, FontStyle.Bold), dotBrush, lastX - 25, lastY - 20);
    }
}
