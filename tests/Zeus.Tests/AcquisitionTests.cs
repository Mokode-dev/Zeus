using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 验证点表查找、周期采集、连续寄存器合并以及采集失败时保留旧值。
/// </summary>
public sealed class AcquisitionTests
{
    /// <summary>
    /// 声明点表后，宿主启动应立刻采到虚拟从站中的初值。
    /// </summary>
    [Fact]
    public async Task Host_PollsDeclaredPointsImmediately()
    {
        var memory = new ModbusSlaveMemory();
        memory.HoldingRegisters[0] = 185;
        memory.HoldingRegisters[1] = 30;
        memory.Coils[2] = true;

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromMilliseconds(200));
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("oven", "bus", points: map =>
            {
                map.HoldingRegister("temperature", 0, raw => raw * 0.1);
                map.HoldingRegister("setpoint", 1);
                map.Coil("heater", 2);
            });
        });

        var temperature = await WaitForPointAsync<double>(host, "temperature");
        Assert.Equal(18.5, temperature, 3);
        Assert.Equal((ushort)30, await WaitForPointAsync<ushort>(host, "setpoint"));
        Assert.True(await WaitForPointAsync<bool>(host, "heater"));
    }

    /// <summary>
    /// 从站映像变化后，下一轮采集应更新点表并触发 Changed。
    /// </summary>
    [Fact]
    public async Task PointTable_PublishesChangeWhenValueUpdates()
    {
        var memory = new ModbusSlaveMemory();
        memory.HoldingRegisters[0] = 100;
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromMilliseconds(80));
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("oven", "bus", points: map => map.HoldingRegister("pv", 0));
        });

        await WaitForPointEqualsAsync<ushort>(host, "pv", 100);
        var changed = new TaskCompletionSource<ushort>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Points.Changed += (_, e) =>
        {
            if (e.Current.Definition.Name == "pv" && e.Current.Value is ushort value && value == 180)
            {
                changed.TrySetResult(value);
            }
        };

        memory.HoldingRegisters[0] = 180;
        var actual = await changed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal((ushort)180, actual);
    }

    /// <summary>
    /// 短名冲突时必须要求使用限定名。
    /// </summary>
    [Fact]
    public async Task AmbiguousShortName_RequiresQualifiedName()
    {
        var memory = new ModbusSlaveMemory();
        await using var host = ZeusHost.Create(builder =>
        {
            // 本用例只验证点名解析，拉长间隔避免两台设备抢同一从站。
            builder.AddAcquisition(options =>
            {
                options.PollImmediately = false;
                options.Interval = TimeSpan.FromHours(1);
            });
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("oven", "bus", points: map => map.HoldingRegister("pv", 0));
            builder.AddModbusRtu("dryer", "bus", unitId: 1, points: map => map.HoldingRegister("pv", 1));
        });

        await host.StartAsync();
        var error = Assert.Throws<ZeusException>(() => host.Points.Get("pv"));
        Assert.Contains("oven.pv", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(host.Points.Get("oven.pv"));
    }

    /// <summary>
    /// 采集失败应保留旧值，并在快照上记录错误。
    /// </summary>
    [Fact]
    public void PollFailure_KeepsLastValueAndRecordsError()
    {
        var table = new PointTable();
        var definition = new PointDefinition("pv", "oven", PointValueKind.UInt16);
        table.Register(definition);
        table.Publish("oven.pv", (ushort)12);

        table.PublishError("oven.pv", "从站超时");
        var snapshot = table.Get("pv");
        Assert.Equal((ushort)12, snapshot.Value);
        Assert.Equal("从站超时", snapshot.Error);
    }

    /// <summary>
    /// 点定义配置报警限后，快照应随当前值给出低报、正常或高报状态。
    /// </summary>
    [Fact]
    public void PointTable_EvaluatesAlarmLimits()
    {
        var table = new PointTable();
        table.Register(new PointDefinition(
            "pv",
            "oven",
            PointValueKind.Double,
            new PointAlarmLimits(low: 10, high: 80)));

        Assert.Equal(PointAlarmState.Unknown, table.Get("pv").AlarmState);

        table.Publish("oven.pv", 8d);
        Assert.Equal(PointAlarmState.Low, table.Get("pv").AlarmState);
        Assert.True(table.Get("pv").IsAlarmed);

        table.Publish("oven.pv", 40d);
        Assert.Equal(PointAlarmState.Normal, table.Get("pv").AlarmState);
        Assert.False(table.Get("pv").IsAlarmed);

        table.Publish("oven.pv", 90d);
        table.PublishError("oven.pv", "从站超时");
        var snapshot = table.Get("pv");
        Assert.Equal(PointAlarmState.High, snapshot.AlarmState);
        Assert.True(snapshot.IsAlarmed);
        Assert.Equal("从站超时", snapshot.Error);
    }

    /// <summary>
    /// Modbus 点报警限应按寄存器换算后的工程值判断，而不是原始寄存器值。
    /// </summary>
    [Fact]
    public async Task ModbusPointMap_AlarmLimitsUseConvertedValue()
    {
        var memory = new ModbusSlaveMemory();
        memory.HoldingRegisters[0] = 185;

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromMilliseconds(100));
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("oven", "bus", points: map =>
            {
                map.HoldingRegister(
                    "temperature",
                    0,
                    raw => raw * 0.1,
                    new PointAlarmLimits(high: 18));
            });
        });

        Assert.Equal(18.5, await WaitForPointAsync<double>(host, "temperature"), 3);
        var snapshot = host.Points.Get("temperature");
        Assert.Equal(PointAlarmState.High, snapshot.AlarmState);
        Assert.True(snapshot.IsAlarmed);
    }

    /// <summary>
    /// 点表历史只保留成功采样，并按容量裁剪旧样本。
    /// </summary>
    [Fact]
    public void PointTable_HistoryKeepsRecentSuccessfulSamples()
    {
        var table = new PointTable(historyCapacity: 2);
        table.Register(new PointDefinition("pv", "oven", PointValueKind.UInt16));

        table.Publish("oven.pv", (ushort)10);
        table.Publish("oven.pv", (ushort)20);
        table.Publish("oven.pv", (ushort)30);

        var history = table.GetHistory("pv");
        Assert.Equal(2, history.Count);
        Assert.Equal((ushort)20, history[0].Value);
        Assert.Equal((ushort)30, history[1].Value);
        Assert.All(history, item =>
        {
            Assert.Null(item.Error);
            Assert.NotNull(item.UpdatedAt);
        });
    }

    /// <summary>
    /// 采集错误会更新当前快照，但不会污染成功采样历史。
    /// </summary>
    [Fact]
    public void PointTable_HistoryIgnoresErrors()
    {
        var table = new PointTable(historyCapacity: 4);
        table.Register(new PointDefinition("pv", "oven", PointValueKind.UInt16));

        table.Publish("oven.pv", (ushort)12);
        table.PublishError("oven.pv", "从站超时");

        var current = table.Get("pv");
        var history = table.GetHistory("pv");
        Assert.Equal("从站超时", current.Error);
        Assert.Single(history);
        Assert.Equal((ushort)12, history[0].Value);
        Assert.Null(history[0].Error);
    }

    /// <summary>
    /// 卸载设备后，点表应摘除该设备的点，并恢复被占用的短名。
    /// </summary>
    [Fact]
    public void PointTable_UnregisterDevice_RemovesPointsAndRestoresShortName()
    {
        var table = new PointTable();
        table.Register(new PointDefinition("pv", "oven", PointValueKind.UInt16));
        table.Register(new PointDefinition("pv", "dryer", PointValueKind.UInt16));
        table.Publish("oven.pv", (ushort)1);
        table.Publish("dryer.pv", (ushort)2);

        var error = Assert.Throws<ZeusException>(() => table.Get("pv"));
        Assert.Contains("oven.pv", error.Message, StringComparison.OrdinalIgnoreCase);

        table.UnregisterDevice("dryer");
        Assert.Equal((ushort)1, table.Get<ushort>("pv"));
        Assert.Throws<ZeusException>(() => table.Get("dryer.pv"));
    }

    /// <summary>
    /// 可写点应按名称把工程值写回从站，并立刻更新点表。
    /// </summary>
    [Fact]
    public async Task PointTable_WriteAsync_WritesHoldingRegisterAndCoil()
    {
        var memory = new ModbusSlaveMemory();
        memory.HoldingRegisters[1] = 30;
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromHours(1));
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("oven", "bus", points: map =>
            {
                map.HoldingRegister("setpoint", 1, 0.1).Writable("setpoint");
                map.Coil("heater", 2).Writable("heater");
            });
        });

        await host.StartAsync();
        await host.Points.WriteAsync("setpoint", 80.0);
        await host.Points.WriteAsync("heater", true);

        Assert.Equal((ushort)800, memory.HoldingRegisters[1]);
        Assert.True(memory.Coils[2]);
        Assert.Equal(80.0, host.Points.Get<double>("setpoint"), 3);
        Assert.True(host.Points.Get<bool>("heater"));
        Assert.Null(host.Points.Get("setpoint").Error);
    }

    /// <summary>
    /// 只读点写回应失败，且不改从站映像。
    /// </summary>
    [Fact]
    public async Task PointTable_WriteAsync_RejectsReadOnlyPoint()
    {
        var memory = new ModbusSlaveMemory();
        memory.HoldingRegisters[0] = 185;
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(options =>
            {
                options.PollImmediately = false;
                options.Interval = TimeSpan.FromHours(1);
            });
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("oven", "bus", points: map =>
            {
                map.HoldingRegister("temperature", 0, 0.1);
            });
        });

        await host.StartAsync();
        var error = await Assert.ThrowsAsync<ZeusException>(() => host.Points.WriteAsync("temperature", 20.0));
        Assert.Contains("只读", error.Message, StringComparison.Ordinal);
        Assert.Equal((ushort)185, memory.HoldingRegisters[0]);
    }

    /// <summary>
    /// 自定义换算函数没有 scale 时，不能标为可写。
    /// </summary>
    [Fact]
    public void ModbusPointMap_CustomConvertCannotBeWritable()
    {
        var map = new ModbusPointMap();
        map.HoldingRegister("temperature", 0, raw => raw * 0.1);
        var error = Assert.Throws<ZeusException>(() => map.Writable("temperature"));
        Assert.Contains("自定义换算", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 输入寄存器不能标为可写。
    /// </summary>
    [Fact]
    public void ModbusPointMap_InputRegisterCannotBeWritable()
    {
        var map = new ModbusPointMap();
        map.InputRegister("status", 0);
        var error = Assert.Throws<ZeusException>(() => map.Writable("status"));
        Assert.Contains("只读", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 写回失败应把错误记到点快照，并重新抛出。
    /// </summary>
    [Fact]
    public async Task PointTable_WriteAsync_RecordsErrorWhenDeviceRejects()
    {
        var memory = new ModbusSlaveMemory(holdingRegisters: 1);
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(options =>
            {
                options.PollImmediately = false;
                options.Interval = TimeSpan.FromHours(1);
            });
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("oven", "bus", points: map =>
            {
                map.HoldingRegister("setpoint", 200).Writable("setpoint");
            });
        });

        await host.StartAsync();
        await Assert.ThrowsAsync<ModbusException>(() => host.Points.WriteAsync("setpoint", (ushort)1));
        Assert.False(string.IsNullOrWhiteSpace(host.Points.Get("setpoint").Error));
    }

    /// <summary>
    /// 未连接设备目录的点表不能写回。
    /// </summary>
    [Fact]
    public async Task PointTable_WriteAsync_RequiresDeviceRegistry()
    {
        var table = new PointTable();
        table.Register(new PointDefinition("setpoint", "oven", PointValueKind.UInt16, null, writable: true));
        var error = await Assert.ThrowsAsync<ZeusException>(() => table.WriteAsync("setpoint", (ushort)1));
        Assert.Contains("设备目录", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// BindText 应在点变化时更新，释放后不再接收。
    /// </summary>
    [Fact]
    public void BindText_UpdatesAndUnsubscribes()
    {
        var table = new PointTable();
        table.Register(new PointDefinition("pv", "oven", PointValueKind.UInt16));
        var texts = new List<string>();
        var binding = table.BindText("pv", ImmediateUiDispatcher.Instance, texts.Add);

        table.Publish("oven.pv", (ushort)7);
        binding.Dispose();
        table.Publish("oven.pv", (ushort)8);

        Assert.Equal(new[] { string.Empty, "7" }, texts);
    }

    private static Task<T> WaitForPointAsync<T>(IZeusHost host, string name)
        => WaitCoreAsync<T>(host, name, hasExpected: false, expected: default);

    private static Task<T> WaitForPointEqualsAsync<T>(IZeusHost host, string name, T expected)
        => WaitCoreAsync(host, name, hasExpected: true, expected);

    private static async Task<T> WaitCoreAsync<T>(IZeusHost host, string name, bool hasExpected, T? expected)
    {
        if (host.Points.All.Count == 0)
        {
            await host.StartAsync();
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (host.Points.TryGet<T>(name, out var value) && value is not null)
            {
                if (!hasExpected || Equals(value, expected))
                {
                    return value;
                }
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"等待点 {name} 超时。");
    }
}
