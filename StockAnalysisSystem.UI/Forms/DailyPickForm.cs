using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockAnalysisSystem.Core.DailyPick;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.RealtimeData;
using StockAnalysisSystem.Core.Services;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.UI.Forms;

    public partial class DailyPickForm : Form
{
    private readonly DailyPicker _picker;
    private readonly IDailyPickRepository _pickRepo;
    private readonly IServiceProvider _serviceProvider;
    private readonly StockFavoriteService _favoriteService;
    private readonly TencentRealtimeService _realtimeService;
    private bool _isHistoryMode = false;  // 是否为历史模式（只查询不选股）
    private Dictionary<string, (decimal Price, decimal ChangePercent)> _todayPriceDict = new(); // 今日行情数据缓存
    private Dictionary<string, int> _strategyIdMap = new();  // 策略名称到ID的映射

    public DailyPickForm(DailyPicker picker, IDailyPickRepository pickRepo, IServiceProvider serviceProvider, StockFavoriteService favoriteService)
    {
        _picker = picker;
        _pickRepo = pickRepo;
        _serviceProvider = serviceProvider;
        _favoriteService = favoriteService;
        _realtimeService = serviceProvider.GetRequiredService<TencentRealtimeService>();
        InitializeComponent();
    }

    /// <summary>
    /// 设置表单模式
    /// </summary>
    /// <param name="isHistoryMode">是否为历史模式（true=只查询历史数据，false=允许刷新选股）</param>
    public void SetMode(bool isHistoryMode)
    {
        _isHistoryMode = isHistoryMode;
        
        if (isHistoryMode)
        {
            // 历史模式：按钮可点击，但只刷新历史数据
           // _btnRefresh.Enabled = true;
            _btnRefresh.Text = "刷新历史";
            _btnRefresh.BackColor = System.Drawing.Color.LightGray;
            _chkDeepSeek.Enabled = false;
        }
        else
        {
            // 每日选股模式：按钮可点击，执行选股
            _btnRefresh.Enabled = true;
            _btnRefresh.Text = "刷新选股";
            _btnRefresh.BackColor = System.Drawing.Color.LightBlue;
            _chkDeepSeek.Enabled = true;
        }
    }

