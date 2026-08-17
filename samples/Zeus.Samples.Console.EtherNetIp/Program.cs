using Zeus;

// 无硬件演示：虚拟 Allen-Bradley EtherNet/IP PLC 应答 Register Session 与 CIP Read/Write Tag。
// 现场联调时，把 AddVirtualChannel 换成 AddTcpClient("enip-link", "192.168.1.10", 44818) 即可。
var memory = new EtherNetIpSlaveMemory();
memory.SetTag("Temperature", EtherNetIpDataType.Int, (short)253);
memory.SetTag("Running", EtherNetIpDataType.Bool, true);

await using var app = ZeusHost.Create(builder =>
{
    builder.AddAcquisition(TimeSpan.FromMilliseconds(100));
    builder.AddVirtualChannel("enip-link", new EtherNetIpSlaveResponder(memory));
    builder.AddEtherNetIp("plc", "enip-link", points: map => map
        .Int("temperature", "Temperature", scale: 0.1).Writable("temperature")
        .Bool("running", "Running").Writable("running"));
});

await app.StartAsync();

var plc = app.Devices.Get<EtherNetIpDevice>("plc");
var temperature = await WaitForPointAsync<double>(app, "temperature");
var running = await WaitForPointAsync<bool>(app, "running");
Console.WriteLine($"EtherNet/IP 点表：temperature = {temperature:F1}, running = {running}");

await app.Points.WriteAsync("temperature", 18.6);
await app.Points.WriteAsync("running", false);
Console.WriteLine($"点名写回后：Temperature = {memory.GetTag("Temperature").Value}, Running = {memory.GetTag("Running").Value}");

await plc.WriteTagAsync("Speed", EtherNetIpDataType.DInt, 1450);
var speed = await plc.ReadTagAsync("Speed", EtherNetIpDataType.DInt);
Console.WriteLine($"直接协议读取：Speed = {speed}");

await app.StopAsync();

static async Task<T> WaitForPointAsync<T>(IZeusHost app, string name)
{
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
    while (DateTime.UtcNow < deadline)
    {
        if (app.Points.TryGet<T>(name, out var value) && value is not null)
        {
            return value;
        }

        await Task.Delay(20);
    }

    throw new TimeoutException($"等待点 {name} 超时。");
}
