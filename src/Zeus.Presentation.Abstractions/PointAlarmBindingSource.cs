using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Zeus;

/// <summary>
/// 报警队列的可绑定投影。属性变更封送到指定调度器，适合报警列表和未确认计数。
/// </summary>
public sealed class PointAlarmBindingSource : INotifyPropertyChanged, IDisposable
{
    private readonly IPointAlarmTable _alarms;
    private readonly IUiDispatcher _dispatcher;
    private IReadOnlyList<PointAlarmRecord> _active = Array.Empty<PointAlarmRecord>();
    private IReadOnlyList<PointAlarmRecord> _history = Array.Empty<PointAlarmRecord>();
    private bool _disposed;

    /// <summary>
    /// 订阅报警队列并投影为可绑定属性。
    /// </summary>
    /// <param name="alarms">宿主报警队列。</param>
    /// <param name="dispatcher">属性变更发布所用的调度器。</param>
    public PointAlarmBindingSource(IPointAlarmTable alarms, IUiDispatcher? dispatcher = null)
    {
        _alarms = alarms ?? throw new ArgumentNullException(nameof(alarms));
        _dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;
        Apply(_alarms.Active, _alarms.History, raiseChanged: false);
        _alarms.Changed += OnChanged;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>当前未复归报警。</summary>
    public IReadOnlyList<PointAlarmRecord> Active => _active;

    /// <summary>最近已复归报警。</summary>
    public IReadOnlyList<PointAlarmRecord> History => _history;

    /// <summary>未复归报警数量。</summary>
    public int ActiveCount => _active.Count;

    /// <summary>尚未确认的报警数量。</summary>
    public int UnacknowledgedCount => _active.Count(item => item.Status == PointAlarmStatus.Active);

    /// <summary>是否存在未复归报警。</summary>
    public bool HasActive => _active.Count > 0;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _alarms.Changed -= OnChanged;
    }

    private void OnChanged(object? sender, PointAlarmChangedEventArgs e)
        => Dispatch(() => Apply(_alarms.Active, _alarms.History, raiseChanged: true));

    private void Dispatch(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Post(action);
    }

    private void Apply(
        IReadOnlyList<PointAlarmRecord> active,
        IReadOnlyList<PointAlarmRecord> history,
        bool raiseChanged)
    {
        if (_disposed)
        {
            return;
        }

        _active = active.ToArray();
        _history = history.ToArray();
        if (!raiseChanged)
        {
            return;
        }

        OnPropertyChanged(nameof(Active));
        OnPropertyChanged(nameof(History));
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(UnacknowledgedCount));
        OnPropertyChanged(nameof(HasActive));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
