using System.Text.Json.Serialization;

namespace Zeus;

/// <summary>
/// Zeus 工程配置根对象，对应一份 JSON 文件。
/// 通道、设备与采集均可在监视开启时热更新；拓扑变更会增删运行中的实例。
/// </summary>
public sealed class ZeusAppConfiguration
{
    /// <summary>周期采集。</summary>
    public AcquisitionConfiguration Acquisition { get; set; } = new();

    /// <summary>通道故障后的自动重连。省略则使用框架默认（开启、1 秒起、上限 30 秒）。</summary>
    public ReconnectConfiguration Reconnect { get; set; } = new();

    /// <summary>传输通道列表。</summary>
    public List<ChannelConfiguration> Channels { get; set; } = [];

    /// <summary>设备列表。引用的通道必须先出现在 <see cref="Channels"/> 中。</summary>
    public List<DeviceConfiguration> Devices { get; set; } = [];
}

/// <summary>
/// 通道故障自动重连的 JSON 配置。
/// </summary>
public sealed class ReconnectConfiguration
{
    /// <summary>是否启用自动重连，默认 true。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>首次重连等待（毫秒），默认 1000。</summary>
    public int InitialDelayMilliseconds { get; set; } = 1000;

    /// <summary>退避上限（毫秒），默认 30000。</summary>
    public int MaxDelayMilliseconds { get; set; } = 30000;

    /// <summary>连续失败时的等待倍数，默认 2。</summary>
    public double BackoffMultiplier { get; set; } = 2;
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
    /// 类型：<c>virtual</c>、<c>serial</c>、<c>tcp</c>、<c>udp</c>。
    /// </summary>
    public string Type { get; set; } = "virtual";

    /// <summary>串口名，例如 COM3。仅 serial。</summary>
    public string? PortName { get; set; }

    /// <summary>波特率，默认 115200。仅 serial。</summary>
    public int BaudRate { get; set; } = 115200;

    /// <summary>TCP/UDP 主机。仅 tcp、udp。</summary>
    public string? Host { get; set; }

    /// <summary>TCP/UDP 端口，默认 502。仅 tcp、udp。</summary>
    public int Port { get; set; } = 502;

    /// <summary>UDP 本地绑定端口，0 表示自动分配。仅 udp。</summary>
    public int LocalPort { get; set; }

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

    /// <summary>可选低报阈值。仅寄存器点支持，按换算后的值判断。</summary>
    public double? LowAlarmLimit { get; set; }

    /// <summary>可选高报阈值。仅寄存器点支持，按换算后的值判断。</summary>
    public double? HighAlarmLimit { get; set; }
}
