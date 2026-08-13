namespace Zeus;

/// <summary>
/// 常见 Modbus 异常码。
/// </summary>
public enum ModbusExceptionCode : byte
{
    /// <summary>非法功能。</summary>
    IllegalFunction = 0x01,

    /// <summary>非法数据地址。</summary>
    IllegalDataAddress = 0x02,

    /// <summary>非法数据值。</summary>
    IllegalDataValue = 0x03,

    /// <summary>从站设备故障。</summary>
    SlaveDeviceFailure = 0x04
}
