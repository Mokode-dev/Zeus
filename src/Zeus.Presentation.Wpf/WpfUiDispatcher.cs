using System.Windows.Threading;

namespace Zeus;

/// <summary>
/// 基于 WPF <see cref="Dispatcher"/> 的调度器。
/// </summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    /// <summary>
    /// 使用指定调度器。通常传入 <c>Application.Current.Dispatcher</c> 或控件的 <c>Dispatcher</c>。
    /// </summary>
    /// <param name="dispatcher">WPF 调度器。</param>
    public WpfUiDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <inheritdoc />
    public bool CheckAccess() => _dispatcher.CheckAccess();

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        _dispatcher.BeginInvoke(action);
    }
}
