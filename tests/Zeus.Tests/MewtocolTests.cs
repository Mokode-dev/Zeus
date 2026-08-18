using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 通过内存虚拟 PLC 验证 Panasonic MEWTOCOL-COM ASCII 帧、点表与 JSON 配置行为。
/// </summary>
public sealed class MewtocolTests
{
    /// <summary>
    /// MEWTOCOL 设备应能读写 DT 数据寄存器与 R 接点字，并由虚拟 PLC 保留内存值。
    /// </summary>
    [Fact]
    public async Task Device_ReadsAndWritesWords()
    {
        var memory = new MewtocolSlaveMemory();
        memory.DataRegisterWords[100] = 1234;
        memory.InternalRelayWords[20] = 0x0008;

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("mewtocol", new MewtocolSlaveResponder(1, memory));
            builder.AddPanasonicMewtocol("plc", "mewtocol", new MewtocolOptions { StationNumber = 1 });
        });

        await host.StartAsync();
        var plc = host.Devices.Get<MewtocolDevice>("plc");

        Assert.Equal(new ushort[] { 1234 }, await plc.ReadDataRegistersAsync(100, 1));
        Assert.Equal(new ushort[] { 0x0008 }, await plc.ReadInternalRelayWordsAsync(20, 1));

        await plc.WriteDataRegistersAsync(110, [10, 20, 30]);
        await plc.WriteInternalRelayWordsAsync(21, [0x0003]);
        Assert.Equal(new ushort[] { 10, 20, 30 }, await plc.ReadDataRegistersAsync(110, 3));
        Assert.Equal(new ushort[] { 0x0003 }, await plc.ReadInternalRelayWordsAsync(21, 1));
    }

    /// <summary>
    /// MEWTOCOL 点表应接入宿主采集循环，并支持按点名写回可写字点与位点。
    /// </summary>
    [Fact]
    public async Task Device_PollsAndWritesPointMap()
    {
        var memory = new MewtocolSlaveMemory();
        memory.DataRegisterWords[100] = 250;
        memory.InternalRelayWords[10] = 0x0001;

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromMilliseconds(50));
            builder.AddVirtualChannel("mewtocol", new MewtocolSlaveResponder(1, memory));
            builder.AddPanasonicMewtocol("plc", "mewtocol", new MewtocolOptions { StationNumber = 1 }, points: map => map
                .DtWord("temperature", 100, 0.1).Writable("temperature")
                .RBit("running", 10, 0).Writable("running"));
        });

        await host.StartAsync();

        Assert.Equal(25.0, await WaitForPointAsync<double>(host, "temperature"), 3);
        Assert.True(await WaitForPointAsync<bool>(host, "running"));
        Assert.True(host.Points.Get("temperature").Definition.Writable);

        await host.Points.WriteAsync("temperature", 18.6);
        await host.Points.WriteAsync("running", false);

        Assert.Equal((ushort)186, memory.DataRegisterWords[100]);
        Assert.Equal(18.6, host.Points.Get<double>("temperature"), 3);
        Assert.False((memory.InternalRelayWords[10] & 0x0001) != 0);
    }

    /// <summary>
    /// JSON 配置应能声明 MEWTOCOL 虚拟 PLC、MEWTOCOL 设备和点表。
    /// </summary>
    [Fact]
    public async Task AddJson_CreatesMewtocolDeviceAndPoints()
    {
        const string json = """
            {
              "acquisition": { "intervalMilliseconds": 50, "pollImmediately": true },
              "channels": [
                { "name": "mewtocol", "type": "virtual", "responder": "mewtocol", "unitId": 1 }
              ],
              "devices": [
                {
                  "name": "plc",
                  "channel": "mewtocol",
                  "type": "panasonic-mewtocol",
                  "unitId": 1,
                  "points": [
                    { "name": "temperature", "area": "dt", "address": 100, "dataType": "word", "scale": 0.1, "writable": true },
                    { "name": "running", "area": "r", "address": 10, "bit": 0, "dataType": "bit", "writable": true }
                  ]
                }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "MEWTOCOL 配置"));
        await host.StartAsync();
        var plc = host.Devices.Get<MewtocolDevice>("plc");

        await plc.WriteDataRegistersAsync(100, [315]);
        Assert.Equal(31.5, await WaitForPointValueAsync(host, "temperature", 31.5), 3);

        await host.Points.WriteAsync("running", true);
        Assert.Equal((ushort)0x0001, (await plc.ReadInternalRelayWordsAsync(10, 1))[0]);
    }

    /// <summary>
    /// 地址越界应暴露为 MEWTOCOL 错误码异常。
    /// </summary>
    [Fact]
    public async Task InvalidAddressThrowsMewtocolException()
    {
        var memory = new MewtocolSlaveMemory(dataRegisterWords: 8);
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("mewtocol", new MewtocolSlaveResponder(1, memory));
            builder.AddPanasonicMewtocol("plc", "mewtocol", new MewtocolOptions { StationNumber = 1 });
        });

        await host.StartAsync();
        var plc = host.Devices.Get<MewtocolDevice>("plc");

        var error = await Assert.ThrowsAsync<MewtocolException>(() => plc.ReadDataRegistersAsync(100, 1));
        Assert.Equal((byte)0x26, error.ErrorCode);
        Assert.Contains("错误码", error.Message, StringComparison.Ordinal);
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
