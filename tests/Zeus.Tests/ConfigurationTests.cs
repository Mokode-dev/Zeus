using Microsoft.Extensions.DependencyInjection;
using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 验证 JSON 配置的校验、装载与采集间隔热更新。
/// </summary>
public sealed class ConfigurationTests
{
    private const string ValidJson = """
        {
          "acquisition": { "intervalMilliseconds": 200, "pollImmediately": true },
          "channels": [
            { "name": "bus", "type": "virtual", "responder": "modbus", "unitId": 1, "transport": "rtu" }
          ],
          "devices": [
            {
              "name": "oven",
              "channel": "bus",
              "type": "modbus-rtu",
              "unitId": 1,
                "points": [
                { "name": "temperature", "table": "holding", "address": 0, "scale": 0.1, "lowAlarmLimit": 10, "highAlarmLimit": 80 },
                { "name": "setpoint", "table": "holding", "address": 1, "scale": 0.1, "writable": true },
                { "name": "heater", "table": "coil", "address": 2, "writable": true }
              ]
            }
          ]
        }
        """;

    /// <summary>
    /// 合法 JSON 应登记通道、设备与点，并采到虚拟从站初值。
    /// </summary>
    [Fact]
    public async Task AddJson_CreatesChannelDeviceAndPoints()
    {
        await using var host = ZeusHost.Create(builder => builder.AddJson(ValidJson, "测试配置"));
        Assert.NotNull(host.Channels.Get("bus"));
        Assert.NotNull(host.Devices.Get<ModbusDevice>("oven"));

        await host.StartAsync();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        double? temperature = null;
        while (DateTime.UtcNow < deadline)
        {
            if (host.Points.TryGet<double>("temperature", out var value))
            {
                temperature = value;
                break;
            }

            await Task.Delay(20);
        }

        Assert.Equal(0d, temperature);
        Assert.Equal(PointAlarmState.Low, host.Points.Get("temperature").AlarmState);
        Assert.True(host.Points.Get("setpoint").Definition.Writable);
        Assert.True(host.Points.Get("heater").Definition.Writable);
        Assert.False(host.Points.Get("temperature").Definition.Writable);
        Assert.Equal(TimeSpan.FromMilliseconds(200), host.Services.GetRequiredService<AcquisitionOptions>().Interval);

        await host.Points.WriteAsync("setpoint", 12.5);
        Assert.Equal(12.5, host.Points.Get<double>("setpoint"), 3);
    }

