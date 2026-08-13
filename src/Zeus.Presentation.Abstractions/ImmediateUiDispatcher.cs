namespace Zeus;

/// <summary>
/// 在当前线程立即执行的调度器，供单元测试与无界面宿主使用。
/// </summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    /// <summary>全局共享实例。该实现无状态，可安全复用。</summary>
    public static ImmediateUiDispatcher Instance { get; } = new();

    /// <inheritdoc />
    public bool CheckAccess() => true;

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }
}
