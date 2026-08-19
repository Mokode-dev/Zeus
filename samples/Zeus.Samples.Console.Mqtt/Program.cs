using Zeus;

var memory = new MqttBrokerMemory();
memory.SetText("factory/temperature", "25.3");
memory.SetText("factory/running", "true");

await using var host = ZeusHost.Create(builder =>
{
    builder.AddAcquisition(TimeSpan.FromMilliseconds(500));
    builder.AddVirtualChannel("mqtt-link", new MqttBrokerResponder(memory));
    // 真实 Broker：builder.AddTcpClient("mqtt-link", "127.0.0.1", 1883);
    builder.AddMqtt(
        "gateway",
        "mqtt-link",
        new MqttOptions { ClientId = "zeus-mqtt-sample" },
        points: map => map
            .Double("temperature", "factory/temperature")
            .Writable("temperature")
            .Boolean("running", "factory/running"));
});

await host.StartAsync();
await Task.Delay(600);

Console.WriteLine($"temperature = {host.Points.Get<double>("temperature"):0.0}");
Console.WriteLine($"running = {host.Points.Get<bool>("running")}");

await host.Points.WriteAsync("temperature", 18.6);
Console.WriteLine($"new temperature = {host.Points.Get<double>("temperature"):0.0}");

await host.StopAsync();
