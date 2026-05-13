using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core;
using StockAnalysisSystem.Core.DeepSeek;
using StockAnalysisSystem.Core.RealtimeData;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.UI.Forms;

/// <summary>
/// AI 选股：将本地库摘要与您的要求一并发给 DeepSeek，由模型给出思路与标的参考（非投资建议）。
/// </summary>
public sealed class AiStockPickForm : Form
{
    private readonly IServiceProvider _sp;

    private DateTimePicker _dtpRef = null!;
    private CheckBox _chkFavorites = null!;
    private Button _btnSummary = null!;
    private Button _btnAsk = null!;
    private TextBox _txtSummary = null!;
    private TextBox _txtGoal = null!;
    private TextBox _txtExtra = null!;
    private RichTextBox _txtReply = null!;
    private Label _lblStatus = null!;

    public AiStockPickForm(IServiceProvider sp)
    {
        _sp = sp;
        Text = "AI选股";
        InitializeUi();
    }

    private void InitializeUi()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(10);

        var intro = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 110,
            Text =
                "【数据与隐私说明】\n" +
                "· 本页不会把您的硬盘文件自动上传到「第三方网盘」。点击「生成数据库摘要」时，程序只从本机已连接的数据库读取统计与列表，并把您确认后的文字一并作为本次对话发给 DeepSeek（与 appsettings 中配置的 API 地址）。\n" +
                "· 更好利用 AI 的方式：先同步日线/涨停等数据，再生成摘要；在「选股目标」里写清风格（短线/波段、市值、是否回避题材等）；在「补充说明」粘贴您允许的少量结构化信息（如自选股备注、公告要点）。\n" +
                "· 模型输出为参考，不构成投资建议；请勿输入账户密码、身份证等敏感信息。"
        };

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        _dtpRef = new DateTimePicker { Width = 120, Format = DateTimePickerFormat.Short };
        _chkFavorites = new CheckBox { Text = "摘要中带自选股代码", AutoSize = true, Margin = new Padding(12, 6, 0, 0) };
        _btnSummary = new Button { Text = "生成数据库摘要", AutoSize = true, Margin = new Padding(12, 2, 0, 0) };
        _btnSummary.Click += async (_, _) => await LoadSummaryAsync();
        _btnAsk = new Button { Text = "请求 AI 选股", AutoSize = true, Margin = new Padding(12, 2, 0, 0) };
        _btnAsk.Click += async (_, _) => await AskAiAsync();

        bar.Controls.Add(new Label { Text = "参考交易日", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        bar.Controls.Add(_dtpRef);
        bar.Controls.Add(_chkFavorites);
        bar.Controls.Add(_btnSummary);
        bar.Controls.Add(_btnAsk);

        _lblStatus = new Label { Dock = DockStyle.Top, Height = 22, Text = "就绪", ForeColor = Color.DimGray };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 320,
            FixedPanel = FixedPanel.None
        };

        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6
        };
        topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 38f));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 32f));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));

        topPanel.Controls.Add(new Label { Text = "本地数据摘要（可编辑后再请求 AI）", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft }, 0, 0);
        _txtSummary = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, WordWrap = true };
        topPanel.Controls.Add(_txtSummary, 0, 1);
        topPanel.Controls.Add(new Label { Text = "选股目标 / 风格（必填）", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft }, 0, 2);
        _txtGoal = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = true };
        topPanel.Controls.Add(_txtGoal, 0, 3);
        topPanel.Controls.Add(new Label { Text = "补充说明（可选：公告、您自己的笔记等）", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft }, 0, 4);
        _txtExtra = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = true };
        topPanel.Controls.Add(_txtExtra, 0, 5);

        split.Panel1.Controls.Add(topPanel);

        _txtReply = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(FontFamily.GenericSansSerif, 9.5f),
            BackColor = Color.White
        };
        split.Panel2.Controls.Add(_txtReply);
        _txtReply.Dock = DockStyle.Fill;
        split.Panel2.Controls.Add(new Label { Text = "AI 回复", Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleLeft });

        Controls.Add(split);
        Controls.Add(_lblStatus);
        Controls.Add(bar);
        Controls.Add(intro);

        _dtpRef.Value = DateTime.Today;
    }

    private static string PrimaryPlateToken(string? plateName)
    {
        if (string.IsNullOrWhiteSpace(plateName)) return "未分类";
        return plateName.Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "未分类";
    }

    private static bool SkipPlateInSummary(string plate)
    {
        if (string.IsNullOrWhiteSpace(plate) || plate == "未分类") return true;
        if (plate is "其他" or "其它" or "其他板块" or "其它板块") return true;
        if (plate.Contains("ST板块", StringComparison.Ordinal)) return true;
        if (plate.Contains("风险警示", StringComparison.Ordinal) || plate.Contains("退市整理", StringComparison.Ordinal))
            return true;
        return false;
    }

    private static string ToCode6(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        return SinaMinuteChartService.NormalizeToCode6(code);
    }

    private async Task LoadSummaryAsync()
    {
        _btnSummary.Enabled = false;
        _lblStatus.Text = "正在读取数据库…";
        try
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dailyRepo = scope.ServiceProvider.GetRequiredService<IStockDailyDataRepository>();
            var favRepo = scope.ServiceProvider.GetRequiredService<IStockFavoriteRepository>();

            // 任意 await 之前读取控件；WinForms 下 async 后续须回到 UI 线程，勿对 DB 异步使用 ConfigureAwait(false)。
            var refDay = _dtpRef.Value.Date;
            var includeFavorites = _chkFavorites.Checked;
            var dayEnd = refDay.AddDays(1);
            var sb = new StringBuilder();

            var latestTrade = await dailyRepo.GetLatestTradeDateAsync().ConfigureAwait(true);
            sb.AppendLine($"【库内最近交易日】{(latestTrade.HasValue ? latestTrade.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "无")}");
            sb.AppendLine($"【摘要参考日】{refDay:yyyy-MM-dd}（与涨停表 analysis_date 对齐）");

            var limitRows = await db.StockLimitUpAnalysis.AsNoTracking()
                .Where(x => x.analysis_date >= refDay && x.analysis_date < dayEnd)
                .Select(x => new { x.code, x.name, x.plate_name })
                .ToListAsync().ConfigureAwait(true);

            var merged = limitRows
                .Where(x => !string.IsNullOrEmpty(ToCode6(x.code)))
                .GroupBy(x => ToCode6(x.code), StringComparer.Ordinal)
                .Select(g =>
                {
                    var name = g.Select(x => x.name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "";
                    var plates = string.Join("、",
                        g.Select(x => x.plate_name).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal));
                    return (Code6: g.Key, Name: name, Plates: plates, Primary: PrimaryPlateToken(plates));
                })
                .Where(x => !IsStName(x.Name))
                .ToList();

            sb.AppendLine($"【该日涨停（合并同名）约】{merged.Count} 只");

            var plateCounts = merged
                .Where(x => !SkipPlateInSummary(x.Primary))
                .GroupBy(x => x.Primary, StringComparer.Ordinal)
                .Select(g => (Plate: g.Key, C: g.Count()))
                .OrderByDescending(x => x.C)
                .Take(12)
                .ToList();

            if (plateCounts.Count > 0)
            {
                sb.AppendLine("【主题材出现频次 Top】");
                foreach (var p in plateCounts)
                    sb.AppendLine($"- {p.Plate}：约 {p.C} 只");
            }

            if (includeFavorites)
            {
                var favs = await favRepo.GetAllAsync().ConfigureAwait(true);
                var codes = favs.Select(f => f.StockCode.Trim()).Where(s => s.Length > 0).Take(40).ToList();
                sb.AppendLine($"【自选股数量】{favs.Count}（下列最多展示 40 个代码）");
                if (codes.Count > 0)
                    sb.AppendLine(string.Join("、", codes));
            }

            sb.AppendLine();
            sb.AppendLine("（以上均为本库统计，非实时行情全市场；若需更细指标请在本页「补充说明」自行粘贴。）");

            _txtSummary.Text = sb.ToString();
            _lblStatus.Text = "摘要已生成，可编辑后点击「请求 AI 选股」。";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "读取失败";
            ErrorLogger.Log(ex, nameof(AiStockPickForm), nameof(LoadSummaryAsync));
            MessageBox.Show(this, $"生成摘要失败：{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _btnSummary.Enabled = true;
        }
    }

    private static bool IsStName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        if (n.StartsWith("S*ST", StringComparison.OrdinalIgnoreCase)) return true;
        if (n.StartsWith("*ST", StringComparison.OrdinalIgnoreCase)) return true;
        return n.StartsWith("ST", StringComparison.OrdinalIgnoreCase) && n.Length >= 4;
    }

    private async Task AskAiAsync()
    {
        var goal = _txtGoal.Text.Trim();
        if (string.IsNullOrEmpty(goal))
        {
            MessageBox.Show(this, "请填写「选股目标 / 风格」。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var summarySnapshot = _txtSummary.Text.Trim();
        var extraSnapshot = _txtExtra.Text.Trim();

        _btnAsk.Enabled = false;
        _lblStatus.Text = "正在调用 DeepSeek…";
        _txtReply.Clear();

        try
        {
            using var scope = _sp.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<DeepSeekClient>();

            var sb = new StringBuilder();
            sb.AppendLine("你是熟悉 A 股的投研助手。用户会使用本地数据库导出的摘要与自身文字说明，请你协助「选股思路与观察名单」。");
            sb.AppendLine();
            sb.AppendLine("硬性要求：");
            sb.AppendLine("1) 不得编造摘要中未出现的具体数字（如精确涨跌幅、财务数据）。");
            sb.AppendLine("2) 输出须包含：对摘要数据的简要解读、选股逻辑、3～8 只可选观察标的（A 股 6 位代码 + 简称），并单列「风险与不确定」。");
            sb.AppendLine("3) 明确声明：以下为研究参考，不构成投资建议。");
            sb.AppendLine("4) 若摘要信息不足，说明缺口并建议用户补充哪些数据（例如：行业、市值区间、是否打板等）。");
            sb.AppendLine();
            sb.AppendLine("【本地数据摘要】");
            sb.AppendLine(string.IsNullOrWhiteSpace(summarySnapshot) ? "（用户未提供摘要）" : summarySnapshot);
            sb.AppendLine();
            sb.AppendLine("【补充说明】");
            sb.AppendLine(string.IsNullOrWhiteSpace(extraSnapshot) ? "（无）" : extraSnapshot);
            sb.AppendLine();
            sb.AppendLine("【选股目标 / 风格】");
            sb.AppendLine(goal);

            var reply = await client.ChatCompletionTextAsync(sb.ToString(), "AiStockPick").ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(reply))
            {
                _txtReply.Text = "未收到有效回复。请检查 appsettings.json 中的 DeepSeek ApiKey 与网络，或稍后重试。";
                _lblStatus.Text = "失败或空回复";
            }
            else
            {
                _txtReply.Text = reply;
                _lblStatus.Text = "完成";
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "异常";
            ErrorLogger.Log(ex, nameof(AiStockPickForm), nameof(AskAiAsync));
            MessageBox.Show(this, $"请求失败：{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnAsk.Enabled = true;
        }
    }
}
