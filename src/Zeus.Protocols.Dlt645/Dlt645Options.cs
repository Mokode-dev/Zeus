namespace Zeus;

/// <summary>
/// DL/T 645-2007 会话选项。
/// </summary>
public sealed class Dlt645Options
{
    /// <summary>12 位十进制表地址。协议帧中按低位字节在前发送。</summary>
    public string MeterAddress { get; set; } = "000000000001";

    /// <summary>帧前导唤醒字节 0xFE 数量，默认 4 个。某些 TCP 透传网关可设为 0。</summary>
    public int WakeUpPreambleCount { get; set; } = 4;

    /// <summary>写数据命令密码，8 位十进制 BCD，默认 00000000。</summary>
    public string Password { get; set; } = "00000000";

    /// <summary>写数据命令操作者代码，8 位十进制 BCD，默认 00000000。</summary>
    public string OperatorCode { get; set; } = "00000000";
}
