using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core.DeepSeek;

namespace StockAnalysisSystem.UI.Forms;

public partial class DeepSeekMarketAnalysisForm : Form
{
    private readonly DeepSeekClient _deepSeekClient;

    private DataGridView _dgvMarketAnalysis = null!;
    private Label _lblStatus = null!;
    private RichTextBox _txtResult = null!;
    private Button _btnAnalyze = null!;
    private string _lastResponse = ""; // 保存最后一次API响应

    public DeepSeekMarketAnalysisForm(DeepSeekClient deepSeekClient)
    {
        _deepSeekClient = deepSeekClient;
        InitializeComponent();
        InitializeControls();
    }

    private void InitializeComponent()
    {
        Text = "DeepSeek 市场分析";
        Size = new Size(1000, 600);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(800, 500);
        AutoScaleMode = AutoScaleMode.Font;
    }

    private void InitializeControls()
    {
        // 顶部面板：操作区域
        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 80,
            Padding = new Padding(10)
        };

        var lblTitle = new Label
        {
            Text = "AI 市场热点分析",
            Location = new Point(10, 10),
            AutoSize = true,
            Font = new Font("SimSun", 10F, FontStyle.Bold)
        };

        var lblDesc = new Label
        {
            Text = "DeepSeek AI 将基于当前市场情况，智能分析并推荐上涨概率较高的板块",
            Location = new Point(10, 35),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("SimSun", 9F)
        };

        _btnAnalyze = new Button
        {
            Text = "开始 AI 分析",
            Location = new Point(750, 20),
            Size = new Size(140, 40),
            BackColor = Color.FromArgb(64, 169, 255),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("SimSun", 10F, FontStyle.Bold)
        };
        _btnAnalyze.Click += async (s, e) => await AnalyzeMarketAsync();
        _btnAnalyze.MouseLeave += (s, e) => _btnAnalyze.BackColor = Color.FromArgb(64, 169, 255);
        _btnAnalyze.MouseEnter += (s, e) => _btnAnalyze.BackColor = Color.FromArgb(51, 153, 255);

        var btnDebug = new Button
        {
            Text = "调试信息",
            Location = new Point(900, 20),
            Size = new Size(90, 40),
            BackColor = Color.FromArgb(100, 100, 100),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("SimSun", 9F)
        };
        btnDebug.Click += (s, e) => ShowDebugInfo();
        btnDebug.MouseLeave += (s, e) => btnDebug.BackColor = Color.FromArgb(100, 100, 100);
        btnDebug.MouseEnter += (s, e) => btnDebug.BackColor = Color.FromArgb(80, 80, 80);

        _lblStatus = new Label
        {
            Text = "就绪 - 点击按钮开始分析",
            Location = new Point(10, 60),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("SimSun", 9F)
        };

        topPanel.Controls.AddRange(new Control[] { lblTitle, lblDesc, _btnAnalyze, btnDebug, _lblStatus });

        // 中部：表格区域
        var tablePanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        var lblTable = new Label
        {
            Text = "AI 推荐板块:",
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font("SimSun", 10F, FontStyle.Bold),
            Padding = new Padding(10, 5, 0, 0)
        };

        _dgvMarketAnalysis = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            ColumnHeadersVisible = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("SimSun", 9F),
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _dgvMarketAnalysis.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _dgvMarketAnalysis.EnableHeadersVisualStyles = false;
        _dgvMarketAnalysis.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
        _dgvMarketAnalysis.ColumnHeadersDefaultCellStyle.Font = new Font("SimSun", 9F, FontStyle.Bold);

        _dgvMarketAnalysis.Columns.Add(new DataGridViewTextBoxColumn { Name = "PlateName", HeaderText = "板块名称", Width = 200 });
        _dgvMarketAnalysis.Columns.Add(new DataGridViewTextBoxColumn { Name = "Reason", HeaderText = "推荐理由", Width = 500 });
        _dgvMarketAnalysis.Columns.Add(new DataGridViewTextBoxColumn { Name = "Confidence", HeaderText = "信心度", Width = 150 });

        tablePanel.Controls.AddRange(new Control[] { lblTable, _dgvMarketAnalysis });

        // 底部：详细结果区域
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        var lblResult = new Label
        {
            Text = "详细分析:",
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font("SimSun", 10F, FontStyle.Bold),
            Padding = new Padding(10, 5, 0, 0)
        };

        _txtResult = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Font = new Font("SimSun", 9F),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        bottomPanel.Controls.AddRange(new Control[] { lblResult, _txtResult });

