namespace Zeus;

/// <summary>
/// MEWTOCOL 响应返回错误码时抛出的异常。
/// </summary>
public sealed class MewtocolException : ZeusProtocolException
{
    /// <summary>创建 MEWTOCOL 异常。</summary>
    public MewtocolException(string command, byte errorCode)
        : base($"MEWTOCOL 命令 {command} 返回错误码 {errorCode:X2}：{Describe(errorCode)}。")
    {
        Command = command;
        ErrorCode = errorCode;
    }

    /// <summary>命令码。</summary>
    public string Command { get; }

    /// <summary>错误码。</summary>
    public byte ErrorCode { get; }

    private static string Describe(byte code)
        => code switch
        {
            0x20 => "不支持的命令",
            0x21 => "不能处理当前命令",
            0x22 => "命令错误",
            0x23 => "设备忙",
            0x24 => "应答超时",
            0x25 => "传输格式错误",
            0x26 => "地址越界或区域不支持",
            0x27 => "数据格式或数据数量错误",
            0x28 => "登录或注册错误",
            0x29 => "PLC 模式不允许执行",
            _ => "未映射的 MEWTOCOL 错误码"
        };
}
