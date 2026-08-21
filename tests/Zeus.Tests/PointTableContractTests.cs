using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 验证点表查找、批次事件、工程值读取与报警回差。这些是框架对业务上位机应收掉的税。
/// </summary>
public sealed class PointTableContractTests
{
    /// <summary>
    /// 点尚未登记时 TryGet 返回 false，而不是抛出「尚未登记任何点」。
    /// </summary>
    [Fact]
    public void TryGet_ReturnsFalseWhenPointMissing()
    {
        var table = new PointTable();
        Assert.False(table.TryGet("pv", out var snapshot));
        Assert.Null(snapshot);
        Assert.False(table.TryGetDouble("pv", out _));
    }

    /// <summary>
    /// 设备一登记，点就出现在点表中，不必等到 StartAsync。
    /// </summary>
    [Fact]
    public async Task Host_RegistersPointsBeforeStart()
    {
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(options =>
            {
                options.PollImmediately = false;
                options.Interval = TimeSpan.FromHours(1);
            });
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu));
            builder.AddModbusRtu("oven", "bus", points: map => map.HoldingRegister("pv", 0));
        });

        Assert.True(host.Points.TryGet("pv", out var snapshot));
        Assert.NotNull(snapshot);
        Assert.Equal("oven.pv", snapshot!.QualifiedName);
        Assert.Null(snapshot.Value);
    }

    /// <summary>
    /// 一轮采集应合并为一次 BatchChanged，避免界面按每个点重绘。
    /// </summary>
    [Fact]
    public void PointTable_BatchChangedCoalescesPublishes()
    {
        var table = new PointTable();
        table.Register(new PointDefinition("a", "oven", PointValueKind.UInt16));
        table.Register(new PointDefinition("b", "oven", PointValueKind.UInt16));
        var batches = 0;
        var points = 0;
        table.Changed += (_, _) => points++;
        table.BatchChanged += (_, e) =>
        {
            batches++;
            Assert.Equal(2, e.Changes.Count);
        };

        table.BeginBatch();
        table.Publish("oven.a", (ushort)1);
        table.Publish("oven.b", (ushort)2);
        table.EndBatch();

        Assert.Equal(2, points);
        Assert.Equal(1, batches);
    }

    /// <summary>
    /// Subscribe 只接收匹配点名的变化。
    /// </summary>
    [Fact]
    public void PointTable_SubscribeFiltersByName()
    {
        var table = new PointTable();
        table.Register(new PointDefinition("pv", "oven", PointValueKind.UInt16));
        table.Register(new PointDefinition("sv", "oven", PointValueKind.UInt16));
        var hits = 0;
        using var sub = table.Subscribe("pv", (_, e) =>
        {
            hits++;
            Assert.Equal("oven.pv", e.Current.QualifiedName);
        });

        table.Publish("oven.pv", (ushort)1);
        table.Publish("oven.sv", (ushort)2);
        Assert.Equal(1, hits);
    }

    /// <summary>
    /// TryGetDouble 把原始寄存器和带 scale 的工程值都读成 double。
    /// </summary>
    [Fact]
    public void TryGetDouble_ReadsRawAndScaledValues()
    {
        var table = new PointTable();
        table.Register(new PointDefinition("raw", "oven", PointValueKind.UInt16));
        table.Register(new PointDefinition("eng", "oven", PointValueKind.Double));
        table.Publish("oven.raw", (ushort)185);
        table.Publish("oven.eng", 18.5);

        Assert.True(table.TryGetDouble("raw", out var raw));
        Assert.Equal(185d, raw);
        Assert.True(table.TryGetDouble("eng", out var eng));
        Assert.Equal(18.5, eng);
    }

    /// <summary>
    /// 报警回差：已高报时，值回到阈值内侧但未越过回差仍保持高报。
    /// </summary>
    [Fact]
    public void AlarmLimits_DeadbandHoldsUntilCleared()
    {
        var limits = new PointAlarmLimits(high: 80, deadband: 5);
        Assert.Equal(PointAlarmState.High, limits.Evaluate(81d, PointAlarmState.Normal));
        Assert.Equal(PointAlarmState.High, limits.Evaluate(76d, PointAlarmState.High));
        Assert.Equal(PointAlarmState.Normal, limits.Evaluate(74d, PointAlarmState.High));
    }

    /// <summary>
    /// JSON 与代码都应按有符号寄存器解释放电电流，而不是把补码当成大正数。
    /// </summary>
    [Fact]
    public async Task ModbusSignedRegister_DecodesNegativeCurrent()
    {
        var memory = new ModbusSlaveMemory();
        memory.HoldingRegisters[1] = unchecked((ushort)(short)(-1234));
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromMilliseconds(50));
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("pack", "bus", points: map =>
                map.HoldingRegister("current", 1, 0.01, signed: true));
        });

        await host.StartAsync();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        double? current = null;
        while (DateTime.UtcNow < deadline)
        {
            if (host.Points.TryGetDouble("current", out var value))
            {
                current = value;
                break;
            }

            await Task.Delay(20);
        }

        Assert.True(current.HasValue, "等待有符号电流超时。");
        Assert.Equal(-12.34, current.Value, 3);
    }

    /// <summary>
    /// JSON signed: true 应与代码声明得到同一工程值。
    /// </summary>
    [Fact]
    public async Task JsonSignedRegister_MatchesCodePath()
    {
        const string json = """
            {
              "acquisition": { "intervalMilliseconds": 50, "pollImmediately": true },
              "channels": [ { "name": "bus", "type": "virtual", "responder": "modbus", "unitId": 1, "transport": "rtu" } ],
              "devices": [
                {
                  "name": "pack",
                  "channel": "bus",
                  "type": "modbus-rtu",
                  "points": [
                    { "name": "current", "table": "holding", "address": 1, "scale": 0.01, "signed": true }
                  ]
                }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "有符号电流"));
        var memory = host.Channels.Get("bus");
        Assert.NotNull(memory);

        var device = host.Devices.Get<ModbusDevice>("pack");
        await host.StartAsync();
        await device.WriteSingleRegisterAsync(1, unchecked((ushort)(short)(-2500)));
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        double? current = null;
        while (DateTime.UtcNow < deadline)
        {
            if (host.Points.TryGetDouble("current", out var value) && value < 0)
            {
                current = value;
                break;
            }

            await Task.Delay(20);
        }

        Assert.Equal(-25.0, current!.Value, 3);
    }
}
