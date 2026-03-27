using System.Windows.Forms;

namespace StockAnalysisSystem.UI.Forms;

/// <summary>
/// 输入对话框
/// </summary>
public static class InputBox
{
    /// <summary>
    /// 显示输入对话框
    /// </summary>
    /// <param name="prompt">提示信息</param>
    /// <param name="title">对话框标题</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>用户输入的文本，如果用户点击取消或输入为空则返回null</returns>
    public static string Show(string prompt, string title = "输入", string defaultValue = "")
    {
        var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false
        };

        var label = new Label
        {
            Text = prompt,
            AutoSize = true,
            Left = 10,
            Top = 10,
            Width = Math.Max(200, TextRenderer.MeasureText(prompt, form.Font).Width)
        };

        var textBox = new TextBox
        {
            Text = defaultValue,
            Left = 10,
            Top = 40,
            Width = Math.Max(200, TextRenderer.MeasureText(defaultValue, form.Font).Width + 20)
        };

        var btnOK = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Left = 10,
            Top = 70,
            Width = 80
        };

        var btnCancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Left = 100,
            Top = 70,
            Width = 80
        };

        btnOK.Click += (s, e) => form.DialogResult = DialogResult.OK;
        btnCancel.Click += (s, e) => form.DialogResult = DialogResult.Cancel;

        form.Controls.AddRange(new Control[] { label, textBox, btnOK, btnCancel });
        form.ClientSize = new Size(Math.Max(220, label.Width + 20), 110);
        form.AcceptButton = btnOK;
        form.CancelButton = btnCancel;

        var result = form.ShowDialog();

        if (result == DialogResult.OK)
        {
            return textBox.Text.Trim();
        }

        return string.Empty;
    }
}
