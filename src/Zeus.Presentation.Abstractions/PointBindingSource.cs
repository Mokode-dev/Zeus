using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Zeus;

/// <summary>
/// 单个点的可绑定投影。属性变更会封送到指定调度器，适合 WinForms / WPF 直接绑定当前值、报警与错误状态。
/// </summary>
public sealed class PointBindingSource : INotifyPropertyChanged, IDisposable
{
    private readonly IPointTable _table;
    private readonly string _pointName;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<object?, string> _formatter;
    private PointSnapshot? _snapshot;
    private string _text = string.Empty;
    private bool _disposed;

    /// <summary>
    /// 订阅点表变化并投影为可绑定属性。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="dispatcher">属性变更发布所用的调度器。测试可传入 <see cref="ImmediateUiDispatcher"/>。</param>
    /// <param name="formatter">成功值到文本的转换；为空时使用不变区域性的 <see cref="object.ToString"/>。</param>
    public PointBindingSource(
        IPointTable table,
        string pointName,
        IUiDispatcher? dispatcher = null,
        Func<object?, string>? formatter = null)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _pointName = PointUiFormatting.NormalizePointName(pointName);
        _dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;
        _formatter = formatter ?? PointUiFormatting.DefaultFormat;

        if (TryFindSnapshot(out var snapshot) && snapshot is not null)
        {
            ApplySnapshot(snapshot, raiseChanged: false);
        }

        _table.Changed += OnChanged;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>绑定时传入的点名，可为短名或限定名。</summary>
    public string PointName => _pointName;

    /// <summary>最近一次点快照。点尚未登记或尚未变化时可能为空。</summary>
    public PointSnapshot? Snapshot => _snapshot;

    /// <summary>点短名。点尚未登记时返回绑定点名。</summary>
    public string Name => _snapshot?.Definition.Name ?? _pointName;

    /// <summary>限定名，格式为 <c>设备.点</c>。点尚未登记时返回绑定点名。</summary>
    public string QualifiedName => _snapshot?.QualifiedName ?? _pointName;

    /// <summary>点值类型。点尚未登记时为空。</summary>
    public PointValueKind? Kind => _snapshot?.Definition.Kind;

    /// <summary>该点是否允许通过点表写回设备。</summary>
    public bool Writable => _snapshot?.Definition.Writable ?? false;

    /// <summary>当前值；采集失败时保留上一次成功值。</summary>
    public object? Value => _snapshot?.Value;

    /// <summary>当前值的文本；采集失败时显示错误说明。</summary>
    public string Text => _text;

    /// <summary>最近一次失败原因；成功或尚未采集时为空字符串。</summary>
    public string Error => _snapshot?.Error ?? string.Empty;

    /// <summary>当前快照是否包含采集错误。</summary>
    public bool HasError => _snapshot?.Error is not null;

    /// <summary>当前快照是否有成功值。</summary>
    public bool HasValue => _snapshot?.Value is not null;

    /// <summary>最近一次成功更新的时间。</summary>
    public DateTimeOffset? UpdatedAt => _snapshot?.UpdatedAt;

    /// <summary>报警状态。点尚未登记时返回 <see cref="PointAlarmState.Unknown"/>。</summary>
    public PointAlarmState AlarmState => _snapshot?.AlarmState ?? PointAlarmState.Unknown;

    /// <summary>当前快照是否处于高报或低报。</summary>
    public bool IsAlarmed => _snapshot?.IsAlarmed ?? false;

    /// <summary>
    /// 按绑定点名把工程值写回设备。
    /// </summary>
    /// <param name="value">工程值。</param>
    /// <param name="cancellationToken">取消本次写入。</param>
    public Task WriteAsync(object value, CancellationToken cancellationToken = default)
        => _table.WriteAsync(_pointName, value, cancellationToken);

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
        if (!PointUiFormatting.Matches(e.Current.Definition, _pointName))
        {
            return;
        }

        Dispatch(() => ApplySnapshot(e.Current, raiseChanged: true));
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

    private bool TryFindSnapshot(out PointSnapshot? snapshot)
    {
        snapshot = _table.All.FirstOrDefault(item => PointUiFormatting.Matches(item.Definition, _pointName));
        return snapshot is not null;
    }

    private void ApplySnapshot(PointSnapshot snapshot, bool raiseChanged)
    {
        if (_disposed)
        {
            return;
        }

        _snapshot = snapshot;
        _text = PointUiFormatting.FormatSnapshot(snapshot, _formatter);

        if (!raiseChanged)
        {
            return;
        }

        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(QualifiedName));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Writable));
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasValue));
        OnPropertyChanged(nameof(UpdatedAt));
        OnPropertyChanged(nameof(AlarmState));
        OnPropertyChanged(nameof(IsAlarmed));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
