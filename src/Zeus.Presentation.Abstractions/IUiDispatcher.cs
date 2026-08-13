namespace Zeus;

/// <summary>
/// 将回调封送到界面线程的抽象。
/// WinForms 与 WPF 各自提供实现；单元测试可使用立即执行的调度器，从而不依赖真实消息循环。
/// </summary>
public interface IUiDispatcher
{
    /// <summary>当前线程是否已是界面线程。</summary>
    bool CheckAccess();

    /// <summary>
    /// 异步投递到界面线程。调用方不得假设 <paramref name="action"/> 在返回前已执行。
    /// </summary>
    /// <param name="action">要在界面线程执行的动作。</param>
    void Post(Action action);
}
