using System.IO.Ports;

namespace Zeus;

/// <summary>
/// 串口通道选项。未指定的字段使用工控现场最常见的 8N1 默认值，避免用户为跑通示例去查手册。
/// </summary>
public sealed class SerialPortOptions
{
    /// <summary>操作系统端口名，例如 <c>COM3</c> 或 <c>/dev/ttyUSB0</c>。</summary>
    public string PortName { get; set; } = "COM1";

    /// <summary>波特率，默认 115200。</summary>
    public int BaudRate { get; set; } = 115200;

    /// <summary>数据位，默认 8。</summary>
    public int DataBits { get; set; } = 8;

    /// <summary>校验位，默认无校验。</summary>
    public Parity Parity { get; set; } = Parity.None;

    /// <summary>停止位，默认 1 位。</summary>
    public StopBits StopBits { get; set; } = StopBits.One;

    /// <summary>读取超时（毫秒）。-1 表示无限等待。</summary>
    public int ReadTimeoutMilliseconds { get; set; } = 1000;

    /// <summary>写入超时（毫秒）。</summary>
    public int WriteTimeoutMilliseconds { get; set; } = 1000;
}
