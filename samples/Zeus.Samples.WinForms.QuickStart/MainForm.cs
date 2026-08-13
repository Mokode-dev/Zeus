using System.Text;
using Zeus;

namespace Zeus.Samples.WinForms.QuickStart;

/// <summary>
/// 最小上位机窗口：挂接宿主、绑定状态与回显、向虚拟通道写入。
/// 有硬件时只需把 <see cref="AttachZeus"/> 回调里的虚拟通道换成串口。
/// </summary>
public sealed class MainForm : Form
{
    private readonly TextBox _input = new();
    private readonly Button _send = new();
    private readonly Label _state = new();
    private readonly Label _echo = new();
    private readonly IChannel _meter;

    /// <summary>
    /// 构建界面并挂接 Zeus。绑定发生在启动前，因此不会漏掉打开瞬间的状态事件。
    /// </summary>
    public MainForm()
    {
        Text = "Zeus WinForms QuickStart";
        Width = 520;
        Height = 240;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(16)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _input.Dock = DockStyle.Fill;
        _input.Text = "PING";
        _send.Text = "发送";
        _send.Dock = DockStyle.Fill;
        _state.Dock = DockStyle.Fill;
        _state.TextAlign = ContentAlignment.MiddleLeft;
        _echo.Dock = DockStyle.Fill;
        _echo.TextAlign = ContentAlignment.MiddleLeft;

        layout.Controls.Add(new Label { Text = "发送", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(_input, 1, 0);
        layout.Controls.Add(_send, 1, 1);
        layout.Controls.Add(new Label { Text = "状态", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 2);
        layout.Controls.Add(_state, 1, 2);
        layout.Controls.Add(new Label { Text = "回显", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 3);
        layout.Controls.Add(_echo, 1, 3);
        Controls.Add(layout);

        var attachment = this.AttachZeus(builder => builder.AddVirtualChannel("meter"));
        _meter = attachment.Host.Channels.Get("meter");
        _meter.BindState(_state);
        _meter.BindTo(_echo);
        _send.Click += async (_, _) => await SendAsync();
    }

    /// <summary>
    /// 把输入框文本写入通道。虚拟通道会回显到 <c>_echo</c>。
    /// </summary>
    private async Task SendAsync()
    {
        try
        {
            await _meter.WriteAsync(Encoding.UTF8.GetBytes(_input.Text));
        }
        catch (ZeusException ex)
        {
            MessageBox.Show(this, ex.Message, "发送失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
