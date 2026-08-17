using System.Globalization;
using System.Text.Json;
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
    /// 类型：<c>virtual</c>、<c>serial</c>、<c>tcp</c>、<c>tcp-server</c>、<c>udp</c>、<c>udp-server</c>。
    /// </summary>
    public string Type { get; set; } = "virtual";

    /// <summary>串口名，例如 COM3。仅 serial。</summary>
    public string? PortName { get; set; }

    /// <summary>波特率，默认 115200。仅 serial。</summary>
    public int BaudRate { get; set; } = 115200;

    /// <summary>TCP/UDP 对端主机。仅 tcp、udp 客户端。</summary>
    public string? Host { get; set; }

    /// <summary>TCP/UDP 端口，默认 502。tcp/udp 为对端端口，tcp-server/udp-server 可作为监听端口。</summary>
    public int Port { get; set; } = 502;

    /// <summary>TCP/UDP 本地监听地址。仅 tcp-server、udp-server。</summary>
    public string? LocalAddress { get; set; }

    /// <summary>TCP/UDP 本地绑定或监听端口，0 表示自动分配。仅 udp、tcp-server、udp-server。</summary>
    public int LocalPort { get; set; }

    /// <summary>
    /// 虚拟通道挂接的从站。支持 <c>modbus</c>、<c>mc</c>。
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

    /// <summary>类型：<c>modbus-rtu</c>、<c>modbus-tcp</c> 或 <c>mitsubishi-mc</c>。</summary>
    public string Type { get; set; } = "modbus-rtu";

    /// <summary>从站/单元标识，默认 1。</summary>
    public byte UnitId { get; set; } = 1;

    /// <summary>应答超时（毫秒）。省略则使用协议默认 1000。</summary>
    public int? TimeoutMilliseconds { get; set; }

    /// <summary>MC 帧类型：<c>1e</c>、<c>3e</c>、<c>4e</c>。仅 Mitsubishi MC。</summary>
    public string FrameType { get; set; } = "3e";

    /// <summary>MC 编码：<c>binary</c> 或 <c>ascii</c>。仅 Mitsubishi MC。</summary>
    public string Encoding { get; set; } = "binary";

    /// <summary>MC 网络号，默认 0。仅 Mitsubishi MC。</summary>
    public int NetworkNumber { get; set; }

    /// <summary>MC PC 号，默认 255。仅 Mitsubishi MC。</summary>
    public int PcNumber { get; set; } = 0xFF;

    /// <summary>MC I/O 号，默认 0x03FF。仅 Mitsubishi MC。</summary>
    public int IoNumber { get; set; } = 0x03FF;

    /// <summary>MC 站号，默认 0。仅 Mitsubishi MC。</summary>
    public int StationNumber { get; set; }

    /// <summary>MC 监视定时器，单位 250ms，默认 0x0010。仅 Mitsubishi MC。</summary>
    public int MonitoringTimer { get; set; } = 0x0010;

    /// <summary>MC 4E 序列号，默认 0。仅 Mitsubishi MC。</summary>
    public int SerialNumber { get; set; }

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
    /// Modbus 数据区：<c>holding</c>、<c>input</c>、<c>coil</c>、<c>discrete</c>。
    /// Mitsubishi MC 也可兼容使用 <c>D</c>、<c>M</c> 等软元件代码；推荐改用 <see cref="DeviceCode"/>。
    /// </summary>
    public string Table { get; set; } = "holding";

    /// <summary>
    /// Mitsubishi MC 软元件代码：<c>D</c>、<c>M</c>、<c>X</c>、<c>Y</c>、<c>W</c>、<c>R</c>、<c>ZR</c>。
    /// Modbus 点忽略该字段。
    /// </summary>
    public string? DeviceCode { get; set; }

    /// <summary>0 基地址。</summary>
    [JsonConverter(typeof(FlexibleInt32JsonConverter))]
    public int Address { get; set; }

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

    /// <summary>
    /// 是否允许按点名写回。默认 false。
    /// 仅 <c>holding</c> 与 <c>coil</c> 可设为 true；输入寄存器和离散输入始终只读。
    /// </summary>
    public bool Writable { get; set; }
}

internal sealed class FlexibleInt32JsonConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            return number;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString()?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                throw new JsonException("address 不能为空字符串。");
            }

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return int.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (ex is FormatException or OverflowException)
                {
                    throw new JsonException("address 十六进制字符串格式无效。", ex);
                }
            }

            try
            {
                return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                throw new JsonException("address 整数字符串格式无效。", ex);
            }
        }

        throw new JsonException("address 必须是整数，或形如 \"0x10\" 的十六进制字符串。");
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
