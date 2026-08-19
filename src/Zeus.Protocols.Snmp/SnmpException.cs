namespace Zeus;

/// <summary>SNMP 协议层异常，包含 PDU 错误状态和出错 varbind 序号。</summary>
public sealed class SnmpException : ZeusProtocolException
{
    /// <summary>创建 SNMP 异常。</summary>
    public SnmpException(SnmpErrorStatus status, int errorIndex)
        : base($"SNMP 请求失败：{status}，error-index={errorIndex}。")
    {
        Status = status;
        ErrorIndex = errorIndex;
    }

    /// <summary>错误状态。</summary>
    public SnmpErrorStatus Status { get; }

    /// <summary>出错 varbind 的 1 基序号。0 表示未指定。</summary>
    public int ErrorIndex { get; }
}
