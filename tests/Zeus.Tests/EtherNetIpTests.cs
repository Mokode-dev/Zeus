using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 通过内存虚拟 PLC 验证 Allen-Bradley EtherNet/IP CIP 标签读写、属性访问、点表与 JSON 配置行为。
/// </summary>
public sealed class EtherNetIpTests
{
    /// <summary>
    /// EtherNet/IP 设备应能完成 Register Session，并读写 CIP 标签与对象属性。
    /// </summary>
    [Fact]
    public async Task Device_ReadsWritesTagsAndAttributes()
    {
        var memory = new EtherNetIpSlaveMemory();
        memory.SetTag("Temperature", EtherNetIpDataType.Int, (short)253);
        memory.SetTag("Running", EtherNetIpDataType.Bool, true);
        memory.SetAttribute(0x01, 1, 1, [0x34, 0x12]);

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("enip-link", new EtherNetIpSlaveResponder(memory));
            builder.AddEtherNetIp("plc", "enip-link");
        });

        await host.StartAsync();
        var plc = host.Devices.Get<EtherNetIpDevice>("plc");

        Assert.Equal(25.3, (double)await plc.ReadTagAsync("Temperature", EtherNetIpDataType.Int, scale: 0.1), 3);
        Assert.True((bool)await plc.ReadTagAsync("Running", EtherNetIpDataType.Bool));

        await plc.WriteTagAsync("Temperature", EtherNetIpDataType.Int, 18.6, scale: 0.1);
        Assert.Equal((short)186, memory.GetTag("Temperature").Value);

        Assert.Equal(new byte[] { 0x34, 0x12 }, await plc.GetAttributeSingleAsync(0x01, 1, 1));
        await plc.SetAttributeSingleAsync(0x01, 1, 2, new byte[] { 0x78, 0x56 });
        Assert.True(memory.TryGetAttribute(0x01, 1, 2, out var attribute));
        Assert.Equal(new byte[] { 0x78, 0x56 }, attribute);
        Assert.NotEqual(0u, plc.Client.SessionHandle);
    }

    /// <summary>
    /// EtherNet/IP 点表应接入宿主采集循环，并支持按点名写回可写点。
    /// </summary>
    [Fact]
    public async Task EtherNetIpDevice_PollsAndWritesPointMap()
    {
        var memory = new EtherNetIpSlaveMemory();
        memory.SetTag("Temperature", EtherNetIpDataType.Int, (short)250);
        memory.SetTag("Running", EtherNetIpDataType.Bool, true);

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromMilliseconds(50));
            builder.AddVirtualChannel("enip-link", new EtherNetIpSlaveResponder(memory));
            builder.AddEtherNetIp("plc", "enip-link", points: map => map
                .Int("temperature", "Temperature", 0.1).Writable("temperature")
                .Bool("running", "Running").Writable("running"));
        });

        await host.StartAsync();

        Assert.Equal(25.0, await WaitForPointAsync<double>(host, "temperature"), 3);
        Assert.True(await WaitForPointAsync<bool>(host, "running"));
        Assert.True(host.Points.Get("temperature").Definition.Writable);

        await host.Points.WriteAsync("temperature", 18.6);
        await host.Points.WriteAsync("running", false);

        Assert.Equal((short)186, memory.GetTag("Temperature").Value);
        Assert.False((bool)memory.GetTag("Running").Value);
        Assert.Equal(18.6, host.Points.Get<double>("temperature"), 3);
    }

    /// <summary>
    /// JSON 配置应能声明 EtherNet/IP 虚拟 PLC、设备和标签点。
    /// </summary>
    [Fact]
    public async Task AddJson_CreatesEtherNetIpDeviceAndPoints()
    {
        const string json = """
            {
              "acquisition": { "intervalMilliseconds": 50, "pollImmediately": true },
              "channels": [
                { "name": "enip-link", "type": "virtual", "responder": "ethernet-ip" }
              ],
              "devices": [
                {
                  "name": "plc",
                  "channel": "enip-link",
                  "type": "ethernet-ip",
                  "points": [
                    { "name": "temperature", "tag": "Temperature", "dataType": "int", "scale": 0.1, "writable": true },
                    { "name": "running", "tag": "Running", "dataType": "bool", "writable": true }
                  ]
                }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "EtherNet/IP 配置"));
        await host.StartAsync();
        var plc = host.Devices.Get<EtherNetIpDevice>("plc");

        Assert.Equal(25.3, await WaitForPointAsync<double>(host, "temperature"), 3);
        Assert.True(await WaitForPointAsync<bool>(host, "running"));

        await host.Points.WriteAsync("temperature", 18.6);
        Assert.Equal(18.6, (double)await plc.ReadTagAsync("Temperature", EtherNetIpDataType.Int, scale: 0.1), 3);
    }

    /// <summary>
    /// 缺失标签应暴露为 CIP 状态异常。
    /// </summary>
    [Fact]
    public async Task MissingTagThrowsEtherNetIpException()
    {
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("enip-link", new EtherNetIpSlaveResponder());
            builder.AddEtherNetIp("plc", "enip-link");
        });

        await host.StartAsync();
        var plc = host.Devices.Get<EtherNetIpDevice>("plc");

        var error = await Assert.ThrowsAsync<EtherNetIpException>(() => plc.ReadTagAsync("MissingTag", EtherNetIpDataType.Int));
        Assert.Equal((byte)0x04, error.GeneralStatus);
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
