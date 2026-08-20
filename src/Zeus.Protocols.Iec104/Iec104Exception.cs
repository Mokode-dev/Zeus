namespace Zeus;

/// <summary>
/// IEC 60870-5-104 对端返回了无法接受的确认或应用服务数据单元。
/// </summary>
public sealed class Iec104Exception : ZeusProtocolException
{
    /// <summary>创建 IEC104 协议异常。</summary>
    public Iec104Exception(byte typeId, Iec104CauseOfTransmission cause)
        : base($"IEC104 ASDU 类型 0x{typeId:X2} 返回传送原因 {cause}，未得到期望确认。")
    {
        TypeId = typeId;
        Cause = cause;
    }

    /// <summary>创建链路层或会话异常，例如 t1 超时。</summary>
    public Iec104Exception(string message)
        : base(message)
    {
    }

    /// <summary>ASDU 类型标识。</summary>
    public byte TypeId { get; }

    /// <summary>传送原因。</summary>
    public Iec104CauseOfTransmission Cause { get; }
}
