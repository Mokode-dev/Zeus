using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Zeus;

/// <summary>
/// 单个点的历史采样投影。属性变更会封送到指定调度器，适合把点表历史接到趋势图或报警时间线。
/// </summary>
public sealed class PointHistoryBindingSource : INotifyPropertyChanged, IDisposable
{
    private readonly IPointTable _table;
    private readonly string _pointName;
    private readonly IUiDispatcher _dispatcher;
    private IReadOnlyList<PointSnapshot> _history = Array.Empty<PointSnapshot>();
    private PointSnapshot? _latest;
    private bool _disposed;

    /// <summary>
    /// 订阅点表变化并投影最近成功采样历史。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="dispatcher">属性变更发布所用的调度器。测试可传入 <see cref="ImmediateUiDispatcher"/>。</param>
    public PointHistoryBindingSource(
        IPointTable table,
        string pointName,
        IUiDispatcher? dispatcher = null)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _pointName = PointUiFormatting.NormalizePointName(pointName);
        _dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;

        if (TryReadInitialHistory(out var history))
        {
            ApplyHistory(history, raiseChanged: false);
        }

        _table.Changed += OnChanged;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>绑定时传入的点名，可为短名或限定名。</summary>
    public string PointName => _pointName;

    /// <summary>最近成功采样历史，顺序从旧到新。</summary>
    public IReadOnlyList<PointSnapshot> History => _history;

    /// <summary>最新一条成功采样；尚无成功采样时为空。</summary>
    public PointSnapshot? Latest => _latest;

    /// <summary>历史采样数量。</summary>
    public int Count => _history.Count;

    /// <summary>是否已有至少一条成功采样。</summary>
    public bool HasSamples => _latest is not null;

    /// <summary>最新成功采样值。</summary>
    public object? LatestValue => _latest?.Value;

    /// <summary>最新成功采样时间。</summary>
    public DateTimeOffset? LatestUpdatedAt => _latest?.UpdatedAt;

    /// <summary>最新成功采样的报警状态。尚无成功采样时返回 <see cref="PointAlarmState.Unknown"/>。</summary>
    public PointAlarmState LatestAlarmState => _latest?.AlarmState ?? PointAlarmState.Unknown;

    /// <summary>最新成功采样是否处于高报或低报。</summary>
    public bool IsLatestAlarmed => _latest?.IsAlarmed ?? false;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _table.Changed -= OnChanged;
    }

    private void OnChanged(object? sender, PointChangedEventArgs e)
    {
        if (e.Current.Error is not null || !PointUiFormatting.Matches(e.Current.Definition, _pointName))
        {
            return;
        }

        var history = _table.GetHistory(e.Current.QualifiedName).ToArray();
        Dispatch(() => ApplyHistory(history, raiseChanged: true));
    }

    private void Dispatch(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Post(action);
    }

    private bool TryReadInitialHistory(out IReadOnlyList<PointSnapshot> history)
    {
        var snapshot = _table.All.FirstOrDefault(item => PointUiFormatting.Matches(item.Definition, _pointName));
        if (snapshot is null)
        {
            history = Array.Empty<PointSnapshot>();
            return false;
        }

        history = _table.GetHistory(snapshot.QualifiedName).ToArray();
        return true;
    }

    private void ApplyHistory(IReadOnlyList<PointSnapshot> history, bool raiseChanged)
    {
        if (_disposed)
        {
            return;
        }

        _history = history;
        _latest = history.Count == 0 ? null : history[^1];

        if (!raiseChanged)
        {
            return;
        }

        OnPropertyChanged(nameof(History));
        OnPropertyChanged(nameof(Latest));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasSamples));
        OnPropertyChanged(nameof(LatestValue));
        OnPropertyChanged(nameof(LatestUpdatedAt));
        OnPropertyChanged(nameof(LatestAlarmState));
        OnPropertyChanged(nameof(IsLatestAlarmed));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