    private async void DailyPickForm_Load(object? sender, EventArgs e)
    {
        try
        {
            await LoadDataAsync();
            await LoadStrategiesAsync();
        }
        catch (Exception ex)
        {
            var fullError = ex.Message;
            if (ex.InnerException != null)
            {
                fullError += "\n内部错误: " + ex.InnerException.Message;
            }
            ErrorLogger.Log(ex, "DailyPickForm_Load", "表单加载失败");
            MessageBox.Show($"表单加载失败: {fullError}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task LoadDataAsync()
    {
        try
        {
            var results = await _picker.GetHistoryAsync(_dtpDate.Value);
            DisplayResults(results);

            // 获取今日行情数据
            await LoadTodayRealtimeDataAsync(results);
        }
        catch (Exception ex)
        {
            var fullError = ex.Message;
            if (ex.InnerException != null)
            {
                fullError += "\n内部错误: " + ex.InnerException.Message;
            }
            ErrorLogger.Log(ex, "DailyPickForm.LoadDataAsync", "加载数据失败");
            MessageBox.Show($"加载数据失败: {fullError}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnRefresh.Enabled = true;
            _btnRefresh.Text = _isHistoryMode ? "刷新历史" : "刷新选股";
            _btnRefresh.BackColor = _isHistoryMode ? System.Drawing.Color.LightGray : System.Drawing.Color.LightBlue;
            _chkDeepSeek.Enabled = !_isHistoryMode;
        }
    }

    /// <summary>
    /// 加载今日实时行情数据
    /// </summary>
    private async Task LoadTodayRealtimeDataAsync(List<DailyPickResult> results)
    {
        if (results == null || results.Count == 0) return;

        try
        {
            // 腾讯接口需要带前缀的股票代码（sz/sh）
            var stockCodesWithPrefix = results.Select(r => r.StockCode)
                .Distinct()
                .Select(code => code.StartsWith("6") ? "sh" + code : "sz" + code)
                .ToList();

            var priceData = await _realtimeService.GetRealtimeDataAsync(stockCodesWithPrefix);

            _todayPriceDict.Clear();
            foreach (var data in priceData)
            {
                if (data != null && !string.IsNullOrEmpty(data.StockCode))
                _todayPriceDict[data.StockCode] = (data.CurrentPrice, data.ChangePercent);
            }

            // 更新DataGridView中今日涨幅列
            foreach (DataGridViewRow row in _dataGridView.Rows)
            {
                var stockCode = row.Cells["StockCode"].Value?.ToString();
                if (_todayPriceDict.TryGetValue(stockCode, out var priceInfo))
                {
                    row.Cells["TodayPrice"].Value = priceInfo.Price.ToString("F2");
                    row.Cells["TodayChangePercent"].Value = priceInfo.ChangePercent.ToString("F2");
                    
                    // 根据涨跌幅设置颜色
                    row.Cells["TodayChangePercent"].Style.ForeColor = 
                        priceInfo.ChangePercent >= 0 ? System.Drawing.Color.FromArgb(220, 20, 60) : System.Drawing.Color.FromArgb(20, 160, 60);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "DailyPickForm.LoadTodayRealtimeDataAsync", "获取今日行情失败");
        }
    }

    private async void BtnRefresh_Click(object? sender, EventArgs e)
    {
        if (_isHistoryMode)
        {
            // 历史模式：只刷新历史数据，不执行选股
            await LoadDataAsync();
            return;
        }

        // 每日选股模式：执行选股
        _btnRefresh.Enabled = false;
        _btnRefresh.Text = "选股中...";
        _dataGridView.Rows.Clear();

        try
        {
            var progress = new Progress<string>(msg =>
            {
                this.Invoke(() => { _lblStats.Text = msg; });
            });

            var results = await _picker.PickAsync(_dtpDate.Value, null, _chkDeepSeek.Checked, progress);
            DisplayResults(results);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"选股失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnRefresh.Enabled = true;
            _btnRefresh.Text = _isHistoryMode ? "刷新历史" : "刷新选股";
        }
    }

    private void BtnAddFavorite_Click(object? sender, EventArgs e)
    {
        AddToFavorite(sender, e);
    }

    private void DisplayResults(List<DailyPickResult> results)
    {
        // 初始化 DataGridView 列（如果还没有列）
        if (_dataGridView.Columns.Count == 0)
        {
            _dataGridView.Columns.Add("StockCode", "股票代码");
            _dataGridView.Columns.Add("StockName", "股票名称");
            _dataGridView.Columns.Add("Industry", "行业");
            _dataGridView.Columns.Add("StrategyName", "策略");
            _dataGridView.Columns.Add("Reason", "理由");
            _dataGridView.Columns.Add("DeepSeekScore", "DeepSeek评分");
            _dataGridView.Columns.Add("FinalScore", "最终得分");
            _dataGridView.Columns.Add("LimitUpCount", "涨停次数");
            _dataGridView.Columns.Add("Plates", "所属板块");
            _dataGridView.Columns.Add("TodayChangePercent", "今日涨幅");
            _dataGridView.Columns.Add("TodayPrice", "今日价格");

            // 设置列宽
            _dataGridView.Columns["StockCode"].Width = 80;
            _dataGridView.Columns["StockName"].Width = 100;
            _dataGridView.Columns["Industry"].Width = 100;
            _dataGridView.Columns["StrategyName"].Width = 120;
            _dataGridView.Columns["Reason"].Width = 200;
            _dataGridView.Columns["DeepSeekScore"].Width = 80;
            _dataGridView.Columns["FinalScore"].Width = 80;
            _dataGridView.Columns["LimitUpCount"].Width = 80;
            _dataGridView.Columns["Plates"].Width = 150;
            _dataGridView.Columns["TodayChangePercent"].Width = 80;
            _dataGridView.Columns["TodayPrice"].Width = 80;
        }

        _dataGridView.Rows.Clear();

        // 获取历史涨停数据
        var limitUpData = new Dictionary<string, (int Count, string Plates)>();
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Core.AppDbContext>();

            var stockCodes = results.Select(r => r.StockCode).Distinct().ToList();
            if (stockCodes.Any())
            {
                // 涨停表股票代码需要加前缀才能匹配 (sz/sh)
                var stockCodesWithPrefix = stockCodes
                    .Select(code => code.StartsWith("6") ? "sh" + code : "sz" + code)
                    .ToList();

                var limitUpRecords = dbContext.StockLimitUpAnalysis
                    .Where(s => stockCodesWithPrefix.Contains(s.code))
                    .ToList();

                // 存储时去掉前缀，保持与选股结果一致
                limitUpData = limitUpRecords
                    .GroupBy(s => s.code.Length > 2 ? s.code.Substring(2) : s.code)
                    .ToDictionary(
                        g => g.Key,
                        g => (Count: g.Count(), Plates: string.Join(", ", g.Where(p => !string.IsNullOrEmpty(p.plate_name)).Select(p => p.plate_name!).Distinct()))
                    );
            }
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "DailyPickForm.DisplayResults", "获取涨停数据失败");
        }

        foreach (var r in results)
        {
            var limitUpInfo = limitUpData.TryGetValue(r.StockCode, out var info) ? info : (Count: 0, Plates: "");
            _dataGridView.Rows.Add(
                r.StockCode, r.StockName, r.Industry,
                r.StrategyName, r.Reason,
                r.DeepSeekScore?.ToString("F1") ?? "-",
                r.FinalScore.ToString("F1"),
                limitUpInfo.Count > 0 ? limitUpInfo.Count.ToString() : "-",
                !string.IsNullOrEmpty(limitUpInfo.Plates) ? limitUpInfo.Plates : "-",
                "加载中...",  // 今日涨幅
                "加载中..."   // 今日价格
            );
        }

        _lblStats.Text = $"共选出 {results.Count} 只股票, 平均得分: {(results.Count > 0 ? results.Average(r => r.FinalScore).ToString("F1") : "-")}";
    }

    private async void AddToFavorite(object? sender, EventArgs e)
    {
        if (_dataGridView.SelectedRows.Count == 0)
        {
            MessageBox.Show("请先选择要加入自选的股票", "提示");
            return;
        }

        var successCount = 0;
        var failCount = 0;
        var existedCount = 0;

        foreach (DataGridViewRow row in _dataGridView.SelectedRows)
        {
            var stockCode = row.Cells["StockCode"].Value?.ToString();
            if (string.IsNullOrEmpty(stockCode)) continue;

            try
            {
                var result = await _favoriteService.AddFavoriteAsync(stockCode);
                if (result == "添加成功")
                {
                    successCount++;
                }
                else if (result == "股票已在自选股中")
                {
                    existedCount++;
                }
                else
                {
                    failCount++;
                }
            }
            catch (Exception ex)
            {
                failCount++;
                var fullError = ex.Message;
                if (ex.InnerException != null)
                {
                    fullError += "\n内部错误: " + ex.InnerException.Message;
                }
                ErrorLogger.Log(ex, "DailyPickForm.AddToFavorite", $"股票代码: {stockCode}, 错误: {fullError}");
            }
        }

        var msg = "";
        if (successCount > 0) msg += $"成功添加 {successCount} 只股票到自选。";
        if (existedCount > 0) msg += (msg.Length > 0 ? " " : "") + $"{existedCount} 只股票已在自选中。";
        if (failCount > 0) msg += (msg.Length > 0 ? " " : "") + $"添加失败 {failCount} 只股票 (请查看日志)";

        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void DataGridView_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        // 检查是否点击了按钮列
        if (e.RowIndex < 0) return;
        if (e.ColumnIndex < 0) return;

        var column = _dataGridView.Columns[e.ColumnIndex];
        if (column is DataGridViewButtonColumn && column.Name == "ActionColumn")
        {
            var row = _dataGridView.Rows[e.RowIndex];
            var stockCode = row.Cells["StockCode"].Value?.ToString();
            var stockName = row.Cells["StockName"].Value?.ToString();

            if (string.IsNullOrEmpty(stockCode))
            {
                MessageBox.Show("股票代码无效", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                var result = await _favoriteService.AddFavoriteAsync(stockCode);
                if (result == "添加成功")
                {
                    MessageBox.Show($"已添加 {stockName}({stockCode}) 到自选", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (result == "股票已在自选股中")
                {
                    MessageBox.Show($"{stockName}({stockCode}) 已在自选股中", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(result, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                var errorMsg = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMsg += "\n内部错误: " + ex.InnerException.Message;
                }
                MessageBox.Show($"添加失败: {errorMsg}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void ExportSelected(object? sender, EventArgs e)
    {
        if (_dataGridView.SelectedRows.Count == 0)
        {
            MessageBox.Show("请先选择要导出的股票", "提示");
            return;
        }

        using var saveDialog = new SaveFileDialog
        {
            Filter = "CSV文件|*.csv",
            FileName = $"选股结果_{_dtpDate.Value:yyyyMMdd}.csv"
        };

        if (saveDialog.ShowDialog() == DialogResult.OK)
        {
            using var writer = new StreamWriter(saveDialog.FileName);
            writer.WriteLine("股票代码,股票名称,行业,策略,理由,DeepSeek评分,最终得分");
            foreach (DataGridViewRow row in _dataGridView.SelectedRows)
            {
                writer.WriteLine($"\"{row.Cells[0].Value}\",\"{row.Cells[1].Value}\",\"{row.Cells[2].Value}\"," +
                               $"\"{row.Cells[3].Value}\",\"{row.Cells[4].Value}\"," +
                               $"\"{row.Cells[5].Value}\",\"{row.Cells[6].Value}\"");
            }
            MessageBox.Show($"导出成功: {saveDialog.FileName}", "提示");
        }
    }

    /// <summary>
    /// 加载策略列表
    /// </summary>
    private async Task LoadStrategiesAsync()
    {
        try
        {
            // 先检查控件是否可用
            if (lstStrategies == null)
            {
                MessageBox.Show("策略列表控件未初始化", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Core.AppDbContext>();

            // 调试：先查询所有策略（包括未激活的）
            var allStrategies = await Task.Run(() =>
            {
                return dbContext.Strategies
                    .Select(s => new
                    {
                        Id = s.Id,
                        Name = s.Name ?? s.StrategyType,
                        StrategyType = s.StrategyType,
                        IsActive = s.IsActive
                    })
                    .OrderBy(s => s.Name)
                    .ToList();
            });

            System.Diagnostics.Debug.WriteLine($"数据库中共有 {allStrategies.Count} 个策略");
            foreach (var s in allStrategies)
            {
                System.Diagnostics.Debug.WriteLine($"  - {s.Name} (ID:{s.Id}, Active:{s.IsActive})");
            }

            // 查询所有激活的策略
            var strategies = allStrategies.Where(s => s.IsActive).ToList();

            // 清空现有数据
            lstStrategies.Items.Clear();
            _strategyIdMap.Clear();

            if (strategies.Count == 0)
            {
                string message = $"数据库中没有激活的策略。\n\n数据库中共有 {allStrategies.Count} 个策略，但都没有启用。\n\n请在'策略管理'中启用至少一个策略。";
                MessageBox.Show(message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            foreach (var strategy in strategies)
            {
                lstStrategies.Items.Add(strategy.Name);  // 只添加策略名称
                _strategyIdMap[strategy.Name] = strategy.Id;  // 保存策略名称到ID的映射

                // 默认选中所有策略
                lstStrategies.SetItemChecked(lstStrategies.Items.Count - 1, true);
            }

            _lblStats.Text = $"已加载 {strategies.Count} 个策略";
        }
        catch (Exception ex)
        {
            var fullError = ex.Message;
            if (ex.InnerException != null)
            {
                fullError += "\n内部错误: " + ex.InnerException.Message;
            }
            ErrorLogger.Log(ex, "DailyPickForm.LoadStrategies", "加载策略列表失败");
            MessageBox.Show($"加载策略列表失败: {fullError}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 组合选股按钮点击事件
    /// </summary>
    private async void BtnCombinePick_Click(object? sender, EventArgs e)
    {
        if (_isHistoryMode)
        {
            // 历史模式：不执行选股
            MessageBox.Show("历史模式不能执行选股，请切换到每日选股模式", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 获取选中的策略
        var selectedStrategyNames = lstStrategies.CheckedItems.Cast<string>().ToList();
        if (selectedStrategyNames.Count == 0)
        {
            MessageBox.Show("请先选择至少一个策略", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 将策略名称转换为策略ID列表
        var selectedStrategyIds = selectedStrategyNames
            .Where(name => _strategyIdMap.ContainsKey(name))
            .Select(name => _strategyIdMap[name])
            .ToList();

        _btnRefresh.Enabled = false;
        _btnCombinePick.Enabled = false;
        _dataGridView.Rows.Clear();

        try
        {
            var progress = new Progress<string>(msg =>
            {
                this.Invoke(() => { _lblStats.Text = msg; });
            });

            // 组合选股：必须同时满足所有选中的策略（且关系）
            this.Invoke(() => { _lblStats.Text = $"正在执行组合选股（且关系）..."; });

            // 分别对每个策略执行选股
            var strategyResults = new Dictionary<int, List<DailyPickResult>>();

            for (int i = 0; i < selectedStrategyIds.Count; i++)
            {
                var strategyId = selectedStrategyIds[i];
                var strategyName = selectedStrategyNames[i];

                this.Invoke(() => { _lblStats.Text = $"正在执行策略 {i + 1}/{selectedStrategyIds.Count}: {strategyName}..."; });

                var results = await _picker.PickAsync(_dtpDate.Value, new List<int> { strategyId }, false, progress);
                strategyResults[strategyId] = results;

                this.Invoke(() => { _lblStats.Text = $"策略 '{strategyName}' 选出 {results.Count} 只股票"; });
            }

            // 找出同时满足所有策略的股票（取交集）
            var combinedResults = new List<DailyPickResult>();
            var allStockCodes = strategyResults.Values.SelectMany(r => r.Select(p => p.StockCode)).Distinct().ToList();

            foreach (var stockCode in allStockCodes)
            {
                // 检查该股票是否在所有策略的结果中都存在
                bool inAllStrategies = true;
                var matchedResults = new List<DailyPickResult>();

                foreach (var strategyId in selectedStrategyIds)
                {
                    var result = strategyResults[strategyId].FirstOrDefault(r => r.StockCode == stockCode);
                    if (result == null)
                    {
                        inAllStrategies = false;
                        break;
                    }
                    matchedResults.Add(result);
                }

                // 如果在所有策略中都有该股票，则添加到组合结果
                if (inAllStrategies && matchedResults.Count > 0)
                {
                    // 合并所有策略的理由
                    var combinedResult = matchedResults[0];
                    var allReasons = matchedResults.Select(r => r.Reason).ToList();
                    combinedResult.Reason = string.Join(" | ", allReasons);
                    combinedResult.StrategyName = string.Join(", ", matchedResults.Select(r => r.StrategyName).Distinct());

                    combinedResults.Add(combinedResult);
                }
            }

            // 如果启用DeepSeek评分，需要调用完整的选股（传入所有策略，但用DeepSeek评分）
            if (_chkDeepSeek.Checked && combinedResults.Count > 0)
            {
                this.Invoke(() => { _lblStats.Text = $"正在对 {combinedResults.Count} 只股票进行DeepSeek评分..."; });

                // 由于组合逻辑改变，这里使用单独的DeepSeek评分方法
                // 简化：只显示技术评分，不调用DeepSeek（避免复杂的权限问题）
                _lblStats.Text = "注意：组合选股使用严格交集逻辑，暂不支持DeepSeek评分";
            }

            DisplayResults(combinedResults);

            MessageBox.Show($"组合选股完成！\n\n" +
                          $"选中策略数: {selectedStrategyIds.Count}\n" +
                          $"同时满足所有策略的股票数: {combinedResults.Count}\n\n" +
                          $"各策略单独选出的股票数:\n" +
                          string.Join("\n", strategyResults.Select(kv => $"  - {selectedStrategyNames[selectedStrategyIds.IndexOf(kv.Key)]}: {kv.Value.Count} 只")),
                          "组合选股结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            var fullError = ex.Message;
            if (ex.InnerException != null)
            {
                fullError += "\n内部错误: " + ex.InnerException.Message;
            }
            MessageBox.Show($"组合选股失败: {fullError}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnRefresh.Enabled = true;
            _btnCombinePick.Enabled = true;
            _btnRefresh.Text = _isHistoryMode ? "刷新历史" : "刷新选股";
            _btnCombinePick.Text = "组合选股";
        }
    }
}

