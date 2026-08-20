namespace Zeus;

/// <summary>
/// 内存报警队列。同一点同时只保留一条未复归记录；复归后进入有上限的历史。
/// </summary>
public sealed class PointAlarmTable : IPointAlarmTable
{
    private const int DefaultHistoryCapacity = 256;
    private readonly object _gate = new();
    private readonly int _historyCapacity;
    private readonly Dictionary<string, PointAlarmRecord> _activeByPoint = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> _idToPoint = [];
    private readonly List<PointAlarmRecord> _history = [];

    /// <summary>
    /// 创建报警队列并订阅点表变化。
    /// </summary>
    /// <param name="points">宿主点表。</param>
    /// <param name="historyCapacity">已复归记录保留条数。</param>
    public PointAlarmTable(IPointTable points, int historyCapacity = DefaultHistoryCapacity)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (historyCapacity < 0)
        {
            throw new ZeusException("报警历史容量不能为负数。");
        }

        _historyCapacity = historyCapacity;
        points.Changed += OnPointChanged;
    }

    /// <inheritdoc />
    public event EventHandler<PointAlarmChangedEventArgs>? Changed;

    /// <inheritdoc />
    public IReadOnlyList<PointAlarmRecord> Active
    {
        get
        {
            lock (_gate)
            {
                return _activeByPoint.Values
                    .OrderBy(item => item.RaisedAt)
                    .ToArray();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<PointAlarmRecord> History
    {
        get
        {
            lock (_gate)
            {
                return _history.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public PointAlarmRecord Acknowledge(Guid id, string? acknowledgedBy = null)
    {
        PointAlarmRecord? previous;
        PointAlarmRecord current;
        lock (_gate)
        {
            if (!_idToPoint.TryGetValue(id, out var qualified)
                || !_activeByPoint.TryGetValue(qualified, out var existing))
            {
                throw new ZeusException($"找不到标识为 {id} 的活动报警。请确认该报警尚未复归。");
            }

            previous = existing;
            current = AcknowledgeLocked(existing, acknowledgedBy);
        }

        if (!ReferenceEquals(previous, current))
        {
            Changed?.Invoke(this, new PointAlarmChangedEventArgs(previous, current));
        }

        return current;
    }

    /// <inheritdoc />
    public PointAlarmRecord? AcknowledgePoint(string pointName, string? acknowledgedBy = null)
    {
        if (string.IsNullOrWhiteSpace(pointName))
        {
            throw new ZeusException("确认报警时点名不能为空。");
        }

        PointAlarmRecord? previous;
        PointAlarmRecord current;
        lock (_gate)
        {
            var key = pointName.Trim();
            var existing = _activeByPoint.Values.FirstOrDefault(item =>
                item.QualifiedName.Equals(key, StringComparison.OrdinalIgnoreCase)
                || item.PointName.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return null;
            }

            previous = existing;
            current = AcknowledgeLocked(existing, acknowledgedBy);
        }

        if (!ReferenceEquals(previous, current))
        {
            Changed?.Invoke(this, new PointAlarmChangedEventArgs(previous, current));
        }

        return current;
    }

    /// <inheritdoc />
    public IReadOnlyList<PointAlarmRecord> AcknowledgeAll(string? acknowledgedBy = null)
    {
        var changes = new List<(PointAlarmRecord Previous, PointAlarmRecord Current)>();
        lock (_gate)
        {
            foreach (var existing in _activeByPoint.Values.ToArray())
            {
                var updated = AcknowledgeLocked(existing, acknowledgedBy);
                if (!ReferenceEquals(existing, updated))
                {
                    changes.Add((existing, updated));
                }
            }
        }

        foreach (var change in changes)
        {
            Changed?.Invoke(this, new PointAlarmChangedEventArgs(change.Previous, change.Current));
        }

        return changes.Select(item => item.Current).ToArray();
    }

    /// <summary>
    /// 设备卸载时把该设备未复归报警标为已复归，避免队列留下悬空点名。
    /// </summary>
    /// <param name="deviceName">设备名。</param>
    public void ClearDevice(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return;
        }

        var key = deviceName.Trim();
        var changes = new List<(PointAlarmRecord Previous, PointAlarmRecord Current)>();
        lock (_gate)
        {
            foreach (var existing in _activeByPoint.Values
                .Where(item => item.DeviceName.Equals(key, StringComparison.OrdinalIgnoreCase))
                .ToArray())
            {
                changes.Add((existing, ClearLocked(existing, existing.Value, DateTimeOffset.Now)));
            }
        }

        foreach (var change in changes)
        {
            Changed?.Invoke(this, new PointAlarmChangedEventArgs(change.Previous, change.Current));
        }
    }

    private void OnPointChanged(object? sender, PointChangedEventArgs e)
    {
        var snapshot = e.Current;
        if (snapshot.IsAlarmed)
        {
            RaiseOrRefresh(snapshot);
            return;
        }

        if (snapshot.AlarmState is PointAlarmState.Normal or PointAlarmState.Disabled)
        {
            ClearIfOpen(snapshot);
        }
    }

    private void RaiseOrRefresh(PointSnapshot snapshot)
    {
        PointAlarmRecord? previous;
        PointAlarmRecord current;
        lock (_gate)
        {
            if (_activeByPoint.TryGetValue(snapshot.QualifiedName, out var existing))
            {
                previous = existing;
                current = new PointAlarmRecord(
                    existing.Id,
                    existing.QualifiedName,
                    existing.PointName,
                    existing.DeviceName,
                    snapshot.AlarmState,
                    existing.Status,
                    snapshot.Value,
                    existing.RaisedAt,
                    existing.AcknowledgedAt,
                    existing.ClearedAt,
                    existing.AcknowledgedBy);
                _activeByPoint[snapshot.QualifiedName] = current;
            }
            else
            {
                previous = null;
                current = new PointAlarmRecord(
                    Guid.NewGuid(),
                    snapshot.QualifiedName,
                    snapshot.Definition.Name,
                    snapshot.Definition.DeviceName,
                    snapshot.AlarmState,
                    PointAlarmStatus.Active,
                    snapshot.Value,
                    snapshot.UpdatedAt ?? DateTimeOffset.Now,
                    null,
                    null,
                    null);
                _activeByPoint[snapshot.QualifiedName] = current;
                _idToPoint[current.Id] = snapshot.QualifiedName;
            }
        }

        if (previous is null
            || previous.AlarmState != current.AlarmState
            || previous.Status != current.Status
            || !Equals(previous.Value, current.Value))
        {
            Changed?.Invoke(this, new PointAlarmChangedEventArgs(previous, current));
        }
    }

    private void ClearIfOpen(PointSnapshot snapshot)
    {
        PointAlarmRecord? previous;
        PointAlarmRecord current;
        lock (_gate)
        {
            if (!_activeByPoint.TryGetValue(snapshot.QualifiedName, out var existing))
            {
                return;
            }

            previous = existing;
            current = ClearLocked(existing, snapshot.Value, snapshot.UpdatedAt ?? DateTimeOffset.Now);
        }

        Changed?.Invoke(this, new PointAlarmChangedEventArgs(previous, current));
    }

    private PointAlarmRecord AcknowledgeLocked(PointAlarmRecord existing, string? acknowledgedBy)
    {
        if (existing.Status != PointAlarmStatus.Active)
        {
            return existing;
        }

        var current = new PointAlarmRecord(
            existing.Id,
            existing.QualifiedName,
            existing.PointName,
            existing.DeviceName,
            existing.AlarmState,
            PointAlarmStatus.Acknowledged,
            existing.Value,
            existing.RaisedAt,
            DateTimeOffset.Now,
            existing.ClearedAt,
            string.IsNullOrWhiteSpace(acknowledgedBy) ? null : acknowledgedBy.Trim());
        _activeByPoint[existing.QualifiedName] = current;
        return current;
    }

    private PointAlarmRecord ClearLocked(PointAlarmRecord existing, object? value, DateTimeOffset clearedAt)
    {
        var current = new PointAlarmRecord(
            existing.Id,
            existing.QualifiedName,
            existing.PointName,
            existing.DeviceName,
            existing.AlarmState,
            PointAlarmStatus.Cleared,
            value,
            existing.RaisedAt,
            existing.AcknowledgedAt,
            clearedAt,
            existing.AcknowledgedBy);
        _activeByPoint.Remove(existing.QualifiedName);
        _idToPoint.Remove(existing.Id);
        _history.Add(current);
        if (_history.Count > _historyCapacity)
        {
            _history.RemoveRange(0, _history.Count - _historyCapacity);
        }

        return current;
    }
}
