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
                { "name": "temperature", "table": "holding", "address": 0, "scale": 0.1 },
                { "name": "heater", "table": "coil", "address": 2 }
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
        Assert.Equal(TimeSpan.FromMilliseconds(200), host.Services.GetRequiredService<AcquisitionOptions>().Interval);
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
    /// 找不到文件时应给出绝对路径。
    /// </summary>
    [Fact]
    public void MissingFile_MentionsFullPath()
    {
        var error = Assert.Throws<ZeusException>(() => ZeusConfigurationLoader.LoadFile("definitely-missing-zeus.json"));
        Assert.Contains("找不到配置文件", error.Message, StringComparison.Ordinal);
        Assert.Contains("definitely-missing-zeus.json", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
