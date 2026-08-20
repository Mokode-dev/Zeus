using System.Text;
using Zeus;

namespace Zeus.Tests;

/// <summary>通过内存虚拟 Broker 验证 MQTT 会话、主题点表与 JSON 配置。</summary>
public sealed class MqttTests
{
    [Fact]
    public async Task Client_ConnectsSubscribesPublishesAndPings()
    {
        var memory = new MqttBrokerMemory();
        memory.SetText("factory/temperature", "25.3");
        var broker = new MqttBrokerResponder(memory);

        await using var host = ZeusHost.Create(builder => builder.AddVirtualChannel("mqtt-link", broker));
        await host.StartAsync();
        await using var client = new MqttClient(host.Channels.Get("mqtt-link"), new MqttOptions { ClientId = "test-client" });

        await client.ConnectAsync();
        await client.SubscribeAsync("factory/#");
        var retained = await client.WaitForMessageAsync("factory/temperature");
        Assert.Equal("25.3", Encoding.UTF8.GetString(retained.Payload));
        Assert.True(retained.Retain);

        await client.PublishAsync("factory/temperature", "18.6"u8.ToArray(), retain: true);
        var published = await client.WaitForMessageAsync("factory/temperature");
        Assert.Equal("18.6", Encoding.UTF8.GetString(published.Payload));
        Assert.True(memory.TryGet("factory/temperature", out var stored));
        Assert.Equal("18.6", Encoding.UTF8.GetString(stored));

        await client.PingAsync();
    }

