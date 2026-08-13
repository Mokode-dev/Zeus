namespace Zeus;

/// <summary>
/// 从站返回异常 PDU 时抛出。
/// </summary>
public sealed class ModbusException : ZeusProtocolException
{
    /// <summary>
    /// 创建 Modbus 异常。
    /// </summary>
    /// <param name="unitId">单元/从站地址。</param>
    /// <param name="function">原始功能码（不含 0x80）。</param>
    /// <param name="code">从站异常码。</param>
    public ModbusException(byte unitId, byte function, ModbusExceptionCode code)
        : base($"从站 {unitId} 拒绝功能 0x{function:X2}：{Describe(code)}。请核对地址范围与功能码是否受该设备支持。")
    {
        UnitId = unitId;
        Function = function;
        Code = code;
    }

    /// <summary>出错的从站地址。</summary>
    public byte UnitId { get; }

    /// <summary>被拒绝的功能码。</summary>
    public byte Function { get; }

    /// <summary>从站异常码。</summary>
    public ModbusExceptionCode Code { get; }

    private static string Describe(ModbusExceptionCode code)
    {
        return code switch
        {
            ModbusExceptionCode.IllegalFunction => "非法功能（01）",
            ModbusExceptionCode.IllegalDataAddress => "非法数据地址（02）",
            ModbusExceptionCode.IllegalDataValue => "非法数据值（03）",
            ModbusExceptionCode.SlaveDeviceFailure => "从站设备故障（04）",
            _ => $"异常码 0x{(byte)code:X2}"
        };
    }
}
