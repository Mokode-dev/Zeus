namespace Zeus;

/// <summary>
/// 通道到界面的通用绑定。调度器由各 UI 适配器提供，本类型不引用任何桌面框架。
/// </summary>
public static class ChannelUiExtensions
{
    /// <summary>
    /// 把接收到的字节格式化后推到界面。释放返回值即可退订。
    /// </summary>
    /// <param name="channel">数据来源通道。</param>
    /// <param name="dispatcher">界面线程调度器。</param>
    /// <param name="setText">在界面线程上设置文本。</param>
    /// <param name="formatter">字节到文本的转换；默认使用 <see cref="ChannelTextFormatter.Default"/>。</param>
    /// <returns>绑定句柄，释放后停止更新。</returns>
    public static IUiBinding BindText(
        this IChannel channel,
        IUiDispatcher dispatcher,
        Action<string> setText,
        Func<ReadOnlyMemory<byte>, string>? formatter = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(setText);
        formatter ??= ChannelTextFormatter.Default;

        void OnDataReceived(object? sender, ChannelDataReceivedEventArgs e)
        {
            var text = formatter(e.Data);
            Dispatch(dispatcher, () => setText(text));
        }

        channel.DataReceived += OnDataReceived;
        return new DelegateUiBinding(() => channel.DataReceived -= OnDataReceived);
    }

    /// <summary>
    /// 把通道状态推到界面，订阅时会立即推送当前状态。
    /// </summary>
    /// <param name="channel">要观察的通道。</param>
    /// <param name="dispatcher">界面线程调度器。</param>
    /// <param name="setText">在界面线程上设置状态文本。</param>
    /// <returns>绑定句柄，释放后停止更新。</returns>
    public static IUiBinding BindState(this IChannel channel, IUiDispatcher dispatcher, Action<string> setText)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(setText);

        void Apply(ChannelState state) => Dispatch(dispatcher, () => setText(state.ToString()));

        void OnStateChanged(object? sender, ChannelStateChangedEventArgs e) => Apply(e.Current);

        Apply(channel.State);
        channel.StateChanged += OnStateChanged;
        return new DelegateUiBinding(() => channel.StateChanged -= OnStateChanged);
    }

    /// <summary>
    /// 创建可绑定投影，属性变更会封送到 <paramref name="dispatcher"/>。
    /// </summary>
    /// <param name="channel">要观察的通道。</param>
    /// <param name="dispatcher">界面线程调度器。测试可传入 <see cref="ImmediateUiDispatcher"/>。</param>
    public static ChannelBindingSource AsBindingSource(this IChannel channel, IUiDispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return new ChannelBindingSource(channel, dispatcher);
    }

    /// <summary>
    /// 已在界面线程则同步执行，否则投递，避免不必要的闪烁与重入。
    /// </summary>
    private static void Dispatch(IUiDispatcher dispatcher, Action action)
    {
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Post(action);
    }
}