    [Fact]
    public async Task Client_SupportsQosOneQosTwoAndUnsubscribe()
    {
        var broker = new MqttBrokerResponder();
        await using var host = ZeusHost.Create(builder => builder.AddVirtualChannel("mqtt-link", broker));
        await host.StartAsync();
        await using var client = new MqttClient(host.Channels.Get("mqtt-link"), new MqttOptions { ClientId = "qos-client" });

        await client.ConnectAsync();
        await client.SubscribeAsync("factory/#", MqttQualityOfService.ExactlyOnce);

        await client.PublishAsync("factory/qos1", "one"u8.ToArray(), MqttQualityOfService.AtLeastOnce, retain: true);
        var qosOne = await client.WaitForMessageAsync("factory/qos1");
        Assert.Equal(MqttQualityOfService.AtLeastOnce, qosOne.QualityOfService);
        Assert.Equal("one", Encoding.UTF8.GetString(qosOne.Payload));

        await client.PublishAsync("factory/qos2", "two"u8.ToArray(), MqttQualityOfService.ExactlyOnce, retain: true);
        var qosTwo = await client.WaitForMessageAsync("factory/qos2");
        Assert.Equal(MqttQualityOfService.ExactlyOnce, qosTwo.QualityOfService);
        Assert.Equal("two", Encoding.UTF8.GetString(qosTwo.Payload));

        await client.UnsubscribeAsync("factory/#");
        client.DrainMessages();
        await client.PublishAsync("factory/after-unsubscribe", "ignored"u8.ToArray());
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.WaitForMessageAsync("factory/after-unsubscribe", timeout.Token));
    }

    [Fact]
    public async Task Client_EncodesWillDeletesRetainedMessageAndKeepsAlive()
    {
        var memory = new MqttBrokerMemory();
        var broker = new MqttBrokerResponder(memory);
        await using var host = ZeusHost.Create(builder => builder.AddVirtualChannel("mqtt-link", broker));
        await host.StartAsync();
        await using var client = new MqttClient(
            host.Channels.Get("mqtt-link"),
            new MqttOptions
            {
                ClientId = "will-client",
                KeepAliveSeconds = 1,
                WillTopic = "factory/status",
                WillPayload = "offline"u8.ToArray(),
                WillQualityOfService = MqttQualityOfService.AtLeastOnce,
                WillRetain = true
            });

        await client.ConnectAsync();
        Assert.NotNull(broker.WillMessage);
        Assert.Equal("factory/status", broker.WillMessage!.Topic);
        Assert.Equal("offline", Encoding.UTF8.GetString(broker.WillMessage.Payload));
        Assert.Equal(MqttQualityOfService.AtLeastOnce, broker.WillMessage.QualityOfService);
        Assert.True(broker.WillMessage.Retain);

        await client.PublishAsync("factory/retained", "value"u8.ToArray(), MqttQualityOfService.AtLeastOnce, retain: true);
        Assert.True(memory.TryGet("factory/retained", out _));
        await client.PublishAsync("factory/retained", ReadOnlyMemory<byte>.Empty, MqttQualityOfService.AtLeastOnce, retain: true);
        Assert.False(memory.TryGet("factory/retained", out _));

        await WaitUntilAsync(() => broker.PingRequestCount > 0, TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Client_ReconnectsAndRestoresSubscriptionsAfterChannelReopens()
    {
        var broker = new MqttBrokerResponder();
        await using var host = ZeusHost.Create(builder => builder.AddVirtualChannel("mqtt-link", broker));
        await host.StartAsync();
        var channel = host.Channels.Get("mqtt-link");
        await using var client = new MqttClient(
            channel,
            new MqttOptions
            {
                ClientId = "reconnect-client",
                ReconnectInitialDelay = TimeSpan.FromMilliseconds(10),
                ReconnectMaxDelay = TimeSpan.FromMilliseconds(50)
            });

        await client.ConnectAsync();
        await client.SubscribeAsync("factory/#", MqttQualityOfService.AtLeastOnce);
        await channel.CloseAsync();
        await channel.OpenAsync();

        await WaitUntilAsync(() => client.IsConnected && broker.ConnectCount >= 2, TimeSpan.FromSeconds(2));
        await client.PublishAsync("factory/reconnected", "ok"u8.ToArray(), MqttQualityOfService.AtLeastOnce);
        var message = await client.WaitForMessageAsync("factory/reconnected");
        Assert.Equal("ok", Encoding.UTF8.GetString(message.Payload));
    }

    [Fact]
    public async Task Device_PollsAndWritesTopicPoints()
    {
        var memory = new MqttBrokerMemory();
        memory.SetText("factory/temperature", "25.3");
        memory.SetText("factory/running", "true");

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromMilliseconds(50));
            builder.AddVirtualChannel("mqtt-link", new MqttBrokerResponder(memory));
            builder.AddMqtt(
                "gateway",
                "mqtt-link",
                points: map => map
                    .Double("temperature", "factory/temperature")
                    .Writable("temperature")
                    .Boolean("running", "factory/running"));
        });

        await host.StartAsync();
        Assert.Equal(25.3, await WaitForPointAsync<double>(host, "temperature"), 3);
        Assert.True(await WaitForPointAsync<bool>(host, "running"));

        await host.Points.WriteAsync("temperature", 18.6);
        Assert.Equal(18.6, host.Points.Get<double>("temperature"), 3);
        Assert.True(memory.TryGet("factory/temperature", out var stored));
        Assert.Equal("18.6", Encoding.UTF8.GetString(stored));
    }

    [Fact]
    public async Task AddJson_CreatesMqttDeviceAndWritablePoint()
    {
        const string json = """
            {
              "acquisition": { "intervalMilliseconds": 50, "pollImmediately": true },
              "channels": [
                { "name": "mqtt-link", "type": "virtual", "responder": "mqtt" }
              ],
              "devices": [
                {
                  "name": "gateway",
                  "channel": "mqtt-link",
                  "type": "mqtt",
                  "mqttClientId": "zeus-json-test",
                  "points": [
                    { "name": "setpoint", "topic": "factory/setpoint", "dataType": "double", "writable": true }
                  ]
                }
              ]
            }
            """;

        await using var host = ZeusHost.Create(builder => builder.AddJson(json, "MQTT 配置"));
        await host.StartAsync();
        var gateway = host.Devices.Get<MqttDevice>("gateway");
        Assert.Equal("zeus-json-test", gateway.Client.Options.ClientId);

        await host.Points.WriteAsync("setpoint", 12.5);
        Assert.Equal(12.5, host.Points.Get<double>("setpoint"), 3);
    }

    [Fact]
    public void Configuration_RejectsPublishTopicWildcard()
    {
        const string json = """
            {
              "channels": [{ "name": "mqtt-link", "type": "virtual", "responder": "mqtt" }],
              "devices": [{
                "name": "gateway",
                "channel": "mqtt-link",
                "type": "mqtt",
                "points": [{ "name": "bad", "topic": "factory/+", "dataType": "text" }]
              }]
            }
            """;

        var error = Assert.Throws<ZeusException>(() => ZeusConfigurationLoader.LoadJson(json, "MQTT 配置"));
        Assert.Contains("不能包含 MQTT 通配符", error.Message);
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

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("等待 MQTT 条件超时。");
    }

    [Fact]
    public async Task QosPublish_DoesNotDeadlockOnVirtualChannel()
    {
        var broker = new MqttBrokerResponder();
        await using var host = ZeusHost.Create(builder => builder.AddVirtualChannel("mqtt-link", broker));
        await host.StartAsync();
        await using var client = new MqttClient(host.Channels.Get("mqtt-link"), new MqttOptions { ClientId = "deadlock-client" });

        await client.ConnectAsync();
        await client.SubscribeAsync("factory/#", MqttQualityOfService.ExactlyOnce);
        await client.PublishAsync("factory/qos2", "payload"u8.ToArray(), MqttQualityOfService.ExactlyOnce, retain: true)
            .WaitAsync(TimeSpan.FromSeconds(3));
        var message = await client.WaitForMessageAsync("factory/qos2").WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("payload", Encoding.UTF8.GetString(message.Payload));
    }
}
