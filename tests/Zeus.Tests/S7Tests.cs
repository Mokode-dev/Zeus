using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 通过内存虚拟 PLC 验证 Siemens S7 读写、点表与 JSON 配置行为。
/// </summary>
public sealed class S7Tests
{
    /// <summary>
    /// S7 设备应能通过虚拟 PLC 读写 DB 区常用类型。
    /// </summary>
    [Fact]
    public async Task S7Device_WritesAndReadsDataBlockValues()
    {
        var memory = new S7SlaveMemory();
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("plc-link", new S7SlaveResponder(memory));
            builder.AddSiemensS7("plc", "plc-link");
        });

        await host.StartAsync();
        var plc = host.Devices.Get<S7Device>("plc");

        await plc.WriteWordAsync(S7Area.DataBlock, 0, 1234, dbNumber: 1);
        await plc.WriteIntAsync(S7Area.DataBlock, 2, -123, dbNumber: 1);
        await plc.WriteDIntAsync(S7Area.DataBlock, 4, -123456, dbNumber: 1);
        await plc.WriteRealAsync(S7Area.DataBlock, 8, 12.5f, dbNumber: 1);
        await plc.WriteBoolAsync(S7Area.DataBlock, 12, 3, true, dbNumber: 1);

        Assert.Equal((ushort)1234, await plc.ReadWordAsync(S7Area.DataBlock, 0, dbNumber: 1));
        Assert.Equal((short)-123, await plc.ReadIntAsync(S7Area.DataBlock, 2, dbNumber: 1));
        Assert.Equal(-123456, await plc.ReadDIntAsync(S7Area.DataBlock, 4, dbNumber: 1));
        Assert.Equal(12.5f, await plc.ReadRealAsync(S7Area.DataBlock, 8, dbNumber: 1));
        Assert.True(await plc.ReadBoolAsync(S7Area.DataBlock, 12, 3, dbNumber: 1));

        var db = memory.GetDataBlock(1);
        Assert.Equal(0x04, db[0]);
        Assert.Equal(0xD2, db[1]);
        Assert.Equal(0b_0000_1000, db[12]);
    }

    /// <summary>
    /// S7 设备应能读写 M 区连续字节。
    /// </summary>
    [Fact]
    public async Task S7Device_WritesAndReadsMarkerBytes()
    {
        var memory = new S7SlaveMemory();
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("plc-link", new S7SlaveResponder(memory));
            builder.AddSiemensS7("plc", "plc-link");
        });

        await host.StartAsync();
        var plc = host.Devices.Get<S7Device>("plc");

        await plc.WriteMarkerBytesAsync(20, [1, 2, 3, 4]);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await plc.ReadMarkerBytesAsync(20, 4));
        Assert.Equal((byte)3, memory.Markers[22]);
    }

    /// <summary>
    /// S7 点表应能周期采集并按点名写回 DB 与 M 区。
    /// </summary>
    [Fact]
    public async Task S7PointTable_PollsAndWritesPoints()
    {
        var memory = new S7SlaveMemory();
        WriteSingle(memory.GetDataBlock(1).AsSpan(0, 4), 25.5f);
        WriteInt16(memory.GetDataBlock(1).AsSpan(4, 2), 250);
        memory.Markers[10] = 0x01;

        await using var host = ZeusHost.Create(builder =>
        {
            builder.Acquisition.Interval = TimeSpan.FromMilliseconds(50);
            builder.AddVirtualChannel("plc-link", new S7SlaveResponder(memory));
            builder.AddSiemensS7("plc", "plc-link", points: map => map
                .DbReal("temperature", 1, 0)
                .DbInt("setpoint", 1, 4, 0.1).Writable("setpoint")
                .MarkerBool("running", 10, 0).Writable("running"));
        });

        await host.StartAsync();

        Assert.Equal(25.5f, await WaitForPointAsync<float>(host, "temperature"));
        Assert.Equal(25d, await WaitForPointAsync<double>(host, "setpoint"));
        Assert.True(await WaitForPointAsync<bool>(host, "running"));
        Assert.True(host.Points.Get("setpoint").Definition.Writable);

        await host.Points.WriteAsync("setpoint", 12.3);
        await host.Points.WriteAsync("running", false);

        Assert.Equal((short)123, ReadInt16(memory.GetDataBlock(1).AsSpan(4, 2)));
        Assert.Equal(12.3d, host.Points.Get<double>("setpoint"), 3);
        Assert.Equal((byte)0x00, memory.Markers[10]);
    }

    /// <summary>
    /// JSON 配置应能声明 Siemens S7 设备、虚拟 PLC 和点表。
    /// </summary>
    [Fact]
    public async Task AddJson_CreatesSiemensS7DeviceAndPoints()
    {
        const string json = """
            {
              "acquisition": { "intervalMilliseconds": 50, "pollImmediately": true },
              "channels": [
                { "name": "plc-link", "type": "virtual", "responder": "s7" }
              ],
              "devices": [
                {
                  "name": "plc",
                  "channel": "plc-link",
                  "type": "siemens-s7",
                  "rack": 0,
                  "slot": 1,
                  "points": [
                    { "name": "temperature", "area": "db", "db": 1, "address": 0, "dataType": "real" },
                    { "name": "setpoint", "area": "db", "db": 1, "address": 4, "dataType": "int", "scale": 0.1, "writable": true },
                    { "name": "running", "area": "m", "address": 10, "bit": 0, "dataType": "bool", "writable": true }
                  ]
                }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "S7 配置"));
        await host.StartAsync();
        var plc = host.Devices.Get<S7Device>("plc");

        await plc.WriteRealAsync(S7Area.DataBlock, 0, 31.5f, dbNumber: 1);
        Assert.Equal(31.5f, await WaitForPointAsync<float>(host, "temperature"));

        await host.Points.WriteAsync("setpoint", 16.8);
        Assert.Equal((short)168, await plc.ReadIntAsync(S7Area.DataBlock, 4, dbNumber: 1));

        await host.Points.WriteAsync("running", true);
        Assert.True(await plc.ReadBoolAsync(S7Area.Merkers, 10, 0));
    }

    /// <summary>
    /// S7 输入区不能在 JSON 点表中声明为可写。
    /// </summary>
    [Fact]
    public void SiemensS7WritableInput_FailsAtLoad()
    {
        const string json = """
            {
              "channels": [ { "name": "plc-link", "type": "virtual", "responder": "s7" } ],
              "devices": [
                {
                  "name": "plc",
                  "channel": "plc-link",
                  "type": "siemens-s7",
                  "points": [ { "name": "start", "area": "i", "address": 0, "bit": 0, "dataType": "bool", "writable": true } ]
                }
              ]
            }
            """;

        var error = Assert.Throws<ZeusException>(() => ZeusConfigurationLoader.LoadJson(json, "S7 坏配置"));
        Assert.Contains("输入区", error.Message, StringComparison.Ordinal);
        Assert.Contains("writable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 虚拟 PLC 不得按请求把单个 DB 扩到超过上限。
    /// </summary>
    [Fact]
    public void SlaveMemory_RejectsOversizedDataBlock()
    {
        var memory = new S7SlaveMemory(1024, 1024, 4096, 64, maxDataBlockSize: 128, maxDataBlockCount: 2);
        var error = Assert.Throws<ZeusException>(() => memory.GetDataBlock(1, 1024));
        Assert.Contains("超过上限", error.Message, StringComparison.Ordinal);
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

    private static void WriteInt16(Span<byte> destination, short value)
    {
        destination[0] = (byte)((value >> 8) & 0xFF);
        destination[1] = (byte)(value & 0xFF);
    }

    private static short ReadInt16(ReadOnlySpan<byte> source)
        => unchecked((short)((source[0] << 8) | source[1]));

    private static void WriteSingle(Span<byte> destination, float value)
    {
        var raw = BitConverter.SingleToInt32Bits(value);
        destination[0] = (byte)((raw >> 24) & 0xFF);
        destination[1] = (byte)((raw >> 16) & 0xFF);
        destination[2] = (byte)((raw >> 8) & 0xFF);
        destination[3] = (byte)(raw & 0xFF);
    }
}
