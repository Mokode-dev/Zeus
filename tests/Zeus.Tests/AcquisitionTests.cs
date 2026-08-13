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
