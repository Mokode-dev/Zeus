using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 通过内存虚拟 PLC 验证 Omron FINS UDP/TCP、内存区读写、点表与 JSON 配置行为。
/// </summary>
public sealed class FinsTests
{
    /// <summary>
    /// FINS/UDP 设备应能读写 DM 字、CIO 位、填充字区，并进行多点读取。
    /// </summary>
    [Fact]
    public async Task UdpDevice_ReadsWritesFillsAndReadsMultipleMemoryAreas()
    {
        var memory = new FinsSlaveMemory();
        memory.DataMemoryWords[100] = 1234;
        memory.CioWords[20] = 0b_0000_1000;

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("fins-link", new FinsSlaveResponder(FinsTransport.Udp, memory));
            builder.AddOmronFinsUdp("plc", "fins-link", new FinsOptions
            {
                SourceNode = 10,
                DestinationNode = 1
            });
        });

        await host.StartAsync();
        var plc = host.Devices.Get<FinsDevice>("plc");

        Assert.Equal(new ushort[] { 1234 }, await plc.ReadDataMemoryWordsAsync(100, 1));
        Assert.True((await plc.ReadCioBitsAsync(20, 3, 1))[0]);

        await plc.WriteDataMemoryWordsAsync(110, [10, 20, 30]);
        Assert.Equal(new ushort[] { 10, 20, 30 }, await plc.ReadDataMemoryWordsAsync(110, 3));

        await plc.WriteCioBitsAsync(21, 1, [true, false, true]);
        Assert.True((memory.CioWords[21] & 0b_0010) != 0);
        Assert.False((memory.CioWords[21] & 0b_0100) != 0);
        Assert.True((memory.CioWords[21] & 0b_1000) != 0);

        await plc.FillWordsAsync(FinsMemoryAreaCode.DataMemoryWord, 120, 3, 0x55AA);
        Assert.Equal(new ushort[] { 0x55AA, 0x55AA, 0x55AA }, await plc.ReadDataMemoryWordsAsync(120, 3));

        var values = await plc.ReadMultipleAsync([
            new FinsMemoryAddress(FinsMemoryAreaCode.DataMemoryWord, 100),
            new FinsMemoryAddress(FinsMemoryAreaCode.CioBit, 21, 3)
        ]);
        Assert.Equal((ushort)1234, values[0].WordValue);
        Assert.True(values[1].BitValue);
    }

    /// <summary>
    /// FINS/TCP 设备应先完成节点地址握手，再执行普通 FINS 帧发送。
    /// </summary>
    [Fact]
    public async Task TcpDevice_UsesNodeAddressHandshake()
    {
        var memory = new FinsSlaveMemory();
        memory.DataMemoryWords[10] = 99;
        var slaveOptions = new FinsOptions { SourceNode = 25, DestinationNode = 3 };

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("fins-tcp", new FinsSlaveResponder(FinsTransport.Tcp, memory, slaveOptions));
            builder.AddOmronFinsTcp("plc", "fins-tcp", new FinsOptions { TcpRequestedClientNode = 7 });
        });

        await host.StartAsync();
        var plc = host.Devices.Get<FinsDevice>("plc");

        Assert.Equal(new ushort[] { 99 }, await plc.ReadDataMemoryWordsAsync(10, 1));
        Assert.Equal((byte)7, plc.Client.Options.SourceNode);
        Assert.Equal((byte)3, plc.Client.Options.DestinationNode);
    }

    /// <summary>
    /// FINS 点表应接入宿主采集循环，并支持按点名写回可写点。
    /// </summary>
    [Fact]
    public async Task FinsDevice_PollsAndWritesPointMap()
    {
        var memory = new FinsSlaveMemory();
        memory.DataMemoryWords[100] = 250;
        memory.CioWords[10] = 0x0001;

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromMilliseconds(50));
            builder.AddVirtualChannel("fins-link", new FinsSlaveResponder(FinsTransport.Udp, memory));
            builder.AddOmronFinsUdp("plc", "fins-link", new FinsOptions { SourceNode = 10, DestinationNode = 1 }, points: map => map
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
    /// JSON 配置应能声明 FINS 虚拟 PLC、FINS 设备和点表。
    /// </summary>
    [Fact]
    public async Task AddJson_CreatesOmronFinsDeviceAndPoints()
    {
        const string json = """
            {
              "acquisition": { "intervalMilliseconds": 50, "pollImmediately": true },
              "channels": [
                { "name": "fins-link", "type": "virtual", "responder": "fins", "transport": "udp" }
              ],
              "devices": [
                {
                  "name": "plc",
                  "channel": "fins-link",
                  "type": "omron-fins-udp",
                  "sourceNode": 10,
                  "destinationNode": 1,
                  "points": [
                    { "name": "temperature", "area": "dm", "address": 100, "dataType": "word", "scale": 0.1, "writable": true },
                    { "name": "running", "area": "cio", "address": 10, "bit": 0, "dataType": "bit", "writable": true }
                  ]
                }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "FINS 配置"));
        await host.StartAsync();
        var plc = host.Devices.Get<FinsDevice>("plc");

        await plc.WriteDataMemoryWordsAsync(100, [315]);
        Assert.Equal(31.5, await WaitForPointAsync<double>(host, "temperature"), 3);

        await host.Points.WriteAsync("running", true);
        Assert.True((await plc.ReadCioBitsAsync(10, 0, 1))[0]);
    }

    /// <summary>
    /// 地址越界应暴露为 FINS 结束码异常。
    /// </summary>
    [Fact]
    public async Task InvalidAddressThrowsFinsException()
    {
        var memory = new FinsSlaveMemory(dataMemoryWords: 8);
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("fins-link", new FinsSlaveResponder(FinsTransport.Udp, memory));
            builder.AddOmronFinsUdp("plc", "fins-link", new FinsOptions { SourceNode = 10, DestinationNode = 1 });
        });

        await host.StartAsync();
        var plc = host.Devices.Get<FinsDevice>("plc");

        var error = await Assert.ThrowsAsync<FinsException>(() => plc.ReadDataMemoryWordsAsync(100, 1));
        Assert.Equal((ushort)0x1103, error.EndCode);
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
}
