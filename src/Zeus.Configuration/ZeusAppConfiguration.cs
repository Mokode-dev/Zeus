using System.Text.Json.Serialization;

namespace Zeus;

/// <summary>
/// Zeus 工程配置根对象，对应一份 JSON 文件。
/// 通道与设备在启动时一次性装载；采集间隔可热更新。
/// </summary>
public sealed class ZeusAppConfiguration
{
    /// <summary>周期采集。</summary>
    public AcquisitionConfiguration Acquisition { get; set; } = new();

    /// <summary>传输通道列表。</summary>
    public List<ChannelConfiguration> Channels { get; set; } = [];

    /// <summary>设备列表。引用的通道必须先出现在 <see cref="Channels"/> 中。</summary>
    public List<DeviceConfiguration> Devices { get; set; } = [];
}

/// <summary>
/// 采集循环配置。
/// </summary>
public sealed class AcquisitionConfiguration
{
    /// <summary>两轮间隔（毫秒），默认 500。</summary>
    public int IntervalMilliseconds { get; set; } = 500;

    /// <summary>启动后是否立刻采第一轮，默认 true。</summary>
    public bool PollImmediately { get; set; } = true;
}

/// <summary>
/// 一条通道的配置。
/// </summary>
public sealed class ChannelConfiguration
{
    /// <summary>通道名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 类型：<c>virtual</c>、<c>serial</c>、<c>tcp</c>。
    /// </summary>
    public string Type { get; set; } = "virtual";

    /// <summary>串口名，例如 COM3。仅 serial。</summary>
    public string? PortName { get; set; }

    /// <summary>波特率，默认 115200。仅 serial。</summary>
    public int BaudRate { get; set; } = 115200;

    /// <summary>TCP 主机。仅 tcp。</summary>
    public string? Host { get; set; }

    /// <summary>TCP 端口，默认 502。仅 tcp。</summary>
    public int Port { get; set; } = 502;

    /// <summary>
    /// 虚拟通道挂接的从站。当前仅支持 <c>modbus</c>。
    /// </summary>
    public string? Responder { get; set; }

    /// <summary>虚拟 Modbus 从站地址，默认 1。</summary>
    public byte UnitId { get; set; } = 1;

    /// <summary>虚拟从站封装：<c>rtu</c> 或 <c>tcp</c>，默认 rtu。</summary>
    public string Transport { get; set; } = "rtu";
}

/// <summary>
/// 一台设备的配置。
/// </summary>
public sealed class DeviceConfiguration
{
    /// <summary>设备名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>绑定的通道名。</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>类型：<c>modbus-rtu</c> 或 <c>modbus-tcp</c>。</summary>
    public string Type { get; set; } = "modbus-rtu";

    /// <summary>从站/单元标识，默认 1。</summary>
    public byte UnitId { get; set; } = 1;

    /// <summary>应答超时（毫秒）。省略则使用协议默认 1000。</summary>
    public int? TimeoutMilliseconds { get; set; }

    /// <summary>周期采集点。</summary>
    public List<PointConfiguration> Points { get; set; } = [];
}

/// <summary>
/// 一个采集点的配置。
/// </summary>
public sealed class PointConfiguration
{
    /// <summary>点名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 数据区：<c>holding</c>、<c>input</c>、<c>coil</c>、<c>discrete</c>。
    /// </summary>
    public string Table { get; set; } = "holding";

    /// <summary>0 基地址。</summary>
    public ushort Address { get; set; }

    /// <summary>
    /// 寄存器换算系数。例如 0.1 表示原始值乘 0.1 后写入点表。
    /// 仅对保持/输入寄存器有效；省略则保留原始 ushort。
    /// </summary>
    [JsonPropertyName("scale")]
    public double? Scale { get; set; }
}
