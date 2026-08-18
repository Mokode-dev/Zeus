using Zeus;

// 无硬件演示：虚拟 Omron Host Link PLC 应答 ASCII Host Link 帧。
// 现场串口联调时，把 AddVirtualChannel 换成 AddSerialPort("host-link", "COM3", 9600) 即可。
// 现场 TCP 透传网关联调时，也可以换成 AddTcpClient("host-link", "192.168.250.1", 9600)。
var memory = new HostLinkSlaveMemory();
memory.DataMemoryWords[100] = 253;
memory.CioWords[10] = 0x0001;

await using var app = ZeusHost.Create(builder =>
{
    builder.AddAcquisition(TimeSpan.FromMilliseconds(100));
    builder.AddVirtualChannel("host-link", new HostLinkSlaveResponder(unitNumber: 0, memory));
    builder.AddOmronHostLink("plc", "host-link", new HostLinkOptions
    {
        UnitNumber = 0
    }, points: map => map
        .DmWord("temperature", address: 100, scale: 0.1).Writable("temperature")
        .CioBit("running", address: 10, bitOffset: 0).Writable("running"));
});

await app.StartAsync();

var plc = app.Devices.Get<HostLinkDevice>("plc");
var temperature = await WaitForPointAsync<double>(app, "temperature");
var running = await WaitForPointAsync<bool>(app, "running");
Console.WriteLine($"Host Link 点表：temperature = {temperature:F1}, running = {running}");

await app.Points.WriteAsync("temperature", 18.6);
await app.Points.WriteAsync("running", false);
Console.WriteLine($"点名写回后：D100 = {memory.DataMemoryWords[100]}, CIO10.0 = {(memory.CioWords[10] & 0x0001) != 0}");

await plc.WriteDataMemoryWordsAsync(120, [10, 20, 30]);
var words = await plc.ReadDataMemoryWordsAsync(120, 3);
Console.WriteLine($"直接协议读取：D120..D122 = {string.Join(", ", words)}");

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
