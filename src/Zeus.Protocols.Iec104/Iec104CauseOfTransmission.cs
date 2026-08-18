namespace Zeus;

/// <summary>
/// IEC 60870-5-104 常用传送原因。
/// </summary>
public enum Iec104CauseOfTransmission : ushort
{
    /// <summary>周期/循环。</summary>
    Periodic = 1,

    /// <summary>背景扫描。</summary>
    BackgroundScan = 2,

    /// <summary>突发。</summary>
    Spontaneous = 3,

    /// <summary>初始化完成。</summary>
    Initialized = 4,

    /// <summary>请求。</summary>
    Request = 5,

    /// <summary>激活。</summary>
    Activation = 6,

    /// <summary>激活确认。</summary>
    ActivationConfirmation = 7,

    /// <summary>停止激活。</summary>
    Deactivation = 8,

    /// <summary>停止激活确认。</summary>
    DeactivationConfirmation = 9,

    /// <summary>激活终止。</summary>
    ActivationTermination = 10,

    /// <summary>响应总召唤。</summary>
    InterrogatedByStation = 20
}
