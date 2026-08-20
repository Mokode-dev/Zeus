namespace Zeus;

/// <summary>
/// 一次点报警的不可变快照。同一点同时只保留一条活动记录；复归后会进入历史。
/// </summary>
public sealed class PointAlarmRecord
{
    /// <summary>
    /// 创建报警记录。
    /// </summary>
    /// <param name="id">记录标识，确认与复归时使用。</param>
    /// <param name="qualifiedName">点限定名。</param>
    /// <param name="pointName">点短名。</param>
    /// <param name="deviceName">所属设备名。</param>
    /// <param name="alarmState">触发时的高低报状态。</param>
    /// <param name="status">队列状态。</param>
    /// <param name="value">触发或最近一次刷新时的工程值。</param>
    /// <param name="raisedAt">首次进入越限的时间。</param>
    /// <param name="acknowledgedAt">确认时间；未确认时为 <c>null</c>。</param>
    /// <param name="clearedAt">复归时间；仍活动时为 <c>null</c>。</param>
    /// <param name="acknowledgedBy">确认人；未确认时为 <c>null</c>。</param>
    public PointAlarmRecord(
        Guid id,
        string qualifiedName,
        string pointName,
        string deviceName,
        PointAlarmState alarmState,
        PointAlarmStatus status,
        object? value,
        DateTimeOffset raisedAt,
        DateTimeOffset? acknowledgedAt,
        DateTimeOffset? clearedAt,
        string? acknowledgedBy)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            throw new ZeusException("报警记录的点限定名不能为空。");
        }

        Id = id;
        QualifiedName = qualifiedName;
        PointName = pointName;
        DeviceName = deviceName;
        AlarmState = alarmState;
        Status = status;
        Value = value;
        RaisedAt = raisedAt;
        AcknowledgedAt = acknowledgedAt;
        ClearedAt = clearedAt;
        AcknowledgedBy = acknowledgedBy;
    }

    /// <summary>记录标识。</summary>
    public Guid Id { get; }

    /// <summary>点限定名，格式为 <c>设备.点</c>。</summary>
    public string QualifiedName { get; }

    /// <summary>点短名。</summary>
    public string PointName { get; }

    /// <summary>所属设备名。</summary>
    public string DeviceName { get; }

    /// <summary>触发时的高低报状态。</summary>
    public PointAlarmState AlarmState { get; }

    /// <summary>当前队列状态。</summary>
    public PointAlarmStatus Status { get; }

    /// <summary>触发或最近一次刷新时的工程值。</summary>
    public object? Value { get; }

    /// <summary>首次进入越限的时间。</summary>
    public DateTimeOffset RaisedAt { get; }

    /// <summary>确认时间。</summary>
    public DateTimeOffset? AcknowledgedAt { get; }

    /// <summary>复归时间。</summary>
    public DateTimeOffset? ClearedAt { get; }

    /// <summary>确认人。未传入时为 <c>null</c>。</summary>
    public string? AcknowledgedBy { get; }

    /// <summary>是否仍处于活动队列（未复归）。</summary>
    public bool IsOpen => Status is PointAlarmStatus.Active or PointAlarmStatus.Acknowledged;
}
