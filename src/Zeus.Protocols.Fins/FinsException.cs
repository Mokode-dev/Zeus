namespace Zeus;

/// <summary>
/// FINS 响应返回非零结束码时抛出的异常。
/// </summary>
public sealed class FinsException : ZeusProtocolException
{
    /// <summary>创建 FINS 异常。</summary>
    public FinsException(ushort command, ushort endCode)
        : base($"FINS 命令 0x{command:X4} 返回结束码 0x{endCode:X4}：{Describe(endCode)}。")
    {
        Command = command;
        EndCode = endCode;
    }

    /// <summary>命令码。</summary>
    public ushort Command { get; }

    /// <summary>结束码。</summary>
    public ushort EndCode { get; }

    private static string Describe(ushort code)
        => code switch
        {
            0x0000 => "正常完成",
            0x0001 => "服务被取消",
            0x0101 => "本地节点不在网络中",
            0x0102 => "令牌超时",
            0x0103 => "重试失败",
            0x0104 => "发送缓冲区不足",
            0x0201 => "目标节点不在网络中",
            0x0202 => "目标节点忙",
            0x0203 => "响应超时",
            0x0301 => "通信控制器错误",
            0x0401 => "未定义命令",
            0x0402 => "不支持的命令",
            0x0501 => "路由表错误",
            0x1001 => "命令太长",
            0x1002 => "命令太短",
            0x1003 => "元素/数据数量不匹配",
            0x1004 => "命令格式错误",
            0x1101 => "区域类型错误或只读",
            0x1103 => "起始地址超出范围",
            0x1104 => "地址范围超出",
            0x110B => "响应太长",
            0x2101 => "只读区域不能写入",
            _ => "未映射的 FINS 结束码"
        };
}