    /// <summary>
    /// 只读数据区不能在 JSON 里标为可写。
    /// </summary>
    [Fact]
    public void WritableInputRegister_FailsAtLoad()
    {
        const string json = """
            {
              "channels": [ { "name": "bus", "type": "virtual" } ],
              "devices": [
                {
                  "name": "oven",
                  "channel": "bus",
                  "type": "modbus-rtu",
                  "points": [
                    { "name": "status", "table": "input", "address": 0, "writable": true }
                  ]
                }
              ]
            }
            """;

        var error = Assert.Throws<ZeusException>(() => ZeusConfigurationLoader.LoadJson(json, "可写配置"));
        Assert.Contains("writable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// JSON 报警限必须保持低限不高于高限。
    /// </summary>
    [Fact]
    public void PointAlarmLimitRange_FailsAtLoad()
    {
        const string json = """
            {
              "channels": [ { "name": "bus", "type": "virtual" } ],
              "devices": [
                {
                  "name": "oven",
                  "channel": "bus",
                  "type": "modbus-rtu",
                  "points": [
                    { "name": "temperature", "table": "holding", "address": 0, "lowAlarmLimit": 90, "highAlarmLimit": 80 }
                  ]
                }
              ]
            }
            """;

        var error = Assert.Throws<ZeusException>(() => ZeusConfigurationLoader.LoadJson(json, "报警配置"));
        Assert.Contains("lowAlarmLimit", error.Message, StringComparison.Ordinal);
        Assert.Contains("highAlarmLimit", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 设备引用不存在的通道必须在装载期失败。
    /// </summary>
    [Fact]
    public void MissingChannel_FailsAtLoad()
    {
        const string json = """
            {
              "channels": [],
              "devices": [ { "name": "oven", "channel": "bus", "type": "modbus-rtu" } ]
            }
            """;
        var error = Assert.Throws<ZeusException>(() => ZeusConfigurationLoader.LoadJson(json, "坏配置"));
        Assert.Contains("bus", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("channels", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 非法 JSON 应指出语法问题，而不是抛出原始序列化异常给用户。
    /// </summary>
    [Fact]
    public void InvalidJson_IsActionable()
    {
        var error = Assert.Throws<ZeusException>(() => ZeusConfigurationLoader.LoadJson("{", "坏配置"));
        Assert.Contains("合法 JSON", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 重复通道名必须失败。
    /// </summary>
    [Fact]
    public void DuplicateChannelName_FailsAtLoad()
    {
        const string json = """
            {
              "channels": [
                { "name": "bus", "type": "virtual" },
                { "name": "bus", "type": "virtual" }
              ]
            }
            """;
        var error = Assert.Throws<ZeusException>(() => ZeusConfigurationLoader.LoadJson(json));
        Assert.Contains("重复", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// JSON 配置可声明 UDP 通道，并保留本地端口选项。
    /// </summary>
    [Fact]
    public async Task AddJson_CreatesUdpChannel()
    {
        const string json = """
            {
              "channels": [
                { "name": "wireless", "type": "udp", "host": "127.0.0.1", "port": 1502, "localPort": 0 }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "UDP 配置"));
        Assert.IsType<UdpClientChannel>(host.Channels.Get("wireless"));
    }

    /// <summary>
    /// JSON 配置可声明 UDP 服务端通道。
    /// </summary>
    [Fact]
    public async Task AddJson_CreatesUdpServerChannel()
    {
        const string json = """
            {
              "channels": [
                { "name": "listener", "type": "udp-server", "localAddress": "127.0.0.1", "localPort": 0 }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "UDP 服务端配置"));
        Assert.IsType<UdpServerChannel>(host.Channels.Get("listener"));
    }

    /// <summary>
    /// JSON 配置可声明 TCP 服务端通道。
    /// </summary>
    [Fact]
    public async Task AddJson_CreatesTcpServerChannel()
    {
        const string json = """
            {
              "channels": [
                { "name": "listener", "type": "tcp-server", "localAddress": "127.0.0.1", "localPort": 0 }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "TCP 服务端配置"));
        Assert.IsType<TcpServerChannel>(host.Channels.Get("listener"));
    }

    /// <summary>
    /// JSON 配置可声明 Mitsubishi MC 设备，默认使用 3E Binary。
    /// </summary>
    [Fact]
    public async Task AddJson_CreatesMitsubishiMcDevice()
    {
        const string json = """
            {
              "channels": [
                { "name": "plc-link", "type": "virtual", "responder": "mc" }
              ],
              "devices": [
                { "name": "plc", "channel": "plc-link", "type": "mitsubishi-mc" }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "MC 配置"));
        await host.StartAsync();

        var plc = host.Devices.Get<McDevice>("plc");
        Assert.Equal(McFrameType.Frame3E, plc.Client.Options.FrameType);
        Assert.Equal(McDataEncoding.Binary, plc.Client.Options.DataEncoding);

        await plc.WriteDataRegistersAsync(100, [123]);
        Assert.Equal(new ushort[] { 123 }, await plc.ReadDataRegistersAsync(100, 1));
    }

    /// <summary>
    /// JSON 配置可声明 4E ASCII Mitsubishi MC 设备。
    /// </summary>
    [Fact]
    public async Task AddJson_CreatesMitsubishiMc4EAsciiDevice()
    {
        const string json = """
            {
              "channels": [
                { "name": "plc-link", "type": "virtual", "responder": "mitsubishi-mc" }
              ],
              "devices": [
                {
                  "name": "plc",
                  "channel": "plc-link",
                  "type": "mc",
                  "frameType": "4e",
                  "encoding": "ascii",
                  "serialNumber": 4660,
                  "networkNumber": 0,
                  "pcNumber": 255,
                  "ioNumber": 1023,
                  "stationNumber": 0,
                  "monitoringTimer": 16
                }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "MC 4E ASCII 配置"));
        await host.StartAsync();

        var plc = host.Devices.Get<McDevice>("plc");
        Assert.Equal(McFrameType.Frame4E, plc.Client.Options.FrameType);
        Assert.Equal(McDataEncoding.Ascii, plc.Client.Options.DataEncoding);
        Assert.Equal((ushort)4660, plc.Client.Options.SerialNumber);

        await plc.WriteLinkRegistersAsync(0x30, [456]);
        Assert.Equal(new ushort[] { 456 }, await plc.ReadLinkRegistersAsync(0x30, 1));
    }

    /// <summary>
    /// MC frameType 必须给出明确可选值。
    /// </summary>
    [Fact]
    public void InvalidMitsubishiMcFrameType_FailsAtLoad()
    {
        const string json = """
            {
              "channels": [ { "name": "plc-link", "type": "virtual", "responder": "mc" } ],
              "devices": [
                { "name": "plc", "channel": "plc-link", "type": "mitsubishi-mc", "frameType": "5e" }
              ]
            }
            """;

        var error = Assert.Throws<ZeusException>(() => ZeusConfigurationLoader.LoadJson(json, "坏 MC 配置"));
        Assert.Contains("frameType", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1e", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4e", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// MC encoding 必须给出明确可选值。
    /// </summary>
    [Fact]
    public void InvalidMitsubishiMcEncoding_FailsAtLoad()
    {
        const string json = """
            {
              "channels": [ { "name": "plc-link", "type": "virtual", "responder": "mc" } ],
              "devices": [
                { "name": "plc", "channel": "plc-link", "type": "mitsubishi-mc", "encoding": "utf8" }
              ]
            }
            """;

        var error = Assert.Throws<ZeusException>(() => ZeusConfigurationLoader.LoadJson(json, "坏 MC 配置"));
        Assert.Contains("encoding", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("binary", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ascii", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// JSON 配置可为 Mitsubishi MC 声明点表，并参与周期采集与写回。
    /// </summary>
    [Fact]
    public async Task AddJson_CreatesMitsubishiMcPoints()
    {
        const string json = """
            {
              "acquisition": { "intervalMilliseconds": 80, "pollImmediately": true },
              "channels": [ { "name": "plc-link", "type": "virtual", "responder": "mc" } ],
              "devices": [
                {
                  "name": "plc",
                  "channel": "plc-link",
                  "type": "mitsubishi-mc",
                  "points": [
                    { "name": "temperature", "deviceCode": "D", "address": 100, "scale": 0.1, "lowAlarmLimit": 1, "highAlarmLimit": 80, "writable": true },
                    { "name": "run", "deviceCode": "M", "address": 10, "writable": true },
                    { "name": "ready", "deviceCode": "X", "address": "0x10" }
                  ]
                }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "MC 点表配置"));
        await host.StartAsync();

        Assert.Equal(0d, await WaitForPointAsync<double>(host, "temperature"));
        Assert.Equal(PointAlarmState.Low, host.Points.Get("temperature").AlarmState);
        Assert.True(host.Points.Get("temperature").Definition.Writable);
        Assert.True(host.Points.Get("run").Definition.Writable);
        Assert.False(host.Points.Get("ready").Definition.Writable);

        await host.Points.WriteAsync("temperature", 12.3);
        await host.Points.WriteAsync("run", true);

        var plc = host.Devices.Get<McDevice>("plc");
        Assert.Equal(new ushort[] { 123 }, await plc.ReadDataRegistersAsync(100, 1));
        Assert.Equal(new[] { true }, await plc.ReadInternalRelaysAsync(10, 1));
        Assert.Equal(12.3, host.Points.Get<double>("temperature"), 3);
        Assert.True(host.Points.Get<bool>("run"));
    }

    /// <summary>
    /// MC X 输入继电器不能在 JSON 点表中声明为可写。
    /// </summary>
    [Fact]
    public void MitsubishiMcWritableInputRelay_FailsAtLoad()
    {
        const string json = """
            {
              "channels": [ { "name": "plc-link", "type": "virtual", "responder": "mc" } ],
              "devices": [
                {
                  "name": "plc",
                  "channel": "plc-link",
                  "type": "mitsubishi-mc",
                  "points": [ { "name": "ready", "deviceCode": "X", "address": 16, "writable": true } ]
                }
              ]
            }
            """;

        var error = Assert.Throws<ZeusException>(() => ZeusConfigurationLoader.LoadJson(json, "MC X 可写配置"));
        Assert.Contains("X", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("writable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// MC 点表必须使用受支持的软元件代码。
    /// </summary>
    [Fact]
    public void MitsubishiMcInvalidDeviceCode_FailsAtLoad()
    {
        const string json = """
            {
              "channels": [ { "name": "plc-link", "type": "virtual", "responder": "mc" } ],
              "devices": [
                {
                  "name": "plc",
                  "channel": "plc-link",
                  "type": "mitsubishi-mc",
                  "points": [ { "name": "bad", "deviceCode": "B", "address": 0 } ]
                }
              ]
            }
            """;

        var error = Assert.Throws<ZeusException>(() => ZeusConfigurationLoader.LoadJson(json, "MC 坏软元件配置"));
        Assert.Contains("deviceCode", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("D", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ZR", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// MC 位软元件不能配置数值报警限。
    /// </summary>
    [Fact]
    public void MitsubishiMcBitAlarmLimits_FailsAtLoad()
    {
        const string json = """
            {
              "channels": [ { "name": "plc-link", "type": "virtual", "responder": "mc" } ],
              "devices": [
                {
                  "name": "plc",
                  "channel": "plc-link",
                  "type": "mitsubishi-mc",
                  "points": [ { "name": "run", "deviceCode": "M", "address": 10, "highAlarmLimit": 1 } ]
                }
              ]
            }
            """;

        var error = Assert.Throws<ZeusException>(() => ZeusConfigurationLoader.LoadJson(json, "MC 位报警配置"));
        Assert.Contains("位软元件", error.Message, StringComparison.Ordinal);
        Assert.Contains("highAlarmLimit", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// MC 1E 帧不支持 ZR 点，应在装载期失败。
    /// </summary>
    [Fact]
    public void MitsubishiMc1EExtendedFileRegister_FailsAtLoad()
    {
        const string json = """
            {
              "channels": [ { "name": "plc-link", "type": "virtual", "responder": "mc" } ],
              "devices": [
                {
                  "name": "plc",
                  "channel": "plc-link",
                  "type": "mitsubishi-mc",
                  "frameType": "1e",
                  "points": [ { "name": "recipe", "deviceCode": "ZR", "address": 0 } ]
                }
              ]
            }
            """;

        var error = Assert.Throws<ZeusException>(() => ZeusConfigurationLoader.LoadJson(json, "MC 1E ZR 配置"));
        Assert.Contains("1E", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ZR", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ReloadAcquisition 只更新间隔，不重建设备。
    /// </summary>
    [Fact]
    public async Task ReloadAcquisition_UpdatesIntervalOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zeus-config-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, ValidJson);
        try
        {
            await using var host = ZeusHost.Create(builder => builder.AddJsonFile(path, watch: false));
            var oven = host.Devices.Get<ModbusDevice>("oven");
            Assert.Equal(TimeSpan.FromMilliseconds(200), host.Services.GetRequiredService<AcquisitionOptions>().Interval);

            var updated = ValidJson.Replace("\"intervalMilliseconds\": 200", "\"intervalMilliseconds\": 800", StringComparison.Ordinal);
            await File.WriteAllTextAsync(path, updated);
            host.ReloadAcquisition(path);

            Assert.Equal(TimeSpan.FromMilliseconds(800), host.Services.GetRequiredService<AcquisitionOptions>().Interval);
            Assert.Same(oven, host.Devices.Get<ModbusDevice>("oven"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 热更新应能按 MC 配置指纹重建设备，例如从 3E Binary 切到 4E ASCII。
    /// </summary>
    [Fact]
    public async Task ReloadAsync_RecreatesMitsubishiMcDeviceWhenOptionsChange()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zeus-mc-config-{Guid.NewGuid():N}.json");
        var initial = """
            {
              "channels": [ { "name": "plc-link", "type": "virtual", "responder": "mc" } ],
              "devices": [ { "name": "plc", "channel": "plc-link", "type": "mitsubishi-mc" } ]
            }
            """;
        var updated = """
            {
              "channels": [ { "name": "plc-link", "type": "virtual", "responder": "mc" } ],
              "devices": [
                {
                  "name": "plc",
                  "channel": "plc-link",
                  "type": "mitsubishi-mc",
                  "frameType": "4e",
                  "encoding": "ascii",
                  "serialNumber": 77
                }
              ]
            }
            """;

        await File.WriteAllTextAsync(path, initial);
        try
        {
            await using var host = ZeusHost.Create(builder => builder.AddJsonFile(path, watch: false));
            var before = host.Devices.Get<McDevice>("plc");
            Assert.Equal(McFrameType.Frame3E, before.Client.Options.FrameType);

            await File.WriteAllTextAsync(path, updated);
            await host.ReloadAsync(path);

            var after = host.Devices.Get<McDevice>("plc");
            Assert.NotSame(before, after);
            Assert.Equal(McFrameType.Frame4E, after.Client.Options.FrameType);
            Assert.Equal(McDataEncoding.Ascii, after.Client.Options.DataEncoding);
            Assert.Equal((ushort)77, after.Client.Options.SerialNumber);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 找不到文件时应给出绝对路径。
    /// </summary>
    [Fact]
    public void MissingFile_MentionsFullPath()
    {
        var error = Assert.Throws<ZeusException>(() => ZeusConfigurationLoader.LoadFile("definitely-missing-zeus.json"));
        Assert.Contains("找不到配置文件", error.Message, StringComparison.Ordinal);
        Assert.Contains("definitely-missing-zeus.json", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<T> WaitForPointAsync<T>(IZeusHost host, string name)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (host.Points.TryGet<T>(name, out var value) && value is not null)
            {
                return value;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"等待点 {name} 超时。");
    }
}
