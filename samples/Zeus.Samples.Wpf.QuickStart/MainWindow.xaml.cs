using System.Globalization;
using System.Text;
using System.Windows;
using Zeus;

namespace Zeus.Samples.Wpf.QuickStart;

/// <summary>
/// 最小 WPF 上位机窗口。绑定与宿主挂接都在构造函数完成，避免漏掉启动瞬间的状态。
/// </summary>
public partial class MainWindow : Window
{
    private readonly PointTable _points = new();
    private readonly IChannel _meter;
    private readonly IPointTableWriter _pointWriter;
    private bool _highTemperature;

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

        _pointWriter = _points;
        _pointWriter.Register(new PointDefinition(
            "temperature",
            "oven",
            PointValueKind.Double,
            new PointAlarmLimits(high: 80),
            writable: false));
        _pointWriter.Publish("oven.temperature", 72.5d);
        _points.BindTo("temperature", TemperatureText, FormatTemperature);
        _points.BindAlarmBackground("temperature", TemperatureText);
        _points.BindSnapshot("temperature", AlarmText, snapshot => AlarmText.Text = snapshot.AlarmState.ToString());
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

    /// <summary>
    /// 模拟现场采集值变化，用来展示点表报警绑定。
    /// </summary>
    private void OnToggleTemperatureClick(object sender, RoutedEventArgs e)
    {
        _highTemperature = !_highTemperature;
        _pointWriter.Publish("oven.temperature", _highTemperature ? 96.5d : 72.5d);
        ToggleTemperatureButton.Content = _highTemperature ? "恢复正常" : "模拟高温";
    }

    private static string FormatTemperature(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("0.0", CultureInfo.InvariantCulture) + " C";
    }
}
