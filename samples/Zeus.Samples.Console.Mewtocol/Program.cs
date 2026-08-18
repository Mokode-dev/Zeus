using Zeus;

// 无硬件演示：虚拟 Panasonic MEWTOCOL-COM PLC 应答 ASCII MEWTOCOL 帧。
// 现场串口联调时，把 AddVirtualChannel 换成 AddSerialPort("mewtocol", "COM3", 9600) 即可。
// 现场 TCP 透传网关联调时，也可以换成 AddTcpClient("mewtocol", "192.168.250.1", 9094)。
var memory = new MewtocolSlaveMemory();
memory.DataRegisterWords[100] = 253;
memory.InternalRelayWords[10] = 0x0001;

await using var app = ZeusHost.Create(builder =>
{
    builder.AddAcquisition(TimeSpan.FromMilliseconds(100));
    builder.AddVirtualChannel("mewtocol", new MewtocolSlaveResponder(stationNumber: 1, memory));
    builder.AddPanasonicMewtocol("plc", "mewtocol", new MewtocolOptions
    {
        StationNumber = 1
    }, points: map => map
        .DtWord("temperature", address: 100, scale: 0.1).Writable("temperature")
        .RBit("running", wordAddress: 10, bitOffset: 0).Writable("running"));
});

await app.StartAsync();

var plc = app.Devices.Get<MewtocolDevice>("plc");
var temperature = await WaitForPointAsync<double>(app, "temperature");
var running = await WaitForPointAsync<bool>(app, "running");
Console.WriteLine($"MEWTOCOL 点表：temperature = {temperature:F1}, running = {running}");

await app.Points.WriteAsync("temperature", 18.6);
await app.Points.WriteAsync("running", false);
Console.WriteLine($"点名写回后：DT100 = {memory.DataRegisterWords[100]}, R10.0 = {(memory.InternalRelayWords[10] & 0x0001) != 0}");

await plc.WriteDataRegistersAsync(120, [10, 20, 30]);
var words = await plc.ReadDataRegistersAsync(120, 3);
Console.WriteLine($"直接协议读取：DT120..DT122 = {string.Join(", ", words)}");

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
