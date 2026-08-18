namespace Zeus;

/// <summary>
/// Host Link 响应返回非零结束码时抛出的异常。
/// </summary>
public sealed class HostLinkException : ZeusProtocolException
{
    /// <summary>创建 Host Link 异常。</summary>
    public HostLinkException(string command, byte endCode)
        : base($"Host Link 命令 {command} 返回结束码 0x{endCode:X2}：{Describe(endCode)}。")
    {
        Command = command;
        EndCode = endCode;
    }

    /// <summary>命令头码。</summary>
    public string Command { get; }

    /// <summary>结束码。</summary>
    public byte EndCode { get; }

    private static string Describe(byte code)
        => code switch
        {
            0x00 => "正常完成",
            0x01 => "不能在当前 PLC 模式下执行",
            0x02 => "未在监视模式下执行",
            0x04 => "地址越界或区域不支持",
            0x13 => "FCS 校验错误",
            0x14 => "命令格式错误",
            0x15 => "数据格式或数据数量错误",
            0x16 => "命令不支持",
            _ => "未映射的 Host Link 结束码"
        };
}
