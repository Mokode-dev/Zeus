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

    /// <summary>
    /// 成功采样落盘的 JSONL 路径。省略时只保留内存历史。
    /// </summary>
    public string? PointHistoryFile { get; set; }
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
    /// 虚拟通道挂接的从站。支持 <c>modbus</c>、<c>mc</c>、<c>s7</c>、<c>fins</c>、<c>host-link</c>、<c>mewtocol</c>、<c>dlt645</c>、<c>iec104</c>、<c>mqtt</c>、<c>snmp</c>。
    /// </summary>
    public string? Responder { get; set; }

    /// <summary>虚拟 Modbus 从站地址，默认 1。</summary>
    public byte UnitId { get; set; } = 1;

    /// <summary>虚拟从站封装：<c>rtu</c>、<c>tcp</c> 或 <c>ascii</c>，默认 rtu。</summary>
    public string Transport { get; set; } = "rtu";

    /// <summary>DL/T 645 虚拟表计地址，12 位十进制字符串，默认 000000000001。</summary>
    public string MeterAddress { get; set; } = "000000000001";

    /// <summary>IEC104 虚拟站公共地址，默认 1。</summary>
    public int CommonAddress { get; set; } = 1;

    /// <summary>SNMP 虚拟 Agent 读 community，默认 public。</summary>
    public string SnmpCommunity { get; set; } = "public";

    /// <summary>SNMP 虚拟 Agent 写 community。省略时沿用 <see cref="SnmpCommunity"/>。</summary>
    public string? SnmpWriteCommunity { get; set; }
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

    /// <summary>类型：<c>modbus-rtu</c>、<c>modbus-tcp</c>、<c>modbus-ascii</c>、<c>mitsubishi-mc</c>、<c>siemens-s7</c>、<c>omron-fins</c>、<c>omron-host-link</c>、<c>panasonic-mewtocol</c>、<c>ethernet-ip</c>、<c>dlt645</c>、<c>iec104</c>、<c>mqtt</c> 或 <c>snmp</c>。</summary>
    public string Type { get; set; } = "modbus-rtu";

    /// <summary>从站/单元标识，默认 1。Host Link 使用 0-31 单元号；MEWTOCOL 使用 1-99 站号。</summary>
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

    /// <summary>S7 机架号，默认 0。仅 Siemens S7。</summary>
    public int Rack { get; set; }

    /// <summary>S7 槽号，默认 1。仅 Siemens S7。</summary>
    public int Slot { get; set; } = 1;

    /// <summary>S7 本地 TSAP，默认 0x0100。仅 Siemens S7。</summary>
    public int LocalTsap { get; set; } = 0x0100;

    /// <summary>S7 远端 TSAP；省略时由 rack/slot 自动计算。仅 Siemens S7。</summary>
    public int? RemoteTsap { get; set; }

    /// <summary>S7 请求协商的 PDU 长度，默认 480。仅 Siemens S7。</summary>
    public int RequestedPduLength { get; set; } = 480;

    /// <summary>FINS 目标网络号 DNA，默认 0。仅 Omron FINS。</summary>
    public int DestinationNetwork { get; set; }

    /// <summary>FINS 目标节点号 DA1。UDP 通常填 PLC 节点号；TCP 可由握手自动填充。</summary>
    public int DestinationNode { get; set; }

    /// <summary>FINS 目标单元号 DA2，CPU 单元通常为 0。</summary>
    public int DestinationUnit { get; set; }

    /// <summary>FINS 源网络号 SNA，默认 0。</summary>
    public int SourceNetwork { get; set; }

    /// <summary>FINS 源节点号 SA1。UDP 通常填本机节点号；TCP 可由握手自动填充。</summary>
    public int SourceNode { get; set; }

    /// <summary>FINS 源单元号 SA2，默认 0。</summary>
    public int SourceUnit { get; set; }

    /// <summary>FINS 网关计数 GCT，默认 2。</summary>
    public int GatewayCount { get; set; } = 2;

    /// <summary>FINS ICF 控制字段，默认 0x80。</summary>
    public int InformationControlField { get; set; } = 0x80;

    /// <summary>FINS/TCP 节点握手请求的客户端节点号，0 表示请求服务端分配。</summary>
    public int TcpRequestedClientNode { get; set; }

    /// <summary>FINS/TCP 是否使用节点地址握手，默认 true。</summary>
    public bool UseTcpNodeAddressHandshake { get; set; } = true;

    /// <summary>FINS / Host Link / MEWTOCOL 32 位值字序：<c>high-word-first</c> 或 <c>low-word-first</c>。</summary>
    public string WordOrder { get; set; } = "high-word-first";

    /// <summary>DL/T 645 表地址，12 位十进制字符串。仅 DL/T 645。</summary>
    public string MeterAddress { get; set; } = "000000000001";

    /// <summary>DL/T 645 写数据密码，8 位十进制 BCD。仅 DL/T 645。</summary>
    public string Password { get; set; } = "00000000";

    /// <summary>DL/T 645 写数据操作者代码，8 位十进制 BCD。仅 DL/T 645。</summary>
    public string OperatorCode { get; set; } = "00000000";

    /// <summary>DL/T 645 帧前导 0xFE 数量，默认 4。仅 DL/T 645。</summary>
    public int WakeUpPreambleCount { get; set; } = 4;

    /// <summary>IEC104 公共地址，默认 1。仅 IEC104。</summary>
    public int CommonAddress { get; set; } = 1;

    /// <summary>IEC104 传送原因源发地址，默认 0。仅 IEC104。</summary>
    public int OriginatorAddress { get; set; }

    /// <summary>IEC104 总召唤限定词 QOI，默认 20。仅 IEC104。</summary>
    public int InterrogationQualifier { get; set; } = 20;

    /// <summary>IEC104 t1（毫秒）：I/U 格式等待确认超时，默认 15000。0 表示关闭。仅 IEC104。</summary>
    public int T1Milliseconds { get; set; } = 15000;

    /// <summary>IEC104 t2（毫秒）：最迟发送 S 格式确认的等待，默认 10000。0 表示仅按 w 窗口确认。仅 IEC104。</summary>
    public int T2Milliseconds { get; set; } = 10000;

    /// <summary>IEC104 t3（毫秒）：空闲后发送 TESTFR act 的间隔，默认 20000。0 表示关闭保活。仅 IEC104。</summary>
    public int T3Milliseconds { get; set; } = 20000;

    /// <summary>IEC104 k：未确认 I 格式上限，默认 12。仅 IEC104。</summary>
    public int MaxUnacknowledgedIFrames { get; set; } = 12;

    /// <summary>IEC104 w：最迟在收到这么多 I 格式后必须确认，默认 8。仅 IEC104。</summary>
    public int AcknowledgeWindow { get; set; } = 8;

    /// <summary>MQTT 客户端标识。省略时由设备名生成。仅 MQTT。</summary>
    public string? MqttClientId { get; set; }

    /// <summary>MQTT 可选用户名。仅 MQTT。</summary>
    public string? MqttUsername { get; set; }

    /// <summary>MQTT 可选密码。仅 MQTT。</summary>
    public string? MqttPassword { get; set; }

    /// <summary>MQTT 保活秒数，默认 60。仅 MQTT。</summary>
    public int MqttKeepAliveSeconds { get; set; } = 60;

    /// <summary>MQTT 是否清理会话，默认 true。仅 MQTT。</summary>
    public bool MqttCleanSession { get; set; } = true;

    /// <summary>MQTT 遗嘱主题。仅 MQTT。</summary>
    public string? MqttWillTopic { get; set; }

    /// <summary>MQTT UTF-8 遗嘱载荷。仅 MQTT。</summary>
    public string? MqttWillPayload { get; set; }

    /// <summary>MQTT 遗嘱 QoS：0、1、2。仅 MQTT。</summary>
    public string MqttWillQos { get; set; } = "0";

    /// <summary>MQTT 是否保留遗嘱。仅 MQTT。</summary>
    public bool MqttWillRetain { get; set; }

    /// <summary>MQTT 最大接收报文大小，默认 1 MiB。仅 MQTT。</summary>
    public int MqttMaximumPacketSize { get; set; } = 1024 * 1024;

    /// <summary>MQTT 是否自动保活，默认 true。仅 MQTT。</summary>
    public bool MqttAutomaticKeepAlive { get; set; } = true;

    /// <summary>MQTT 是否在通道恢复后自动重连并恢复订阅，默认 true。仅 MQTT。</summary>
    public bool MqttAutomaticReconnect { get; set; } = true;

    /// <summary>SNMP 读 community，默认 public。仅 SNMP。</summary>
    public string SnmpCommunity { get; set; } = "public";

    /// <summary>SNMP 写 community。省略时沿用 <see cref="SnmpCommunity"/>。仅 SNMP。</summary>
    public string? SnmpWriteCommunity { get; set; }

    /// <summary>SNMP 初始 request-id，默认 1。仅 SNMP。</summary>
    public int SnmpInitialRequestId { get; set; } = 1;

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

    /// <summary>MQTT 主题。省略时使用点名。仅 MQTT。</summary>
    public string? Topic { get; set; }

    /// <summary>SNMP OID，例如 1.3.6.1.2.1.1.5.0。仅 SNMP。</summary>
    public string? Oid { get; set; }

    /// <summary>MQTT 订阅与写回 QoS：0、1、2。仅 MQTT。</summary>
    public string MqttQos { get; set; } = "0";

    /// <summary>MQTT 点写回时是否设置 retain，默认 true。仅 MQTT。</summary>
    public bool MqttRetain { get; set; } = true;

    /// <summary>EtherNet/IP 标签名。省略时使用 <see cref="Name"/>。</summary>
    public string? TagName { get; set; }

    /// <summary>EtherNet/IP 标签名短写，JSON 字段名为 <c>tag</c>。</summary>
    [JsonPropertyName("tag")]
    public string? Tag { get; set; }

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
    /// Siemens S7 存储区：<c>db</c>、<c>m</c>、<c>i</c>、<c>q</c>。
    /// Omron FINS 存储区：<c>cio</c>、<c>wr</c>、<c>hr</c>、<c>ar</c>、<c>dm</c>、<c>tc</c>、<c>em</c>、<c>em0</c> 等。
    /// MEWTOCOL 存储区：<c>dt</c>、<c>ld</c>、<c>fl</c>、<c>x</c>、<c>y</c>、<c>r</c>、<c>l</c>。
    /// Modbus 与 Mitsubishi MC 点忽略该字段。
    /// </summary>
    public string? Area { get; set; }

    /// <summary>Siemens S7 DB 块号。JSON 字段名为 <c>db</c>。</summary>
    [JsonPropertyName("db")]
    public int DbNumber { get; set; }

    /// <summary>Siemens S7 Bool 位偏移，0–7。JSON 字段名为 <c>bit</c>。</summary>
    [JsonPropertyName("bit")]
    public int BitOffset { get; set; }

    /// <summary>Siemens S7 / Omron FINS / Host Link / MEWTOCOL / EtherNet/IP / DL/T 645 / IEC104 数据类型。DL/T 645 支持 <c>bcd</c>、<c>raw</c>；IEC104 支持 <c>single-point</c>、<c>normalized</c>、<c>scaled</c>、<c>short-float</c>。</summary>
    public string DataType { get; set; } = "word";

    /// <summary>DL/T 645 数据项有效载荷长度，不含 4 字节数据项标识，默认 4。</summary>
    public int DataLength { get; set; } = 4;

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
