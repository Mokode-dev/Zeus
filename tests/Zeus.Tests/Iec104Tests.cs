using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 通过内存虚拟站验证 IEC 60870-5-104 总召唤、命令、点表与 JSON 配置行为。
/// </summary>
public sealed class Iec104Tests
{
    /// <summary>
    /// IEC104 设备应能完成 STARTDT、总召唤和单点命令。
    /// </summary>
    [Fact]
    public async Task Device_InterrogatesAndWritesCommand()
    {
        var memory = new Iec104StationMemory();
        memory.SetSinglePoint(1, true);
        memory.SetScaled(100, 253);
        memory.SetShortFloat(200, 25.3);

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("iec-link", new Iec104SlaveResponder(new Iec104Options { CommonAddress = 7 }, memory));
            builder.AddIec104("station", "iec-link", new Iec104Options { CommonAddress = 7 });
        });

        await host.StartAsync();
        var station = host.Devices.Get<Iec104Device>("station");

        var values = await station.InterrogateAsync();
        Assert.Contains(values, item => item.Address == 1 && item.DataType == Iec104DataType.SinglePoint && (bool)item.Value);
        Assert.Contains(values, item => item.Address == 100 && item.DataType == Iec104DataType.Scaled && (short)item.Value == 253);
        Assert.Contains(values, item => item.Address == 200 && item.DataType == Iec104DataType.ShortFloat && Math.Abs(Convert.ToDouble(item.Value) - 25.3) < 0.001);

        await station.SendSingleCommandAsync(1, false);
        Assert.Contains(memory.Snapshot, item => item.Address == 1 && item.DataType == Iec104DataType.SinglePoint && !(bool)item.Value);
    }

    /// <summary>
    /// IEC104 点表应接入宿主采集循环，并支持按点名写回可写点。
    /// </summary>
    [Fact]
    public async Task Device_PollsAndWritesPointMap()
    {
        var memory = new Iec104StationMemory();
        memory.SetSinglePoint(1, true);
        memory.SetScaled(100, 253);

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromMilliseconds(50));
            builder.AddVirtualChannel("iec-link", new Iec104SlaveResponder(new Iec104Options { CommonAddress = 7 }, memory));
            builder.AddIec104(
                "station",
                "iec-link",
                new Iec104Options { CommonAddress = 7 },
                points: map => map
                    .SinglePoint("running", 1)
                    .Writable("running")
                    .Scaled("temperature", 100, scale: 0.1)
                    .Writable("temperature"));
        });

        await host.StartAsync();

        Assert.True(await WaitForPointAsync<bool>(host, "running"));
        Assert.Equal(25.3, await WaitForPointAsync<double>(host, "temperature"), 3);

        await host.Points.WriteAsync("running", false);
        Assert.Contains(memory.Snapshot, item => item.Address == 1 && item.DataType == Iec104DataType.SinglePoint && !(bool)item.Value);
        Assert.False(host.Points.Get<bool>("running"));

        await host.Points.WriteAsync("temperature", 18.6);
        Assert.Contains(memory.Snapshot, item => item.Address == 100 && item.DataType == Iec104DataType.Scaled && (short)item.Value == 186);
        Assert.Equal(18.6, host.Points.Get<double>("temperature"), 3);
    }

    /// <summary>
    /// JSON 配置应能声明 IEC104 虚拟站、设备和点表。
    /// </summary>
    [Fact]
    public async Task AddJson_CreatesIec104DeviceAndPoints()
    {
        const string json = """
            {
              "acquisition": { "intervalMilliseconds": 50, "pollImmediately": true },
              "channels": [
                { "name": "iec-link", "type": "virtual", "responder": "iec104", "commonAddress": 7 }
              ],
              "devices": [
                {
                  "name": "station",
                  "channel": "iec-link",
                  "type": "iec104",
                  "commonAddress": 7,
                  "points": [
                    { "name": "running", "address": 1, "dataType": "single-point", "writable": true },
                    { "name": "temperature", "address": 100, "dataType": "scaled", "scale": 0.1, "writable": true },
                    { "name": "pressure", "address": 200, "dataType": "short-float" }
                  ]
                }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "IEC104 配置"));
        await host.StartAsync();
        var station = host.Devices.Get<Iec104Device>("station");

        Assert.True(await WaitForPointAsync<bool>(host, "running"));
        Assert.Equal(25.3, await WaitForPointAsync<double>(host, "temperature"), 3);

        await host.Points.WriteAsync("temperature", 12.3);
        var values = await station.InterrogateAsync();
        Assert.Contains(values, item => item.Address == 100 && item.DataType == Iec104DataType.Scaled && (short)item.Value == 123);
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
