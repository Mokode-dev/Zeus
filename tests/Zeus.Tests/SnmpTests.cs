using Zeus;

namespace Zeus.Tests;

/// <summary>通过内存虚拟 Agent 验证 SNMP v2c GET/SET、OID 点表与 JSON 配置。</summary>
public sealed class SnmpTests
{
    [Fact]
    public async Task Client_GetsAndSetsVariables()
    {
        var memory = new SnmpAgentMemory();
        memory.SetGauge32("1.3.6.1.4.1.55555.1.1.0", 253, writable: true);
        memory.SetText("1.3.6.1.4.1.55555.1.2.0", "online", writable: true);

        await using var host = ZeusHost.Create(builder => builder.AddVirtualChannel("snmp-link", new SnmpAgentResponder(memory)));
        await host.StartAsync();
        await using var client = new SnmpClient(host.Channels.Get("snmp-link"));

        var gauge = await client.GetAsync("1.3.6.1.4.1.55555.1.1.0");
        Assert.Equal(SnmpDataType.Gauge32, gauge.DataType);
        Assert.Equal((uint)253, gauge.Value);

        await client.SetAsync("1.3.6.1.4.1.55555.1.1.0", SnmpValue.Gauge32(186));
        Assert.True(memory.TryGet("1.3.6.1.4.1.55555.1.1.0", out var stored));
        Assert.Equal((uint)186, stored!.Value);

        Assert.Equal("online", await client.GetTextAsync("1.3.6.1.4.1.55555.1.2.0"));
    }

    [Fact]
    public async Task Device_PollsAndWritesOidPoints()
    {
        var memory = new SnmpAgentMemory();
        memory.SetGauge32("1.3.6.1.4.1.55555.2.1.0", 253, writable: true);
        memory.SetText("1.3.6.1.4.1.55555.2.2.0", "rack-a", writable: true);

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromMilliseconds(50));
            builder.AddVirtualChannel("snmp-link", new SnmpAgentResponder(memory));
            builder.AddSnmp(
                "ups",
                "snmp-link",
                points: map => map
                    .Gauge32("temperature", "1.3.6.1.4.1.55555.2.1.0", scale: 0.1)
                    .Writable("temperature")
                    .Text("location", "1.3.6.1.4.1.55555.2.2.0"));
        });

        await host.StartAsync();
        Assert.Equal(25.3, await WaitForPointAsync<double>(host, "temperature"), 3);
        Assert.Equal("rack-a", await WaitForPointAsync<string>(host, "location"));

        await host.Points.WriteAsync("temperature", 18.6);
        Assert.Equal(18.6, host.Points.Get<double>("temperature"), 3);
        Assert.True(memory.TryGet("1.3.6.1.4.1.55555.2.1.0", out var stored));
        Assert.Equal((uint)186, stored!.Value);
    }

    [Fact]
    public async Task AddJson_CreatesSnmpDeviceAndWritablePoint()
    {
        const string json = """
            {
              "acquisition": { "intervalMilliseconds": 50, "pollImmediately": true },
              "channels": [
                { "name": "snmp-link", "type": "virtual", "responder": "snmp" }
              ],
              "devices": [
                {
                  "name": "agent",
                  "channel": "snmp-link",
                  "type": "snmp",
                  "points": [
                    { "name": "sysName", "oid": "1.3.6.1.2.1.1.5.0", "dataType": "text", "writable": true }
                  ]
                }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "SNMP 配置"));
        await host.StartAsync();
        var agent = host.Devices.Get<SnmpDevice>("agent");
        Assert.Equal("public", agent.Client.Options.Community);

        Assert.Equal("zeus", await WaitForPointAsync<string>(host, "sysName"));
        await host.Points.WriteAsync("sysName", "edge-01");
        Assert.Equal("edge-01", host.Points.Get<string>("sysName"));
    }

    [Fact]
    public void Configuration_RejectsInvalidOid()
    {
        const string json = """
            {
              "channels": [{ "name": "snmp-link", "type": "virtual", "responder": "snmp" }],
              "devices": [{
                "name": "agent",
                "channel": "snmp-link",
                "type": "snmp",
                "points": [{ "name": "bad", "oid": "1", "dataType": "integer" }]
              }]
            }
            """;

        var error = Assert.Throws<ZeusProtocolException>(() => ZeusConfigurationLoader.LoadJson(json, "SNMP 配置"));
        Assert.Contains("OID", error.Message);
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
