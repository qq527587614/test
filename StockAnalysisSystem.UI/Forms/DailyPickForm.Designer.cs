namespace StockAnalysisSystem.UI.Forms;

partial class DailyPickForm
{
    private System.ComponentModel.IContainer components = null;

    private System.Windows.Forms.DateTimePicker _dtpDate;
    private System.Windows.Forms.Button _btnRefresh;
    private System.Windows.Forms.Button _btnAddFavorite;
    private System.Windows.Forms.CheckBox _chkDeepSeek;
    private System.Windows.Forms.DataGridView _dataGridView;
    private System.Windows.Forms.Label _lblStats;
    private System.Windows.Forms.Panel toolBar;
    private System.Windows.Forms.Label lblDate;
    private System.Windows.Forms.ContextMenuStrip contextMenu;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this._dtpDate = new System.Windows.Forms.DateTimePicker();
        this._btnRefresh = new System.Windows.Forms.Button();
        this._btnAddFavorite = new System.Windows.Forms.Button();
        this._chkDeepSeek = new System.Windows.Forms.CheckBox();
        this._dataGridView = new System.Windows.Forms.DataGridView();
        this._lblStats = new System.Windows.Forms.Label();
        this.toolBar = new System.Windows.Forms.Panel();
        this.lblDate = new System.Windows.Forms.Label();
        this.contextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
        this.toolBar.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this._dataGridView)).BeginInit();
        this.SuspendLayout();
        // 
        // _dtpDate
        // 
        this._dtpDate.Location = new System.Drawing.Point(100, 12);
        this._dtpDate.Name = "_dtpDate";
        this._dtpDate.Size = new System.Drawing.Size(150, 23);
        this._dtpDate.TabIndex = 0;
        // 
        // _btnRefresh
        // 
        this._btnRefresh.BackColor = System.Drawing.Color.LightBlue;
        this._btnRefresh.Location = new System.Drawing.Point(420, 10);
        this._btnRefresh.Name = "_btnRefresh";
        this._btnRefresh.Size = new System.Drawing.Size(100, 30);
        this._btnRefresh.TabIndex = 1;
        this._btnRefresh.Text = "刷新选股";
        this._btnRefresh.UseVisualStyleBackColor = false;
        this._btnRefresh.Click += new System.EventHandler(this.BtnRefresh_Click);
        //
        // _btnAddFavorite
        //
        this._btnAddFavorite.Location = new System.Drawing.Point(530, 10);
        this._btnAddFavorite.Name = "_btnAddFavorite";
        this._btnAddFavorite.Size = new System.Drawing.Size(100, 30);
        this._btnAddFavorite.TabIndex = 6;
        this._btnAddFavorite.Text = "加入自选";
        this._btnAddFavorite.UseVisualStyleBackColor = true;
        this._btnAddFavorite.Click += new System.EventHandler(this.BtnAddFavorite_Click);
        //
        // _chkDeepSeek
        // 
        this._chkDeepSeek.AutoSize = true;
        this._chkDeepSeek.Location = new System.Drawing.Point(270, 14);
        this._chkDeepSeek.Name = "_chkDeepSeek";
        this._chkDeepSeek.Size = new System.Drawing.Size(130, 21);
        this._chkDeepSeek.TabIndex = 2;
        this._chkDeepSeek.Text = "启用DeepSeek评分";
        this._chkDeepSeek.UseVisualStyleBackColor = true;
        // 
        // _dataGridView
        // 
        this._dataGridView.AllowUserToAddRows = false;
        this._dataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        this._dataGridView.Dock = System.Windows.Forms.DockStyle.Fill;
        this._dataGridView.Location = new System.Drawing.Point(0, 50);
        this._dataGridView.Name = "_dataGridView";
        this._dataGridView.ReadOnly = true;
        this._dataGridView.RowHeadersWidth = 51;
        this._dataGridView.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
        this._dataGridView.Size = new System.Drawing.Size(1000, 550);
        this._dataGridView.TabIndex = 3;
        // 
        // _lblStats
        // 
        this._lblStats.AutoSize = true;
        this._lblStats.Location = new System.Drawing.Point(650, 15);
        this._lblStats.Name = "_lblStats";
        this._lblStats.Size = new System.Drawing.Size(0, 17);
        this._lblStats.TabIndex = 4;
        // 
        // toolBar
        // 
        this.toolBar.Controls.Add(this.lblDate);
        this.toolBar.Controls.Add(this._dtpDate);
        this.toolBar.Controls.Add(this._btnRefresh);
        this.toolBar.Controls.Add(this._btnAddFavorite);
        this.toolBar.Controls.Add(this._chkDeepSeek);
        this.toolBar.Controls.Add(this._lblStats);
        this.toolBar.Dock = System.Windows.Forms.DockStyle.Top;
        this.toolBar.Location = new System.Drawing.Point(0, 0);
        this.toolBar.Name = "toolBar";
        this.toolBar.Size = new System.Drawing.Size(1000, 50);
        this.toolBar.TabIndex = 5;
        // 
        // lblDate
        // 
        this.lblDate.AutoSize = true;
        this.lblDate.Location = new System.Drawing.Point(20, 15);
        this.lblDate.Name = "lblDate";
        this.lblDate.Size = new System.Drawing.Size(56, 17);
        this.lblDate.TabIndex = 5;
        this.lblDate.Text = "选股日期:";
        // 
        // DailyPickForm
        // 
        this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(1000, 600);
        this.Controls.Add(this._dataGridView);
        this.Controls.Add(this.toolBar);
        this.Name = "DailyPickForm";
        this.Text = "每日选股";
        this.toolBar.ResumeLayout(false);
        this.toolBar.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)(this._dataGridView)).EndInit();
        this.ResumeLayout(false);

    }

    #endregion
}
