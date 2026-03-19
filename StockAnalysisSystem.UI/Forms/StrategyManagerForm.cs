using Microsoft.Extensions.DependencyInjection;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.Strategies;

namespace StockAnalysisSystem.UI.Forms;

public partial class StrategyManagerForm : Form
{
    private readonly IStrategyRepository _strategyRepo;
    private readonly IServiceProvider _serviceProvider;
    private DataGridView _dataGridView = null!;
    private GroupBox _detailGroup = null!;
    private TextBox _txtName = null!;
    private TextBox _txtDescription = null!;
    private ComboBox _cboStrategyType = null!;
    private Panel _parameterPanel = null!;
    private Button _btnSave = null!;
    private Button _btnDelete = null!;
    private Button _btnNew = null!;
    private CheckBox _chkActive = null!;

    private List<Strategy> _strategies = new();
    private Strategy? _currentStrategy;

    public StrategyManagerForm(IStrategyRepository strategyRepo, IServiceProvider serviceProvider)
    {
        _strategyRepo = strategyRepo;
        _serviceProvider = serviceProvider;
        InitializeComponent();
        InitializeControls();
        LoadStrategies();
    }

    private void InitializeControls()
    {
        // 左侧列表
        var leftPanel = new Panel { Dock = DockStyle.Left, Width = 400 };
        _dataGridView = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        _dataGridView.Columns.Add("Id", "ID");
        _dataGridView.Columns.Add("Name", "策略名称");
        _dataGridView.Columns.Add("StrategyType", "策略类型");
        _dataGridView.Columns.Add("IsActive", "启用");
        _dataGridView.SelectionChanged += DataGridView_SelectionChanged;
        leftPanel.Controls.Add(_dataGridView);

        // 工具栏
        var toolBar = new Panel { Dock = DockStyle.Top, Height = 40 };
        _btnNew = new Button { Text = "新建", Left = 10, Top = 8, Width = 80 };
        _btnNew.Click += BtnNew_Click;
        _btnSave = new Button { Text = "保存", Left = 100, Top = 8, Width = 80 };
        _btnSave.Click += BtnSave_Click;
        _btnDelete = new Button { Text = "删除", Left = 190, Top = 8, Width = 80 };
        _btnDelete.Click += BtnDelete_Click;
        toolBar.Controls.AddRange(new Control[] { _btnNew, _btnSave, _btnDelete });
        leftPanel.Controls.Add(toolBar);

        Controls.Add(leftPanel);

        // 右侧详情
        _detailGroup = new GroupBox { Dock = DockStyle.Fill, Text = "策略详情", Padding = new Padding(10) };
        
        int y = 30;
        var lblName = new Label { Text = "策略名称:", Left = 20, Top = y, Width = 80 };
        _txtName = new TextBox { Left = 110, Top = y - 3, Width = 300 };
        y += 35;

        var lblType = new Label { Text = "策略类型:", Left = 20, Top = y, Width = 80 };
        _cboStrategyType = new ComboBox { Left = 110, Top = y - 3, Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
        _cboStrategyType.Items.AddRange(StrategyFactory.GetSupportedTypes().ToArray());
        _cboStrategyType.SelectedIndexChanged += CboStrategyType_SelectedIndexChanged;
        y += 35;

        var lblDesc = new Label { Text = "描述:", Left = 20, Top = y, Width = 80 };
        _txtDescription = new TextBox { Left = 110, Top = y - 3, Width = 300, Height = 60, Multiline = true };
        y += 70;

        _chkActive = new CheckBox { Text = "启用", Left = 110, Top = y, Width = 80 };
        y += 30;

        var lblParam = new Label { Text = "参数:", Left = 20, Top = y, Width = 80 };
        _parameterPanel = new Panel { Left = 110, Top = y, Width = 400, Height = 200, BorderStyle = BorderStyle.FixedSingle };

        _detailGroup.Controls.AddRange(new Control[] { 
            lblName, _txtName, lblType, _cboStrategyType, 
            lblDesc, _txtDescription, _chkActive, lblParam, _parameterPanel 
        });

        Controls.Add(_detailGroup);

        Text = "策略管理";
    }

    private async void LoadStrategies()
    {
        try
        {
            _strategies = await _strategyRepo.GetAllAsync();
            _dataGridView.Rows.Clear();
            foreach (var s in _strategies)
            {
                _dataGridView.Rows.Add(s.Id, s.Name, s.StrategyType, s.IsActive ? "是" : "否");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载策略失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DataGridView_SelectionChanged(object? sender, EventArgs e)
    {
        if (_dataGridView.SelectedRows.Count > 0)
        {
            var id = (int)_dataGridView.SelectedRows[0].Cells[0].Value;
            _currentStrategy = _strategies.FirstOrDefault(s => s.Id == id);
            if (_currentStrategy != null)
            {
                _txtName.Text = _currentStrategy.Name;
                _txtDescription.Text = _currentStrategy.Description ?? "";
                _cboStrategyType.SelectedItem = _currentStrategy.StrategyType;
                _chkActive.Checked = _currentStrategy.IsActive;
                LoadParameters();
            }
        }
    }

    private void CboStrategyType_SelectedIndexChanged(object? sender, EventArgs e)
    {
        LoadParameters();
    }

    private void LoadParameters()
    {
        _parameterPanel.Controls.Clear();
        var type = _cboStrategyType.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(type)) return;

        var defaultParams = StrategyFactory.GetDefaultParameters(type);
        int y = 10;

        foreach (var kvp in defaultParams)
        {
            var lbl = new Label { Text = kvp.Key + ":", Left = 10, Top = y, Width = 100 };
            var num = new NumericUpDown 
            { 
                Left = 120, Top = y - 3, Width = 100,
                Minimum = 1, Maximum = 1000,
                Value = Convert.ToDecimal(kvp.Value),
                Tag = kvp.Key
            };
            _parameterPanel.Controls.AddRange(new Control[] { lbl, num });
            y += 35;
        }
    }

    private void BtnNew_Click(object? sender, EventArgs e)
    {
        _currentStrategy = null;
        _txtName.Clear();
        _txtDescription.Clear();
        _cboStrategyType.SelectedIndex = -1;
        _chkActive.Checked = true;
        _parameterPanel.Controls.Clear();
        _dataGridView.ClearSelection();
    }

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text) || string.IsNullOrWhiteSpace(_cboStrategyType.SelectedItem?.ToString()))
        {
            MessageBox.Show("请填写策略名称和类型", "提示");
            return;
        }

        try
        {
            var parameters = new Dictionary<string, object>();
            foreach (Control ctrl in _parameterPanel.Controls)
            {
                if (ctrl is NumericUpDown num && ctrl.Tag != null)
                {
                    parameters[ctrl.Tag.ToString()!] = (int)num.Value;
                }
            }

            var strategy = _currentStrategy ?? new Strategy();
            strategy.Name = _txtName.Text;
            strategy.Description = _txtDescription.Text;
            strategy.StrategyType = _cboStrategyType.SelectedItem.ToString()!;
            strategy.ParametersDict = parameters;
            strategy.IsActive = _chkActive.Checked;

            if (_currentStrategy == null)
            {
                await _strategyRepo.AddAsync(strategy);
            }
            else
            {
                await _strategyRepo.UpdateAsync(strategy);
            }

            LoadStrategies();
            MessageBox.Show("保存成功", "提示");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (_currentStrategy == null) return;

        if (MessageBox.Show($"确定要删除策略 '{_currentStrategy.Name}' 吗?", "确认删除", 
            MessageBoxButtons.YesNo) == DialogResult.Yes)
        {
            try
            {
                await _strategyRepo.DeleteAsync(_currentStrategy.Id);
                LoadStrategies();
                BtnNew_Click(sender, e);
                MessageBox.Show("删除成功", "提示");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
