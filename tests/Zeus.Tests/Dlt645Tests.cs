using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 通过内存虚拟表计验证 DL/T 645-2007 帧、点表与 JSON 配置行为。
/// </summary>
public sealed class Dlt645Tests
{
    private const string MeterAddress = "000000000001";

    /// <summary>
    /// DL/T 645 设备应能读取和写入 BCD 数据项，并由虚拟表计保留内存值。
    /// </summary>
    [Fact]
    public async Task Device_ReadsAndWritesBcdData()
    {
        var memory = new Dlt645SlaveMemory();
        memory.SetBcd(0x00000000, 1234.56, byteLength: 4, scale: 0.01);

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("meter-link", new Dlt645SlaveResponder(MeterAddress, memory));
            builder.AddDlt645("meter", "meter-link", new Dlt645Options { MeterAddress = MeterAddress, WakeUpPreambleCount = 0 });
        });

        await host.StartAsync();
        var meter = host.Devices.Get<Dlt645Device>("meter");

        Assert.Equal(1234.56, await meter.ReadBcdAsync(0x00000000, byteLength: 4, scale: 0.01), 3);

        await meter.WriteBcdAsync(0x04000101, 88.8, byteLength: 2, scale: 0.1);
        Assert.Equal(88.8, memory.GetBcd(0x04000101, byteLength: 2, scale: 0.1), 3);
    }

    /// <summary>
    /// DL/T 645 点表应接入宿主采集循环，并支持按点名写回可写 BCD 点。
    /// </summary>
    [Fact]
    public async Task Device_PollsAndWritesPointMap()
    {
        var memory = new Dlt645SlaveMemory();
        memory.SetBcd(0x00000000, 321.09, byteLength: 4, scale: 0.01);
        memory.SetBcd(0x04000101, 10.0, byteLength: 2, scale: 0.1);

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromMilliseconds(50));
            builder.AddVirtualChannel("meter-link", new Dlt645SlaveResponder(MeterAddress, memory));
            builder.AddDlt645(
                "meter",
                "meter-link",
                new Dlt645Options { MeterAddress = MeterAddress, WakeUpPreambleCount = 0 },
                points: map => map
                    .Bcd("energy", 0x00000000, dataLength: 4, scale: 0.01)
                    .Bcd("limit", 0x04000101, dataLength: 2, scale: 0.1)
                    .Writable("limit"));
        });

        await host.StartAsync();

        Assert.Equal(321.09, await WaitForPointAsync<double>(host, "energy"), 3);
        Assert.True(host.Points.Get("limit").Definition.Writable);

        await host.Points.WriteAsync("limit", 88.8);

        Assert.Equal(88.8, memory.GetBcd(0x04000101, byteLength: 2, scale: 0.1), 3);
        Assert.Equal(88.8, host.Points.Get<double>("limit"), 3);
    }

    /// <summary>
    /// JSON 配置应能声明 DL/T 645 虚拟表计、设备和点表。
    /// </summary>
    [Fact]
    public async Task AddJson_CreatesDlt645DeviceAndPoints()
    {
        const string json = """
            {
              "acquisition": { "intervalMilliseconds": 50, "pollImmediately": true },
              "channels": [
                { "name": "meter-link", "type": "virtual", "responder": "dlt645", "meterAddress": "000000000001" }
              ],
              "devices": [
                {
                  "name": "meter",
                  "channel": "meter-link",
                  "type": "dlt645",
                  "meterAddress": "000000000001",
                  "wakeUpPreambleCount": 0,
                  "points": [
                    { "name": "energy", "address": "0x00000000", "dataType": "bcd", "dataLength": 4, "scale": 0.01 },
                    { "name": "limit", "address": "0x04000101", "dataType": "bcd", "dataLength": 2, "scale": 0.1, "writable": true }
                  ]
                }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "DL/T 645 配置"));
        await host.StartAsync();
        var meter = host.Devices.Get<Dlt645Device>("meter");

        await meter.WriteBcdAsync(0x00000000, 45.67, byteLength: 4, scale: 0.01);
        Assert.Equal(45.67, await WaitForPointValueAsync(host, "energy", 45.67), 3);

        await host.Points.WriteAsync("limit", 12.3);
        Assert.Equal(12.3, await meter.ReadBcdAsync(0x04000101, byteLength: 2, scale: 0.1), 3);
    }

    /// <summary>
    /// 未设置的数据项应暴露为 DL/T 645 异常码。
    /// </summary>
    [Fact]
    public async Task MissingDataThrowsDlt645Exception()
    {
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("meter-link", new Dlt645SlaveResponder(MeterAddress, new Dlt645SlaveMemory()));
            builder.AddDlt645("meter", "meter-link", new Dlt645Options { MeterAddress = MeterAddress, WakeUpPreambleCount = 0 });
        });

        await host.StartAsync();
        var meter = host.Devices.Get<Dlt645Device>("meter");

        var error = await Assert.ThrowsAsync<Dlt645Exception>(() => meter.ReadDataAsync(0x00000000));
        Assert.Equal((byte)0x02, error.ErrorCode);
        Assert.Contains("异常码", error.Message, StringComparison.Ordinal);
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
