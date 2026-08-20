using System.Text.Json;
using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 验证报警队列、确认/复归与点历史落盘。
/// </summary>
public sealed class AlarmAndHistoryTests
{
    /// <summary>
    /// 点越限应产生活动报警，回到正常范围后自动复归。
    /// </summary>
    [Fact]
    public void AlarmTable_RaisesAndClearsWhenValueCrossesLimits()
    {
        var table = new PointTable();
        var alarms = new PointAlarmTable(table, historyCapacity: 8);
        table.Register(new PointDefinition("pv", "oven", PointValueKind.Double, new PointAlarmLimits(low: 10, high: 80)));

        table.Publish("oven.pv", 90d);
        Assert.Single(alarms.Active);
        Assert.Equal(PointAlarmStatus.Active, alarms.Active[0].Status);
        Assert.Equal(PointAlarmState.High, alarms.Active[0].AlarmState);

        table.Publish("oven.pv", 50d);
        Assert.Empty(alarms.Active);
        Assert.Single(alarms.History);
        Assert.Equal(PointAlarmStatus.Cleared, alarms.History[0].Status);
    }

    /// <summary>
    /// 确认后状态变为已确认；点复归后进入历史并保留确认人。
    /// </summary>
    [Fact]
    public void AlarmTable_AcknowledgeKeepsRecordUntilCleared()
    {
        var table = new PointTable();
        var alarms = new PointAlarmTable(table);
        table.Register(new PointDefinition("pv", "oven", PointValueKind.Double, new PointAlarmLimits(high: 10)));
        table.Publish("oven.pv", 20d);

        var acknowledged = alarms.AcknowledgePoint("pv", "operator");
        Assert.NotNull(acknowledged);
        Assert.Equal(PointAlarmStatus.Acknowledged, acknowledged!.Status);
        Assert.Equal("operator", acknowledged.AcknowledgedBy);
        Assert.Single(alarms.Active);

        table.Publish("oven.pv", 5d);
        Assert.Empty(alarms.Active);
        Assert.Equal("operator", alarms.History[0].AcknowledgedBy);
        Assert.Equal(PointAlarmStatus.Cleared, alarms.History[0].Status);
    }

    /// <summary>
    /// 宿主应暴露报警队列，采集越限后可通过 Alarms 读取。
    /// </summary>
    [Fact]
    public async Task Host_ExposesAlarmsFromAcquisition()
    {
        var memory = new ModbusSlaveMemory();
        memory.HoldingRegisters[0] = 900;
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromMilliseconds(50));
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("oven", "bus", points: map =>
                map.HoldingRegister("temperature", 0, 0.1, new PointAlarmLimits(high: 80)));
        });

        await host.StartAsync();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && host.Alarms.Active.Count == 0)
        {
            await Task.Delay(20);
        }

        Assert.NotEmpty(host.Alarms.Active);
        Assert.Equal("oven.temperature", host.Alarms.Active[0].QualifiedName);
        host.Alarms.AcknowledgeAll("qa");
        Assert.Equal(PointAlarmStatus.Acknowledged, host.Alarms.Active[0].Status);
    }

    /// <summary>
    /// 文件历史存储应把成功采样写成 JSONL。
    /// </summary>
    [Fact]
    public async Task FilePointHistoryStore_AppendsJsonLines()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zeus-history-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using var store = new FilePointHistoryStore(path);
            var table = new PointTable(null, 8, 64, store);
            table.Register(new PointDefinition("pv", "oven", PointValueKind.UInt16));
            table.Publish("oven.pv", (ushort)12);
            await Task.Delay(100);

            var line = (await File.ReadAllTextAsync(path)).Trim();
            using var document = JsonDocument.Parse(line);
            Assert.Equal("oven.pv", document.RootElement.GetProperty("qualifiedName").GetString());
            Assert.Equal("12", document.RootElement.GetProperty("value").GetString());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
