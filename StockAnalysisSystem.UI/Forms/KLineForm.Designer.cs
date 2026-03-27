namespace StockAnalysisSystem.UI.Forms;

partial class KLineForm
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        _plotControl?.Dispose();
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        this.toolStrip1 = new System.Windows.Forms.ToolStrip();
        this.lblStockCode = new System.Windows.Forms.ToolStripLabel();
        this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
        this.btnDaily = new System.Windows.Forms.ToolStripButton();
        this.btnWeekly = new System.Windows.Forms.ToolStripButton();
        this.btnMonthly = new System.Windows.Forms.ToolStripButton();
        this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
        this.btnRefresh = new System.Windows.Forms.ToolStripButton();
        this.statusStrip1 = new System.Windows.Forms.StatusStrip();
        this.toolStripStatusLabel1 = new System.Windows.Forms.ToolStripStatusLabel();
        this.toolStrip1.SuspendLayout();
        this.statusStrip1.SuspendLayout();
        this.SuspendLayout();
        //
        // toolStrip1
        //
        this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.lblStockCode,
            this.toolStripSeparator1,
            this.btnDaily,
            this.btnWeekly,
            this.btnMonthly,
            this.toolStripSeparator2,
            this.btnRefresh
        });
        this.toolStrip1.Location = new System.Drawing.Point(0, 0);
        this.toolStrip1.Name = "toolStrip1";
        this.toolStrip1.Size = new System.Drawing.Size(1184, 25);
        this.toolStrip1.TabIndex = 0;
        this.toolStrip1.Text = "toolStrip1";
        //
        // lblStockCode
        //
        this.lblStockCode.Name = "lblStockCode";
        this.lblStockCode.Size = new System.Drawing.Size(150, 22);
        this.lblStockCode.Text = "股票代码: ";
        //
        // toolStripSeparator1
        //
        this.toolStripSeparator1.Name = "toolStripSeparator1";
        this.toolStripSeparator1.Size = new System.Drawing.Size(6, 25);
        //
        // btnDaily
        //
        this.btnDaily.Checked = true;
        this.btnDaily.CheckOnClick = true;
        this.btnDaily.CheckState = System.Windows.Forms.CheckState.Checked;
        this.btnDaily.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
        this.btnDaily.ImageTransparentColor = System.Drawing.Color.Magenta;
        this.btnDaily.Name = "btnDaily";
        this.btnDaily.Size = new System.Drawing.Size(36, 22);
        this.btnDaily.Text = "日K";
        //
        // btnWeekly
        //
        this.btnWeekly.CheckOnClick = true;
        this.btnWeekly.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
        this.btnWeekly.ImageTransparentColor = System.Drawing.Color.Magenta;
        this.btnWeekly.Name = "btnWeekly";
        this.btnWeekly.Size = new System.Drawing.Size(36, 22);
        this.btnWeekly.Text = "周K";
        //
        // btnMonthly
        //
        this.btnMonthly.CheckOnClick = true;
        this.btnMonthly.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
        this.btnMonthly.ImageTransparentColor = System.Drawing.Color.Magenta;
        this.btnMonthly.Name = "btnMonthly";
        this.btnMonthly.Size = new System.Drawing.Size(36, 22);
        this.btnMonthly.Text = "月K";
        //
        // toolStripSeparator2
        //
        this.toolStripSeparator2.Name = "toolStripSeparator2";
        this.toolStripSeparator2.Size = new System.Drawing.Size(6, 25);
        //
        // btnRefresh
        //
        this.btnRefresh.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
        this.btnRefresh.ImageTransparentColor = System.Drawing.Color.Magenta;
        this.btnRefresh.Name = "btnRefresh";
        this.btnRefresh.Size = new System.Drawing.Size(36, 22);
        this.btnRefresh.Text = "刷新";
        //
        // statusStrip1
        //
        this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripStatusLabel1
        });
        this.statusStrip1.Location = new System.Drawing.Point(0, 739);
        this.statusStrip1.Name = "statusStrip1";
        this.statusStrip1.Size = new System.Drawing.Size(1184, 22);
        this.statusStrip1.TabIndex = 1;
        this.statusStrip1.Text = "statusStrip1";
        //
        // toolStripStatusLabel1
        //
        this.toolStripStatusLabel1.Name = "toolStripStatusLabel1";
        this.toolStripStatusLabel1.Spring = true;
        this.toolStripStatusLabel1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        this.toolStripStatusLabel1.Text = "就绪";
        //
        // KLineForm
        //
        this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(1184, 761);
        this.Controls.Add(this.statusStrip1);
        this.Controls.Add(this.toolStrip1);
        this.Name = "KLineForm";
        this.Text = "KLineForm";
        this.Load += new System.EventHandler(this.KLineForm_Load);
        this.toolStrip1.ResumeLayout(false);
        this.toolStrip1.PerformLayout();
        this.statusStrip1.ResumeLayout(false);
        this.statusStrip1.PerformLayout();
        this.ResumeLayout(false);
        this.PerformLayout();

    }

    #endregion

    private System.Windows.Forms.ToolStrip toolStrip1;
    private System.Windows.Forms.ToolStripLabel lblStockCode;
    private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
    private System.Windows.Forms.ToolStripButton btnDaily;
    private System.Windows.Forms.ToolStripButton btnWeekly;
    private System.Windows.Forms.ToolStripButton btnMonthly;
    private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
    private System.Windows.Forms.ToolStripButton btnRefresh;
    private System.Windows.Forms.StatusStrip statusStrip1;
    private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel1;
}
