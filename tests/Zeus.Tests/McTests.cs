using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 通过内存虚拟 PLC 验证 Mitsubishi MC 读写行为。
/// </summary>
public sealed class McTests
{
    /// <summary>可用帧型组合。</summary>
    public static TheoryData<McFrameType, McDataEncoding> FrameVariants => new()
    {
        { McFrameType.Frame1E, McDataEncoding.Binary },
        { McFrameType.Frame1E, McDataEncoding.Ascii },
        { McFrameType.Frame3E, McDataEncoding.Ascii },
        { McFrameType.Frame4E, McDataEncoding.Binary },
        { McFrameType.Frame4E, McDataEncoding.Ascii }
    };

    /// <summary>支持随机读写的 3E/4E 帧型组合。</summary>
    public static TheoryData<McFrameType, McDataEncoding> RandomFrameVariants => new()
    {
        { McFrameType.Frame3E, McDataEncoding.Binary },
        { McFrameType.Frame3E, McDataEncoding.Ascii },
        { McFrameType.Frame4E, McDataEncoding.Binary },
        { McFrameType.Frame4E, McDataEncoding.Ascii }
    };

    /// <summary>
    /// MC 设备应能批量读写 D 数据寄存器。
    /// </summary>
    [Fact]
    public async Task McDevice_WritesAndReadsDataRegisters()
    {
        var memory = new McSlaveMemory();
        memory.DataRegisters[100] = 1234;
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("plc-bus", new McSlaveResponder(memory));
            builder.AddMitsubishiMc("plc", "plc-bus");
        });

        await host.StartAsync();
        var plc = host.Devices.Get<McDevice>("plc");

        Assert.Equal(new ushort[] { 1234 }, await plc.ReadDataRegistersAsync(100, 1));

        await plc.WriteDataRegistersAsync(110, [10, 20, 30]);
        Assert.Equal((ushort)10, memory.DataRegisters[110]);
        Assert.Equal((ushort)20, memory.DataRegisters[111]);
        Assert.Equal((ushort)30, memory.DataRegisters[112]);
        Assert.Equal(new ushort[] { 10, 20, 30 }, await plc.ReadDataRegistersAsync(110, 3));
    }

    /// <summary>
    /// MC 设备应能批量读写 M 内部继电器。
    /// </summary>
    [Fact]
    public async Task McDevice_WritesAndReadsInternalRelays()
    {
        var memory = new McSlaveMemory();
        memory.InternalRelays[10] = true;
        memory.InternalRelays[12] = true;
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("plc-bus", new McSlaveResponder(memory));
            builder.AddMitsubishiMc("plc", "plc-bus");
        });

        await host.StartAsync();
        var plc = host.Devices.Get<McDevice>("plc");

        Assert.Equal(new[] { true, false, true, false }, await plc.ReadInternalRelaysAsync(10, 4));

        await plc.WriteInternalRelaysAsync(20, [true, true, false, false, true]);
        Assert.True(memory.InternalRelays[20]);
        Assert.True(memory.InternalRelays[21]);
        Assert.False(memory.InternalRelays[22]);
        Assert.False(memory.InternalRelays[23]);
        Assert.True(memory.InternalRelays[24]);
        Assert.Equal(new[] { true, true, false, false, true }, await plc.ReadInternalRelaysAsync(20, 5));
    }

    /// <summary>
    /// MC 设备应能批量读写 W、R、ZR 字软元件。
    /// </summary>
    [Fact]
    public async Task McDevice_WritesAndReadsAdditionalWordDevices()
    {
        var memory = new McSlaveMemory();
        memory.LinkRegisters[10] = 111;
        memory.LinkRegisters[11] = 112;
        memory.FileRegisters[20] = 221;
        memory.FileRegisters[21] = 222;
        memory.ExtendedFileRegisters[30] = 331;
        memory.ExtendedFileRegisters[31] = 332;
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("plc-bus", new McSlaveResponder(memory));
            builder.AddMitsubishiMc("plc", "plc-bus");
        });

        await host.StartAsync();
        var plc = host.Devices.Get<McDevice>("plc");

        Assert.Equal(new ushort[] { 111, 112 }, await plc.ReadLinkRegistersAsync(10, 2));
        Assert.Equal(new ushort[] { 221, 222 }, await plc.ReadFileRegistersAsync(20, 2));
        Assert.Equal(new ushort[] { 331, 332 }, await plc.ReadExtendedFileRegistersAsync(30, 2));

        await plc.WriteLinkRegistersAsync(40, [410, 411]);
        await plc.WriteFileRegistersAsync(50, [510, 511]);
        await plc.WriteExtendedFileRegistersAsync(60, [610, 611]);

        Assert.Equal((ushort)410, memory.LinkRegisters[40]);
        Assert.Equal((ushort)411, memory.LinkRegisters[41]);
        Assert.Equal((ushort)510, memory.FileRegisters[50]);
        Assert.Equal((ushort)511, memory.FileRegisters[51]);
        Assert.Equal((ushort)610, memory.ExtendedFileRegisters[60]);
        Assert.Equal((ushort)611, memory.ExtendedFileRegisters[61]);
    }

    /// <summary>
    /// MC 设备应能批量读取 X 输入继电器并读写 Y 输出继电器。
    /// </summary>
    [Fact]
    public async Task McDevice_WritesAndReadsAdditionalBitDevices()
    {
        var memory = new McSlaveMemory();
        memory.InputRelays[0x10] = true;
        memory.InputRelays[0x12] = true;
        memory.OutputRelays[0x20] = true;
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("plc-bus", new McSlaveResponder(memory));
            builder.AddMitsubishiMc("plc", "plc-bus");
        });

        await host.StartAsync();
        var plc = host.Devices.Get<McDevice>("plc");

        Assert.Equal(new[] { true, false, true, false }, await plc.ReadInputRelaysAsync(0x10, 4));
        Assert.Equal(new[] { true, false, false }, await plc.ReadOutputRelaysAsync(0x20, 3));

        await plc.WriteOutputRelaysAsync(0x30, [false, true, true]);
        Assert.False(memory.OutputRelays[0x30]);
        Assert.True(memory.OutputRelays[0x31]);
        Assert.True(memory.OutputRelays[0x32]);
        Assert.Equal(new[] { false, true, true }, await plc.ReadOutputRelaysAsync(0x30, 3));

        await plc.WriteBitsAsync(McDeviceCode.InputRelay, 0x40, [true, false, true]);
        Assert.True(memory.InputRelays[0x40]);
        Assert.False(memory.InputRelays[0x41]);
        Assert.True(memory.InputRelays[0x42]);
    }

    /// <summary>
    /// MC 设备应能在 1E、4E 和 ASCII 帧下保持同样的高层读写行为。
    /// </summary>
    [Theory]
    [MemberData(nameof(FrameVariants))]
    public async Task McDevice_WritesAndReadsAcrossFrameVariants(McFrameType frameType, McDataEncoding encoding)
    {
        var memory = new McSlaveMemory();
        memory.DataRegisters[100] = 1234;
        memory.InternalRelays[10] = true;
        memory.InternalRelays[12] = true;
        memory.InputRelays[0x10] = true;
        memory.OutputRelays[0x20] = true;
        memory.LinkRegisters[0x30] = 300;
        memory.FileRegisters[70] = 700;
        memory.ExtendedFileRegisters[80] = 800;
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("plc-bus", new McSlaveResponder(memory));
            builder.AddMitsubishiMc("plc", "plc-bus", new McOptions
            {
                FrameType = frameType,
                DataEncoding = encoding,
                SerialNumber = 0x1234
            });
        });

        await host.StartAsync();
        var plc = host.Devices.Get<McDevice>("plc");

        Assert.Equal(new ushort[] { 1234 }, await plc.ReadDataRegistersAsync(100, 1));
        await plc.WriteDataRegistersAsync(110, [10, 20]);
        Assert.Equal(new ushort[] { 10, 20 }, await plc.ReadDataRegistersAsync(110, 2));

        Assert.Equal(new[] { true, false, true, false }, await plc.ReadInternalRelaysAsync(10, 4));
        await plc.WriteInternalRelaysAsync(20, [true, false, true]);
        Assert.Equal(new[] { true, false, true }, await plc.ReadInternalRelaysAsync(20, 3));

        Assert.Equal(new[] { true, false }, await plc.ReadInputRelaysAsync(0x10, 2));
        Assert.Equal(new[] { true, false }, await plc.ReadOutputRelaysAsync(0x20, 2));
        await plc.WriteOutputRelaysAsync(0x22, [false, true]);
        Assert.Equal(new[] { false, true }, await plc.ReadOutputRelaysAsync(0x22, 2));

        Assert.Equal(new ushort[] { 300 }, await plc.ReadLinkRegistersAsync(0x30, 1));
        await plc.WriteLinkRegistersAsync(0x31, [301]);
        Assert.Equal(new ushort[] { 301 }, await plc.ReadLinkRegistersAsync(0x31, 1));

        Assert.Equal(new ushort[] { 700 }, await plc.ReadFileRegistersAsync(70, 1));
        await plc.WriteFileRegistersAsync(71, [701]);
        Assert.Equal(new ushort[] { 701 }, await plc.ReadFileRegistersAsync(71, 1));

        if (frameType != McFrameType.Frame1E)
        {
            Assert.Equal(new ushort[] { 800 }, await plc.ReadExtendedFileRegistersAsync(80, 1));
            await plc.WriteExtendedFileRegistersAsync(81, [801]);
            Assert.Equal(new ushort[] { 801 }, await plc.ReadExtendedFileRegistersAsync(81, 1));
        }
    }

    /// <summary>
    /// MC 设备应能在 3E/4E 下随机读取并随机写入字/双字软元件。
    /// </summary>
    [Theory]
    [MemberData(nameof(RandomFrameVariants))]
    public async Task McDevice_RandomReadsAndWritesWordDevices(McFrameType frameType, McDataEncoding encoding)
    {
        var memory = new McSlaveMemory();
        memory.DataRegisters[10] = 101;
        memory.LinkRegisters[5] = 202;
        memory.DataRegisters[30] = 0x1111;
        memory.DataRegisters[31] = 0x2222;
        memory.ExtendedFileRegisters[70] = 0x5678;
        memory.ExtendedFileRegisters[71] = 0x1234;
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("plc-bus", new McSlaveResponder(memory));
            builder.AddMitsubishiMc("plc", "plc-bus", new McOptions
            {
                FrameType = frameType,
                DataEncoding = encoding,
                SerialNumber = 0x4567
            });
        });

        await host.StartAsync();
        var plc = host.Devices.Get<McDevice>("plc");

        var read = await plc.ReadRandomAsync(
            [new McDeviceAddress(McDeviceCode.DataRegister, 10), new McDeviceAddress(McDeviceCode.LinkRegister, 5)],
            [new McDeviceAddress(McDeviceCode.DataRegister, 30), new McDeviceAddress(McDeviceCode.ExtendedFileRegister, 70)]);

        Assert.Equal(new ushort[] { 101, 202 }, read.WordValues);
        Assert.Equal(new uint[] { 0x22221111, 0x12345678 }, read.DoubleWordValues);

        await plc.WriteRandomWordsAsync(
            [new McWordWrite(McDeviceCode.DataRegister, 11, 111), new McWordWrite(McDeviceCode.LinkRegister, 6, 222)],
            [new McDoubleWordWrite(McDeviceCode.DataRegister, 32, 0x33445566), new McDoubleWordWrite(McDeviceCode.FileRegister, 40, 0x778899AA)]);

        Assert.Equal((ushort)111, memory.DataRegisters[11]);
        Assert.Equal((ushort)222, memory.LinkRegisters[6]);
        Assert.Equal((ushort)0x5566, memory.DataRegisters[32]);
        Assert.Equal((ushort)0x3344, memory.DataRegisters[33]);
        Assert.Equal((ushort)0x99AA, memory.FileRegisters[40]);
        Assert.Equal((ushort)0x7788, memory.FileRegisters[41]);
    }

    /// <summary>
    /// MC 设备应能在 3E/4E 下随机写入位软元件。
    /// </summary>
    [Theory]
    [MemberData(nameof(RandomFrameVariants))]
    public async Task McDevice_RandomWritesBitDevices(McFrameType frameType, McDataEncoding encoding)
    {
        var memory = new McSlaveMemory();
        memory.InternalRelays[102] = true;
        memory.OutputRelays[0x21] = true;
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("plc-bus", new McSlaveResponder(memory));
            builder.AddMitsubishiMc("plc", "plc-bus", new McOptions
            {
                FrameType = frameType,
                DataEncoding = encoding,
                SerialNumber = 0x4567
            });
        });

        await host.StartAsync();
        var plc = host.Devices.Get<McDevice>("plc");

        await plc.WriteRandomBitsAsync(
        [
            new McBitWrite(McDeviceCode.InternalRelay, 100, true),
            new McBitWrite(McDeviceCode.InternalRelay, 102, false),
            new McBitWrite(McDeviceCode.OutputRelay, 0x20, true),
            new McBitWrite(McDeviceCode.OutputRelay, 0x21, false)
        ]);

        Assert.True(memory.InternalRelays[100]);
        Assert.False(memory.InternalRelays[102]);
        Assert.True(memory.OutputRelays[0x20]);
        Assert.False(memory.OutputRelays[0x21]);
    }

    /// <summary>
    /// 1E 帧不支持随机读写时应快速抛出协议异常。
    /// </summary>
    [Theory]
    [InlineData(McDataEncoding.Binary)]
    [InlineData(McDataEncoding.Ascii)]
    public async Task McDevice_RandomAccessRequires3EOr4E(McDataEncoding encoding)
    {
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("plc-bus", new McSlaveResponder());
            builder.AddMitsubishiMc("plc", "plc-bus", new McOptions
            {
                FrameType = McFrameType.Frame1E,
                DataEncoding = encoding
            });
        });

        await host.StartAsync();
        var plc = host.Devices.Get<McDevice>("plc");

        var error = await Assert.ThrowsAsync<ZeusProtocolException>(() => plc.ReadRandomAsync(
            [new McDeviceAddress(McDeviceCode.DataRegister, 10)]));
        Assert.Contains("1E", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 3E/4E 应能一次读取多个不连续字块和位块。
    /// </summary>
    [Theory]
    [MemberData(nameof(RandomFrameVariants))]
    public async Task McDevice_ReadsMultipleBlocks(McFrameType frameType, McDataEncoding encoding)
    {
        var memory = new McSlaveMemory();
        memory.DataRegisters[10] = 11;
        memory.DataRegisters[11] = 22;
        memory.LinkRegisters[5] = 33;
        memory.InternalRelays[100] = true;
        memory.InternalRelays[101] = false;
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("plc-bus", new McSlaveResponder(memory));
            builder.AddMitsubishiMc("plc", "plc-bus", new McOptions
            {
                FrameType = frameType,
                DataEncoding = encoding
            });
        });

        await host.StartAsync();
        var plc = host.Devices.Get<McDevice>("plc");
        var result = await plc.ReadMultipleBlocksAsync(
            [
                new McDeviceRange(McDeviceCode.DataRegister, 10, 2),
                new McDeviceRange(McDeviceCode.LinkRegister, 5, 1)
            ],
            [new McDeviceRange(McDeviceCode.InternalRelay, 100, 2)]);

        Assert.Equal(new ushort[] { 11, 22, 33 }, result.WordValues);
        Assert.Equal(new[] { true, false }, result.BitValues);
    }

    /// <summary>
    /// 远程 RUN/STOP 应更新虚拟 PLC 运行状态。
    /// </summary>
    [Fact]
    public async Task McDevice_RemoteRunAndStop()
    {
        var memory = new McSlaveMemory();
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("plc-bus", new McSlaveResponder(memory));
            builder.AddMitsubishiMc("plc", "plc-bus");
        });

        await host.StartAsync();
        var plc = host.Devices.Get<McDevice>("plc");
        await plc.RemoteStopAsync();
        Assert.False(memory.IsRunning);
        Assert.Equal(McRemoteControlMode.Stop, memory.LastRemoteControl);

        await plc.RemoteRunAsync();
        Assert.True(memory.IsRunning);
        Assert.Equal(McRemoteControlMode.Run, memory.LastRemoteControl);
    }

    /// <summary>
    /// MC 点图应接入宿主采集循环，并支持按点名写回可写软元件。
    /// </summary>
    [Fact]
    public async Task McDevice_PollsAndWritesPointMap()
    {
        var memory = new McSlaveMemory();
        memory.DataRegisters[100] = 123;
        memory.InternalRelays[10] = true;
        memory.InputRelays[0x10] = true;
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromMilliseconds(80));
            builder.AddVirtualChannel("plc-bus", new McSlaveResponder(memory));
            builder.AddMitsubishiMc("plc", "plc-bus", points: map =>
            {
                map.DataRegister("temperature", 100, 0.1, new PointAlarmLimits(low: 5, high: 80))
                    .Writable("temperature");
                map.InternalRelay("run", 10).Writable("run");
                map.InputRelay("ready", 0x10);
            });
        });

        Assert.Equal(12.3, await WaitForPointAsync<double>(host, "temperature"), 3);
        Assert.True(await WaitForPointAsync<bool>(host, "run"));
        Assert.True(await WaitForPointAsync<bool>(host, "ready"));
        Assert.True(host.Points.Get("temperature").Definition.Writable);
        Assert.False(host.Points.Get("ready").Definition.Writable);

        await host.Points.WriteAsync("temperature", 45.6);
        await host.Points.WriteAsync("run", false);

        Assert.Equal((ushort)456, memory.DataRegisters[100]);
        Assert.False(memory.InternalRelays[10]);
        Assert.Equal(45.6, host.Points.Get<double>("temperature"), 3);
        Assert.False(host.Points.Get<bool>("run"));
    }

    /// <summary>
    /// 虚拟 PLC 返回非零结束码时，应暴露为 <see cref="McException"/>。
    /// </summary>
    [Fact]
    public async Task McDevice_InvalidAddressThrowsMcException()
    {
        var memory = new McSlaveMemory(dataRegisters: 8);
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("plc-bus", new McSlaveResponder(memory));
            builder.AddMitsubishiMc("plc", "plc-bus");
        });

        await host.StartAsync();
        var plc = host.Devices.Get<McDevice>("plc");

        var error = await Assert.ThrowsAsync<McException>(() => plc.ReadDataRegistersAsync(100, 1));
        Assert.NotEqual((ushort)0, error.EndCode);
        Assert.Contains("结束码", error.Message, StringComparison.Ordinal);
    }

    private static async Task<T> WaitForPointAsync<T>(IZeusHost host, string name)
    {
        if (!host.IsRunning)
        {
            await host.StartAsync();
        }

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
