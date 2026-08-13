using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Zeus;

/// <summary>
/// 通道的可绑定投影。属性变更会封送到指定调度器，WPF 可直接作为 <c>DataContext</c>。
/// WinForms 若不用数据绑定，仍可订阅 <see cref="PropertyChanged"/> 或改用控件级 <c>BindText</c>。
/// </summary>
public sealed class ChannelBindingSource : INotifyPropertyChanged, IDisposable
{
    private readonly IChannel _channel;
    private readonly IUiDispatcher _dispatcher;
    private string _lastText = string.Empty;
    private string _lastHex = string.Empty;
    private ChannelState _state;
    private int _receivedCount;
    private bool _disposed;

    /// <summary>
    /// 订阅通道事件并投影为可绑定属性。
    /// </summary>
    /// <param name="channel">要观察的通道。</param>
    /// <param name="dispatcher">属性变更发布所用的调度器。测试可传入 <see cref="ImmediateUiDispatcher"/>。</param>
    public ChannelBindingSource(IChannel channel, IUiDispatcher? dispatcher = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;
        _state = channel.State;
        _channel.DataReceived += OnDataReceived;
        _channel.StateChanged += OnStateChanged;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>通道注册名，绑定后不会变化。</summary>
    public string Name => _channel.Name;

    /// <summary>最近一次通道状态。</summary>
    public ChannelState State => _state;

    /// <summary>状态的字符串形式，便于直接绑到文本控件。</summary>
    public string StateText => _state.ToString();

    /// <summary>最近一次载荷的默认文本表示。</summary>
    public string LastText => _lastText;

    /// <summary>最近一次载荷的十六进制表示。</summary>
    public string LastHex => _lastHex;

    /// <summary>自绑定创建以来收到的次数。</summary>
    public int ReceivedCount => _receivedCount;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.DataReceived -= OnDataReceived;
        _channel.StateChanged -= OnStateChanged;
    }

    private void OnDataReceived(object? sender, ChannelDataReceivedEventArgs e)
    {
        var text = ChannelTextFormatter.Default(e.Data);
        var hex = ChannelTextFormatter.Hex(e.Data);
        _dispatcher.Post(() =>
        {
            _lastText = text;
            _lastHex = hex;
            _receivedCount++;
            OnPropertyChanged(nameof(LastText));
            OnPropertyChanged(nameof(LastHex));
            OnPropertyChanged(nameof(ReceivedCount));
        });
    }

    private void OnStateChanged(object? sender, ChannelStateChangedEventArgs e)
    {
        _dispatcher.Post(() =>
        {
            _state = e.Current;
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(StateText));
        });
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
