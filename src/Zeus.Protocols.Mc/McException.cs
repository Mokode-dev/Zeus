namespace Zeus;

/// <summary>
/// MC 协议响应返回非零结束码。
/// </summary>
public sealed class McException : ZeusProtocolException
{
    /// <summary>
    /// 创建 MC 协议异常。
    /// </summary>
    /// <param name="endCode">MC 响应结束码。</param>
    public McException(ushort endCode)
        : base($"MC 协议返回结束码 0x{endCode:X4}。请检查 PLC 软元件地址、点数、CPU 状态或访问权限。")
    {
        EndCode = endCode;
    }

    /// <summary>MC 响应结束码。</summary>
    public ushort EndCode { get; }
}
