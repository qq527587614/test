using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StockAnalysisSystem.Core.Strategies.Rules;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace StockAnalysisSystem_UI_WinUI.Pages;

public sealed partial class StrategyBuilderPage : Page
{
    private readonly List<IRuleNode> _entryRules = new();

    public StrategyBuilderPage()
    {
        InitializeComponent();
        CboRuleType.SelectionChanged += CboRuleType_SelectionChanged;
        InitCombos();
        UpdateRulePanels();
        RefreshEntryList();
        RefreshJson();
    }

    private void InitCombos()
    {
        var sources = Enum.GetValues<ValueSource>().Cast<ValueSource>().ToList();
        foreach (var s in sources)
        {
            CboLeftSource.Items.Add(new ComboBoxItem { Content = s.ToString(), Tag = s });
            CboFastSource.Items.Add(new ComboBoxItem { Content = s.ToString(), Tag = s });
            CboSlowSource.Items.Add(new ComboBoxItem { Content = s.ToString(), Tag = s });
        }

        var ops = Enum.GetValues<CompareOp>().Cast<CompareOp>().ToList();
        foreach (var op in ops)
        {
            CboCompareOp.Items.Add(new ComboBoxItem { Content = OpToText(op), Tag = op });
        }

        CboLeftSource.SelectedIndex = sources.IndexOf(ValueSource.MA5);
        CboCompareOp.SelectedIndex = ops.IndexOf(CompareOp.GreaterThan);
        TxtRightValue.Text = "0";

        CboFastSource.SelectedIndex = sources.IndexOf(ValueSource.MA5);
        CboSlowSource.SelectedIndex = sources.IndexOf(ValueSource.MA10);
    }

    private static string OpToText(CompareOp op) => op switch
    {
        CompareOp.GreaterThan => ">",
        CompareOp.GreaterOrEqual => ">=",
        CompareOp.LessThan => "<",
        CompareOp.LessOrEqual => "<=",
        CompareOp.Equal => "==",
        CompareOp.NotEqual => "!=",
        _ => op.ToString()
    };

    private void CboRuleType_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateRulePanels();

    private void UpdateRulePanels()
    {
        // In WinUI, SelectionChanged can fire during XAML load; guard against partially constructed tree.
        if (PanelCompare == null || PanelCross == null || CboRuleType == null)
            return;

        var isCompare = CboRuleType.SelectedIndex <= 0;
        PanelCompare.Visibility = isCompare ? Visibility.Visible : Visibility.Collapsed;
        PanelCross.Visibility = isCompare ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BtnAddEntry_Click(object sender, RoutedEventArgs e)
    {
        var ruleType = CboRuleType.SelectedIndex switch
        {
            0 => "compare",
            1 => "crossOver",
            2 => "crossUnder",
            _ => "compare"
        };

        IRuleNode? node = null;

        if (ruleType == "compare")
        {
            if ((CboLeftSource.SelectedItem as ComboBoxItem)?.Tag is not ValueSource left)
                return;
            if ((CboCompareOp.SelectedItem as ComboBoxItem)?.Tag is not CompareOp op)
                return;
            if (!decimal.TryParse(TxtRightValue.Text, out var rv))
            {
                TxtStatus.Text = "右侧常数不是有效数字。";
                return;
            }
            node = new CompareNode { Left = left, Op = op, RightValue = rv };
        }
        else
        {
            if ((CboFastSource.SelectedItem as ComboBoxItem)?.Tag is not ValueSource fast)
                return;
            if ((CboSlowSource.SelectedItem as ComboBoxItem)?.Tag is not ValueSource slow)
                return;

            node = ruleType == "crossUnder"
                ? new CrossUnderNode { Fast = fast, Slow = slow }
                : new CrossOverNode { Fast = fast, Slow = slow };
        }

        _entryRules.Add(node);
        RefreshEntryList();
        RefreshJson();
    }

    private void BtnRemoveEntry_Click(object sender, RoutedEventArgs e)
    {
        var idx = EntryRulesList.SelectedIndex;
        if (idx < 0 || idx >= _entryRules.Count)
            return;
        _entryRules.RemoveAt(idx);
        RefreshEntryList();
        RefreshJson();
    }

    private void BtnClearEntry_Click(object sender, RoutedEventArgs e)
    {
        _entryRules.Clear();
        RefreshEntryList();
        RefreshJson();
    }

    private void BtnRefreshJson_Click(object sender, RoutedEventArgs e) => RefreshJson();

    private void BtnTemplateFirstBoardPullback_Click(object sender, RoutedEventArgs e)
    {
        TxtName.Text = "首板后回落（方案A）";
        _entryRules.Clear();
        _entryRules.Add(new FirstBoardPullbackNode
        {
            LimitUpThreshold = 9.95m,
            PullbackRange = 0.03m,
            MaxDeviationFromFirstBoardLowPercent = 3m,
            MaxDaysAfterLimitUp = 10,
            MinDaysAfterLimitUp = 1,
            FirstBoardLookbackDays = 30,
            MaxDailyDropPercent = 9m
        });
        RefreshEntryList();
        RefreshJson();
        TxtStatus.Text = "已套用模板：首板后回落（方案A）。你可以在 JSON 里微调参数（例如 LookbackDays、MaxDaysAfterLimitUp）。";
    }

    private async void BtnSaveJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            picker.FileTypeChoices.Add("JSON", new[] { ".json" });
            picker.SuggestedFileName = "strategy-definition";
            picker.DefaultFileExtension = ".json";

            var hwnd = Services.AppServices.MainWindow == null
                ? IntPtr.Zero
                : WinRT.Interop.WindowNative.GetWindowHandle(Services.AppServices.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null)
                return;

            await FileIO.WriteTextAsync(file, TxtJson.Text ?? "");
            TxtStatus.Text = $"已保存：{file.Path}";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"保存失败：{ex.Message}";
        }
    }

    private void RefreshEntryList()
    {
        EntryRulesList.Items.Clear();
        foreach (var n in _entryRules)
        {
            EntryRulesList.Items.Add(new TextBlock { Text = Describe(n) });
        }
    }

    private void RefreshJson()
    {
        var def = new StrategyDefinition
        {
            Name = string.IsNullOrWhiteSpace(TxtName.Text) ? "规则策略" : TxtName.Text.Trim(),
            EntryRule = _entryRules.Count == 0 ? null : new AndNode { Children = _entryRules.ToList() },
            ExitRule = null,
            Parameters = new Dictionary<string, object>()
        };

        var json = JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true });
        TxtJson.Text = json;
        TxtStatus.Text = _entryRules.Count == 0 ? "提示：入场规则为空，将不会产生买入信号。" : $"已添加 {_entryRules.Count} 条入场条件。";
    }

    private static string Describe(IRuleNode n)
    {
        return n switch
        {
            CompareNode c => $"{c.Left} {OpToText(c.Op)} {c.RightValue}",
            CrossOverNode x => $"{x.Fast} 上穿 {x.Slow}",
            CrossUnderNode x => $"{x.Fast} 下穿 {x.Slow}",
            FirstBoardPullbackNode p => $"首板后回落: lookback={p.FirstBoardLookbackDays}d, days=[{p.MinDaysAfterLimitUp},{p.MaxDaysAfterLimitUp}], dev<=min({p.PullbackRange:P0},{p.MaxDeviationFromFirstBoardLowPercent}%)",
            _ => n.GetType().Name
        };
    }
}

