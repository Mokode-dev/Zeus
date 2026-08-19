using Zeus;

var memory = new SnmpAgentMemory();
memory.SetGauge32("1.3.6.1.4.1.55555.10.1.0", 253, writable: true);
memory.SetText("1.3.6.1.4.1.55555.10.2.0", "rack-a", writable: true);

await using var app = ZeusHost.Create(builder =>
{
    builder.AddAcquisition(TimeSpan.FromMilliseconds(200));
    builder.AddVirtualChannel("snmp-link", new SnmpAgentResponder(memory));
    builder.AddSnmp(
        "ups",
        "snmp-link",
        points: map => map
            .Gauge32("temperature", "1.3.6.1.4.1.55555.10.1.0", scale: 0.1)
            .Writable("temperature")
            .Text("location", "1.3.6.1.4.1.55555.10.2.0"));
});

await app.StartAsync();
await Task.Delay(300);

Console.WriteLine($"temperature = {app.Points.Get<double>("temperature"):0.0}");
Console.WriteLine($"location = {app.Points.Get<string>("location")}");

await app.Points.WriteAsync("temperature", 18.6);
Console.WriteLine($"new temperature = {app.Points.Get<double>("temperature"):0.0}");

await app.StopAsync();
