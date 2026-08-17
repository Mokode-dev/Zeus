using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 通过内存从站验证 Modbus RTU/TCP 读写与异常码。
/// </summary>
public sealed class ModbusTests
{
    /// <summary>
    /// RTU 设备应能写入并读回保持寄存器。
    /// </summary>
    [Fact]
    public async Task RtuDevice_WritesAndReadsHoldingRegisters()
    {
        var memory = new ModbusSlaveMemory();
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("oven", "bus", unitId: 1);
        });

        await host.StartAsync();
        var oven = host.Devices.Get<ModbusDevice>("oven");
        await oven.WriteSingleRegisterAsync(10, 250);
        await oven.WriteMultipleRegistersAsync(20, [1, 2, 3]);

        var single = await oven.ReadHoldingRegistersAsync(10, 1);
        var many = await oven.ReadHoldingRegistersAsync(20, 3);

        Assert.Equal((ushort)250, single[0]);
        Assert.Equal(new ushort[] { 1, 2, 3 }, many);
        Assert.Equal((ushort)250, memory.HoldingRegisters[10]);
    }

    /// <summary>
    /// TCP 封装应能读写线圈。
    /// </summary>
    [Fact]
    public async Task TcpDevice_WritesAndReadsCoils()
    {
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Tcp));
            builder.AddModbusTcp("io", "bus", unitId: 1);
        });

        await host.StartAsync();
        var io = host.Devices.Get<ModbusDevice>("io");
        await io.WriteSingleCoilAsync(3, true);
        var coils = await io.ReadCoilsAsync(0, 8);
        Assert.True(coils[3]);
        Assert.False(coils[0]);
    }

    /// <summary>
    /// 功能码 0x0F 应能一次写入多个线圈。
    /// </summary>
    [Fact]
    public async Task RtuDevice_WritesMultipleCoils()
    {
        var memory = new ModbusSlaveMemory();
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("io", "bus");
        });

        await host.StartAsync();
        var io = host.Devices.Get<ModbusDevice>("io");
        await io.WriteMultipleCoilsAsync(1, [true, false, true]);

        Assert.False(memory.Coils[0]);
        Assert.True(memory.Coils[1]);
        Assert.False(memory.Coils[2]);
        Assert.True(memory.Coils[3]);

        var read = await io.ReadCoilsAsync(1, 3);
        Assert.Equal(new[] { true, false, true }, read);
    }

    /// <summary>
    /// 功能码 0x16 应能用 AND / OR 掩码修改单个保持寄存器。
    /// </summary>
    [Fact]
    public async Task RtuDevice_MaskWritesHoldingRegister()
    {
        var memory = new ModbusSlaveMemory();
        memory.HoldingRegisters[7] = 0xAACC;
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("meter", "bus");
        });

        await host.StartAsync();
        var meter = host.Devices.Get<ModbusDevice>("meter");
        await meter.MaskWriteRegisterAsync(7, andMask: 0xFFF0, orMask: 0x0005);

        Assert.Equal((ushort)0xAAC5, memory.HoldingRegisters[7]);
        var read = await meter.ReadHoldingRegistersAsync(7, 1);
        Assert.Equal((ushort)0xAAC5, read[0]);
    }

    /// <summary>
    /// 功能码 0x17 应先写入保持寄存器，再返回读取区间。
    /// </summary>
    [Fact]
    public async Task RtuDevice_ReadWriteMultipleRegisters()
    {
        var memory = new ModbusSlaveMemory();
        memory.HoldingRegisters[1] = 10;
        memory.HoldingRegisters[2] = 20;
        memory.HoldingRegisters[3] = 30;
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("meter", "bus");
        });

        await host.StartAsync();
        var meter = host.Devices.Get<ModbusDevice>("meter");
        var read = await meter.ReadWriteMultipleRegistersAsync(
            readAddress: 1,
            readQuantity: 3,
            writeAddress: 2,
            writeValues: [200, 300]);

        Assert.Equal(new ushort[] { 10, 200, 300 }, read);
        Assert.Equal((ushort)200, memory.HoldingRegisters[2]);
        Assert.Equal((ushort)300, memory.HoldingRegisters[3]);
    }

    /// <summary>
    /// 诊断类功能码应能读取异常状态、执行回显并报告服务器 ID。
    /// </summary>
    [Fact]
    public async Task RtuDevice_ReadsDiagnosticsAndServerId()
    {
        var memory = new ModbusSlaveMemory
        {
            ExceptionStatus = 0b_0000_0101,
            ServerId = 0x42,
            ServerRunIndicatorStatus = true,
            ServerIdAdditionalData = [0x10, 0x20, 0x30]
        };
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("meter", "bus");
        });

        await host.StartAsync();
        var meter = host.Devices.Get<ModbusDevice>("meter");

        Assert.Equal((byte)0b_0000_0101, await meter.ReadExceptionStatusAsync());
        Assert.Equal((ushort)0xA55A, await meter.DiagnosticsReturnQueryDataAsync(0xA55A));

        var serverId = await meter.ReportServerIdAsync();
        Assert.Equal((byte)0x42, serverId.ServerId);
        Assert.True(serverId.RunIndicatorStatus);
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, serverId.AdditionalData);
    }

    /// <summary>
    /// 越界地址必须变成可识别的 <see cref="ModbusException"/>。
    /// </summary>
    [Fact]
    public async Task IllegalAddress_ThrowsModbusException()
    {
        var memory = new ModbusSlaveMemory(holdingRegisters: 8);
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("oven", "bus");
        });

        await host.StartAsync();
        var oven = host.Devices.Get<ModbusDevice>("oven");
        var error = await Assert.ThrowsAsync<ModbusException>(() => oven.ReadHoldingRegistersAsync(100, 1));
        Assert.Equal(ModbusExceptionCode.IllegalDataAddress, error.Code);
        Assert.Contains("非法数据地址", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 设备必须在通道之后注册，否则构建期给出可操作错误。
    /// </summary>
    [Fact]
    public void AddDevice_BeforeChannel_FailsAtBuild()
    {
        var error = Assert.Throws<ZeusException>(() =>
            ZeusHost.Create(builder => builder.AddModbusRtu("oven", "missing")));
        Assert.Contains("missing", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AddSerialPort", error.Message, StringComparison.Ordinal);
    }
}
