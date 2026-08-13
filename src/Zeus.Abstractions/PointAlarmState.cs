namespace Zeus;

/// <summary>
/// 点值相对于报警限的状态。
/// </summary>
public enum PointAlarmState
{
    /// <summary>未配置报警限。</summary>
    Disabled = 0,

    /// <summary>已配置报警限，当前值在允许范围内。</summary>
    Normal = 1,

    /// <summary>当前值低于下限。</summary>
    Low = 2,

    /// <summary>当前值高于上限。</summary>
    High = 3,

    /// <summary>已配置报警限，但当前值为空或不能转换为数值。</summary>
    Unknown = 4
}
