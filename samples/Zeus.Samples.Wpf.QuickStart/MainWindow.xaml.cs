using System.Text;
using System.Windows;
using Zeus;

namespace Zeus.Samples.Wpf.QuickStart;

/// <summary>
/// 最小 WPF 上位机窗口。绑定与宿主挂接都在构造函数完成，避免漏掉启动瞬间的状态。
/// </summary>
public partial class MainWindow : Window
{
    private readonly IChannel _meter;

    /// <summary>
    /// 初始化窗口、挂接宿主并绑定状态与回显。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        var attachment = this.AttachZeus(builder => builder.AddVirtualChannel("meter"));
        _meter = attachment.Host.Channels.Get("meter");
        _meter.BindState(StateText);
        _meter.BindTo(EchoText);
    }

    /// <summary>
    /// 把输入框文本写入通道。
    /// </summary>
    private async void OnSendClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _meter.WriteAsync(Encoding.UTF8.GetBytes(InputBox.Text));
        }
        catch (ZeusException ex)
        {
            MessageBox.Show(this, ex.Message, "发送失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
