namespace Zeus;

/// <summary>
/// 报警记录在队列中的生命周期状态。
/// </summary>
public enum PointAlarmStatus
{
    /// <summary>点仍越限，尚未确认。</summary>
    Active = 0,

    /// <summary>操作员已确认，点仍越限。</summary>
    Acknowledged = 1,

    /// <summary>点已回到正常范围，无需再确认。</summary>
    Cleared = 2
}
