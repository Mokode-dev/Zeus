namespace Zeus;

/// <summary>
/// DL/T 645 从站返回异常应答时抛出的异常。
/// </summary>
public sealed class Dlt645Exception : ZeusProtocolException
{
    /// <summary>创建 DL/T 645 异常。</summary>
    public Dlt645Exception(byte command, byte errorCode)
        : base($"DL/T 645 命令 0x{command:X2} 返回异常码 0x{errorCode:X2}：{Describe(errorCode)}。")
    {
        Command = command;
        ErrorCode = errorCode;
    }

    /// <summary>请求控制码。</summary>
    public byte Command { get; }

    /// <summary>异常码。</summary>
    public byte ErrorCode { get; }

    private static string Describe(byte code)
        => code switch
        {
            0x01 => "其他错误",
            0x02 => "无请求数据",
            0x04 => "密码错或未授权",
            0x08 => "通信速率不能更改",
            0x10 => "年时区数超",
            0x20 => "日时段数超",
            0x40 => "费率数超",
            _ => "未映射的 DL/T 645 异常码"
        };
}