        // 使用 TableLayoutPanel 替代 SplitContainer
        var tableLayoutPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };

        tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));  // 上半部分 50%
        tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));  // 下半部分 50%

        tableLayoutPanel.Controls.Add(tablePanel, 0, 0);
        tableLayoutPanel.Controls.Add(bottomPanel, 0, 1);

        Controls.AddRange(new Control[] { topPanel, tableLayoutPanel });
    }

    /// <summary>
    /// 显示调试信息
    /// </summary>
    private void ShowDebugInfo()
    {
        var debugInfo = $"调试信息\n\n";
        debugInfo += $"当前时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";
        debugInfo += $"API响应状态: {(!string.IsNullOrEmpty(_lastResponse) ? "有响应" : "无响应")}\n\n";

        if (!string.IsNullOrEmpty(_lastResponse))
        {
            debugInfo += "DeepSeek API 原始响应:\n";
            debugInfo += "=================================\n";
            debugInfo += _lastResponse;
            debugInfo += "\n=================================\n\n";

            // 尝试提取JSON
            var jsonStart = _lastResponse.IndexOf('{');
            var jsonEnd = _lastResponse.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                debugInfo += "提取的JSON:\n";
                debugInfo += _lastResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }
            else
            {
                debugInfo += "未找到有效的JSON结构\n";
                debugInfo += $"JSON起始位置: {jsonStart}\n";
                debugInfo += $"JSON结束位置: {jsonEnd}";
            }
        }
        else
        {
            debugInfo += "未获取到API响应\n\n";
            debugInfo += "可能的原因：\n";
            debugInfo += "1. API密钥未配置\n";
            debugInfo += "2. 网络连接问题\n";
            debugInfo += "3. API服务异常\n";
            debugInfo += "4. 请求超时";
        }

        MessageBox.Show(debugInfo, "调试信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// DeepSeek 市场分析
    /// </summary>
    private async Task AnalyzeMarketAsync()
    {
        try
        {
            // 确保控件已初始化
            if (_lblStatus == null || _txtResult == null || _dgvMarketAnalysis == null || _btnAnalyze == null)
            {
                return;
            }

            _lblStatus.Text = "DeepSeek AI 正在分析市场...";
            _lblStatus.ForeColor = Color.Blue;
            _txtResult.Clear();
            _dgvMarketAnalysis.Rows.Clear();
            _btnAnalyze.Enabled = false;
            _lastResponse = "";

            var result = await _deepSeekClient.AnalyzeMarketAsync("");

            if (result == null)
            {
                _txtResult.SelectionColor = Color.Red;
                _txtResult.SelectionFont = new Font("SimSun", 10F, FontStyle.Bold);
                _txtResult.AppendText("分析失败\n\n");
                _txtResult.SelectionColor = Color.Black;
                _txtResult.SelectionFont = new Font("SimSun", 9F);
                _txtResult.AppendText("可能的原因：\n");
                _txtResult.AppendText("1. API密钥未配置\n");
                _txtResult.AppendText("2. 网络连接问题\n");
                _txtResult.AppendText("3. API服务异常\n\n");
                _txtResult.AppendText("请点击'调试信息'按钮查看详细错误。");

                _lblStatus.Text = "分析失败 - 点击调试信息查看详情";
                _lblStatus.ForeColor = Color.Red;
                _btnAnalyze.Enabled = true;
                return;
            }

            // 显示市场趋势
            var trendText = $"市场趋势: {result.MarketTrend}";
            _txtResult.SelectionColor = Color.Black;
            _txtResult.SelectionFont = new Font("SimSun", 9F, FontStyle.Bold);
            _txtResult.AppendText(trendText + Environment.NewLine + Environment.NewLine);

            // 显示风险提示
            if (result.Risks.Count > 0)
            {
                _txtResult.SelectionColor = Color.Red;
                _txtResult.SelectionFont = new Font("SimSun", 9F, FontStyle.Bold);
                _txtResult.AppendText("风险提示:" + Environment.NewLine);
                _txtResult.SelectionColor = Color.Black;
                _txtResult.SelectionFont = new Font("SimSun", 9F);
                foreach (var risk in result.Risks)
                {
                    _txtResult.AppendText($"  • {risk}" + Environment.NewLine);
                }
                _txtResult.AppendText(Environment.NewLine);
            }

            // 显示推荐板块
            _txtResult.SelectionColor = Color.Green;
            _txtResult.SelectionFont = new Font("SimSun", 9F, FontStyle.Bold);
            _txtResult.AppendText($"AI 推荐板块 ({result.RecommendedPlates.Count}个):" + Environment.NewLine);
            _txtResult.SelectionColor = Color.Black;
            _txtResult.SelectionFont = new Font("SimSun", 9F);

            foreach (var plate in result.RecommendedPlates)
            {
                _txtResult.AppendText($"  ★ {plate.PlateName}" + Environment.NewLine);
                _txtResult.AppendText($"    理由: {plate.Reason}" + Environment.NewLine);
                _txtResult.AppendText($"    信心度: {plate.Confidence:P2}" + Environment.NewLine);
                if (plate.Stocks.Count > 0)
                {
                    _txtResult.AppendText($"    代表股票: {string.Join(", ", plate.Stocks.Take(3))}" + Environment.NewLine);
                }
                _txtResult.AppendText(Environment.NewLine);
            }

            // 填充表格
            foreach (var plate in result.RecommendedPlates)
            {
                var rowIndex = _dgvMarketAnalysis.Rows.Add(
                    plate.PlateName,
                    plate.Reason,
                    plate.Confidence.ToString("P2")
                );

                var cell = _dgvMarketAnalysis.Rows[rowIndex].Cells["Confidence"];
                cell.Style.BackColor = plate.Confidence > 0.7m ? Color.LightGreen :
                                        plate.Confidence > 0.4m ? Color.LightYellow : Color.LightPink;
            }

            _lblStatus.Text = $"AI 分析完成: 推荐了 {result.RecommendedPlates.Count} 个板块";
            _lblStatus.ForeColor = Color.Green;
            _btnAnalyze.Enabled = true;
        }
        catch (Exception ex)
        {
            if (_lblStatus != null)
            {
                _lblStatus.Text = "分析失败";
                _lblStatus.ForeColor = Color.Red;
            }
            Core.Utils.ErrorLogger.Log(ex, "DeepSeekMarketAnalysisForm.AnalyzeMarket", "");
            MessageBox.Show($"分析失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (_btnAnalyze != null)
            {
                _btnAnalyze.Enabled = true;
            }
        }
    }
}
