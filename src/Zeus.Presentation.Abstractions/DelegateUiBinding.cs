namespace Zeus;

/// <summary>
/// 用委托实现的绑定句柄。适配器在订阅事件后返回本类型，释放时退订。
/// </summary>
public sealed class DelegateUiBinding : IUiBinding
{
    private Action? _dispose;

    /// <summary>
    /// 创建绑定句柄。
    /// </summary>
    /// <param name="dispose">释放时执行一次的退订动作。</param>
    public DelegateUiBinding(Action dispose)
    {
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var action = Interlocked.Exchange(ref _dispose, null);
        action?.Invoke();
    }
}
