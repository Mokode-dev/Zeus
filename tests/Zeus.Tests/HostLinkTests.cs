using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 通过内存虚拟 PLC 验证 Omron Host Link ASCII 帧、点表与 JSON 配置行为。
/// </summary>
public sealed class HostLinkTests
{
    /// <summary>
    /// Host Link 设备应能读写 DM / CIO 字区，并由虚拟 PLC 保留内存值。
    /// </summary>
    [Fact]
    public async Task Device_ReadsAndWritesWords()
    {
        var memory = new HostLinkSlaveMemory();
        memory.DataMemoryWords[100] = 1234;
        memory.CioWords[20] = 0x0008;

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("host-link", new HostLinkSlaveResponder(0, memory));
            builder.AddOmronHostLink("plc", "host-link", new HostLinkOptions { UnitNumber = 0 });
        });

        await host.StartAsync();
        var plc = host.Devices.Get<HostLinkDevice>("plc");

        Assert.Equal(new ushort[] { 1234 }, await plc.ReadDataMemoryWordsAsync(100, 1));
        Assert.Equal(new ushort[] { 0x0008 }, await plc.ReadCioWordsAsync(20, 1));

        await plc.WriteDataMemoryWordsAsync(110, [10, 20, 30]);
        Assert.Equal(new ushort[] { 10, 20, 30 }, await plc.ReadDataMemoryWordsAsync(110, 3));
    }

    /// <summary>
    /// Host Link 点表应接入宿主采集循环，并支持按点名写回可写字点与位点。
    /// </summary>
    [Fact]
    public async Task Device_PollsAndWritesPointMap()
    {
        var memory = new HostLinkSlaveMemory();
        memory.DataMemoryWords[100] = 250;
        memory.CioWords[10] = 0x0001;

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromMilliseconds(50));
            builder.AddVirtualChannel("host-link", new HostLinkSlaveResponder(0, memory));
            builder.AddOmronHostLink("plc", "host-link", new HostLinkOptions { UnitNumber = 0 }, points: map => map
                .DmWord("temperature", 100, 0.1).Writable("temperature")
                .CioBit("running", 10, 0).Writable("running"));
        });

        await host.StartAsync();

        Assert.Equal(25.0, await WaitForPointAsync<double>(host, "temperature"), 3);
        Assert.True(await WaitForPointAsync<bool>(host, "running"));
        Assert.True(host.Points.Get("temperature").Definition.Writable);

        await host.Points.WriteAsync("temperature", 18.6);
        await host.Points.WriteAsync("running", false);

        Assert.Equal((ushort)186, memory.DataMemoryWords[100]);
        Assert.Equal(18.6, host.Points.Get<double>("temperature"), 3);
        Assert.False((memory.CioWords[10] & 0x0001) != 0);
    }

    /// <summary>
    /// JSON 配置应能声明 Host Link 虚拟 PLC、Host Link 设备和点表。
    /// </summary>
    [Fact]
    public async Task AddJson_CreatesHostLinkDeviceAndPoints()
    {
        const string json = """
            {
              "acquisition": { "intervalMilliseconds": 50, "pollImmediately": true },
              "channels": [
                { "name": "host-link", "type": "virtual", "responder": "host-link", "unitId": 0 }
              ],
              "devices": [
                {
                  "name": "plc",
                  "channel": "host-link",
                  "type": "omron-host-link",
                  "unitId": 0,
                  "points": [
                    { "name": "temperature", "area": "dm", "address": 100, "dataType": "word", "scale": 0.1, "writable": true },
                    { "name": "running", "area": "cio", "address": 10, "bit": 0, "dataType": "bit", "writable": true }
                  ]
                }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "Host Link 配置"));
        await host.StartAsync();
        var plc = host.Devices.Get<HostLinkDevice>("plc");

        await plc.WriteDataMemoryWordsAsync(100, [315]);
        Assert.Equal(31.5, await WaitForPointValueAsync(host, "temperature", 31.5), 3);

        await host.Points.WriteAsync("running", true);
        Assert.Equal((ushort)0x0001, (await plc.ReadCioWordsAsync(10, 1))[0]);
    }

    /// <summary>
    /// 地址越界应暴露为 Host Link 结束码异常。
    /// </summary>
    [Fact]
    public async Task InvalidAddressThrowsHostLinkException()
    {
        var memory = new HostLinkSlaveMemory(dataMemoryWords: 8);
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("host-link", new HostLinkSlaveResponder(0, memory));
            builder.AddOmronHostLink("plc", "host-link", new HostLinkOptions { UnitNumber = 0 });
        });

        await host.StartAsync();
        var plc = host.Devices.Get<HostLinkDevice>("plc");

        var error = await Assert.ThrowsAsync<HostLinkException>(() => plc.ReadDataMemoryWordsAsync(100, 1));
        Assert.Equal((byte)0x04, error.EndCode);
        Assert.Contains("结束码", error.Message, StringComparison.Ordinal);
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

    private static async Task<double> WaitForPointValueAsync(IZeusHost host, string name, double expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (host.Points.TryGet<double>(name, out var value) && Math.Abs(value - expected) < 0.0001)
            {
                return value;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"等待点 {name} 更新为 {expected} 超时。");
    }
}
