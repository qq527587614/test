using System.Drawing;
using StockAnalysisSystem.Core.Entities;

namespace StockAnalysisSystem.UI.Forms;

public class AnalysisForm : Form
{
    private readonly StockAnalysisResult _result;

    public AnalysisForm(StockAnalysisResult result)
    {
        _result = result;
        InitializeComponent();
        DisplayResult();
    }

    private void InitializeComponent()
    {
        Text = $"股票分析 - {_result.StockName} ({_result.StockCode})";
        Size = new Size(520, 700);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(20)
        };

        Controls.Add(scrollPanel);
    }

    private void DisplayResult()
    {
        var panel = Controls[0] as Panel;
        if (panel == null) return;

        int y = 10;

        // 标题
        var titleLabel = new Label
        {
            Text = $"{_result.StockName} ({_result.StockCode})",
            Font = new Font("微软雅黑", 16, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(20, y),
            AutoSize = true
        };
        panel.Controls.Add(titleLabel);
        y += 45;

        // 综合评分
        var scorePanel = new Panel
        {
            Size = new Size(440, 120),
            Location = new Point(20, y),
            BackColor = GetScoreColor(_result.Score)
        };

        var scoreLabel = new Label
        {
            Text = _result.Score.ToString(),
            Font = new Font("Arial", 40, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(20, 10),
            AutoSize = true
        };
        scorePanel.Controls.Add(scoreLabel);

        var recommendationLabel = new Label
        {
            Text = _result.GetRecommendationText(),
            Font = new Font("微软雅黑", 18),
            ForeColor = Color.White,
            Location = new Point(100, 25),
            AutoSize = true
        };
        scorePanel.Controls.Add(recommendationLabel);

        var descLabel = new Label
        {
            Text = _result.GetScoreDescription(),
            Font = new Font("微软雅黑", 12),
            ForeColor = Color.FromArgb(220, 220, 220),
            Location = new Point(20, 70),
            AutoSize = true
        };
        scorePanel.Controls.Add(descLabel);

        panel.Controls.Add(scorePanel);
        y += 140;

        // 股票信息
        y = AddSection(panel, y, "基本信息");
        AddInfoRow(panel, ref y, "市场", $"{_result.Market}");
        AddInfoRow(panel, ref y, "最新价", _result.ClosePrice.HasValue ? $"{_result.ClosePrice:F2}" : "--");
        var changeStr = _result.ChangePercent.HasValue
            ? $"{_result.ChangePercent:+0.00;-0.00}%"
            : "--";
        AddInfoRow(panel, ref y, "涨跌幅", changeStr);
        y += 10;

        // 技术指标
        y = AddSection(panel, y, "技术指标");
        AddIndicatorRow(panel, ref y, "MA均线", $"MA5: {_result.Ma5:F2}  MA10: {_result.Ma10:F2}  MA20: {_result.Ma20:F2}", _result.MaScore);
        AddIndicatorRow(panel, ref y, "MACD", $"DIF: {_result.MacdDif:F2}  DEA: {_result.MacdDea:F2}  MACD: {_result.MacdValue:F2}", _result.MacdScore);
        AddIndicatorRow(panel, ref y, "RSI", $"RSI6: {_result.Rsi6:F2}  RSI12: {_result.Rsi12:F2}", _result.RsiScore);
        AddIndicatorRow(panel, ref y, "KDJ", $"K: {_result.K:F2}  D: {_result.D:F2}  J: {_result.J:F2}", _result.KdjScore);
        AddIndicatorRow(panel, ref y, "BOLL", $"上轨: {_result.BollUpper:F2}  中轨: {_result.BollMiddle:F2}  下轨: {_result.BollLower:F2}", _result.BollScore);
        AddIndicatorRow(panel, ref y, "成交量", $"成交量: {_result.Volume / 10000:F0}万  均量: {_result.VolumeMa / 10000:F0}万", _result.VolumeScore);
        y += 10;

        // 信号详情
        y = AddSection(panel, y, "信号详情");
        var signalLabel = new Label
        {
            Text = _result.SignalDetails,
            Font = new Font("微软雅黑", 10),
            ForeColor = Color.FromArgb(200, 200, 200),
            Location = new Point(20, y),
            Size = new Size(440, 80),
            AutoSize = true
        };
        panel.Controls.Add(signalLabel);
        y += 90;

        // 评分明细
        y = AddSection(panel, y, "评分明细");
        AddScoreRow(panel, ref y, "MA均线", _result.MaScore, 20);
        AddScoreRow(panel, ref y, "MACD", _result.MacdScore, 25);
        AddScoreRow(panel, ref y, "RSI", _result.RsiScore, 20);
        AddScoreRow(panel, ref y, "KDJ", _result.KdjScore, 15);
        AddScoreRow(panel, ref y, "BOLL", _result.BollScore, 10);
        AddScoreRow(panel, ref y, "成交量", _result.VolumeScore, 10);
        y += 10;

        // 分析时间
        var timeLabel = new Label
        {
            Text = $"分析时间: {_result:AnalysisTime:yyyy-MM-dd HH:mm:ss}",
            Font = new Font("Arial", 9),
            ForeColor = Color.Gray,
            Location = new Point(20, y),
            AutoSize = true
        };
        panel.Controls.Add(timeLabel);
    }

    private int AddSection(Panel panel, int y, string title)
    {
        var label = new Label
        {
            Text = title,
            Font = new Font("微软雅黑", 12, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 180, 255),
            Location = new Point(20, y),
            AutoSize = true
        };
        panel.Controls.Add(label);
        return y + 30;
    }

    private void AddInfoRow(Panel panel, ref int y, string label, string value)
    {
        var labelCtrl = new Label
        {
            Text = label + ":",
            Font = new Font("微软雅黑", 10),
            ForeColor = Color.Gray,
            Location = new Point(20, y),
            Size = new Size(80, 20)
        };
        panel.Controls.Add(labelCtrl);

        var valueCtrl = new Label
        {
            Text = value,
            Font = new Font("微软雅黑", 10),
            ForeColor = Color.White,
            Location = new Point(100, y),
            Size = new Size(200, 20)
        };
        panel.Controls.Add(valueCtrl);

        y += 25;
    }

    private void AddIndicatorRow(Panel panel, ref int y, string name, string value, int score)
    {
        var labelCtrl = new Label
        {
            Text = name,
            Font = new Font("微软雅黑", 9),
            ForeColor = Color.FromArgb(180, 180, 180),
            Location = new Point(20, y),
            Size = new Size(60, 20)
        };
        panel.Controls.Add(labelCtrl);

        var valueCtrl = new Label
        {
            Text = value,
            Font = new Font("Arial", 9),
            ForeColor = Color.White,
            Location = new Point(80, y),
            Size = new Size(280, 20)
        };
        panel.Controls.Add(valueCtrl);

        var scoreCtrl = new Label
        {
            Text = $"{score}分",
            Font = new Font("Arial", 9, FontStyle.Bold),
            ForeColor = GetScoreColor(score * 5),
            Location = new Point(370, y),
            Size = new Size(50, 20),
            TextAlign = ContentAlignment.MiddleRight
        };
        panel.Controls.Add(scoreCtrl);

        y += 22;
    }

    private void AddScoreRow(Panel panel, ref int y, string name, int score, int maxScore)
    {
        var labelCtrl = new Label
        {
            Text = name,
            Font = new Font("微软雅黑", 9),
            ForeColor = Color.FromArgb(180, 180, 180),
            Location = new Point(20, y),
            Size = new Size(80, 20)
        };
        panel.Controls.Add(labelCtrl);

        var progressBar = new ProgressBar
        {
            Location = new Point(100, y),
            Size = new Size(250, 15),
            Minimum = 0,
            Maximum = maxScore,
            Value = score,
            Style = ProgressBarStyle.Continuous,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = GetScoreColor(score * 100 / maxScore)
        };
        panel.Controls.Add(progressBar);

        var scoreLabel = new Label
        {
            Text = $"{score}/{maxScore}",
            Font = new Font("Arial", 9),
            ForeColor = Color.White,
            Location = new Point(360, y),
            Size = new Size(50, 20)
        };
        panel.Controls.Add(scoreLabel);

        y += 25;
    }

    private Color GetScoreColor(int score)
    {
        return score switch
        {
            >= 80 => Color.FromArgb(0, 150, 80),     // 绿色 - 强烈买入
            >= 60 => Color.FromArgb(80, 160, 0),    // 浅绿 - 建议买入
            >= 40 => Color.FromArgb(180, 150, 0),   // 黄色 - 观望
            >= 20 => Color.FromArgb(200, 100, 0),   // 橙色 - 建议卖出
            _ => Color.FromArgb(200, 50, 50)        // 红色 - 强烈卖出
        };
    }
}
