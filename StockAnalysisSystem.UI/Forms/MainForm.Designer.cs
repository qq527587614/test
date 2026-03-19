namespace StockAnalysisSystem.UI.Forms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

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
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(1200, 800);
        this.Text = "股票分析系统";
        this.Name = "MainForm";
        this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
        this.MinimumSize = new System.Drawing.Size(1200, 800);
    }

    #endregion
}
