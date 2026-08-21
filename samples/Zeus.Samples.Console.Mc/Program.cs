using Zeus;

// 无硬件演示：虚拟 Mitsubishi MC PLC 应答 3E Binary 请求。
// 现场联调时，把 AddVirtualChannel 换成 AddTcpClient("plc-bus", "192.168.1.10", 5000) 即可。
var memory = new McSlaveMemory();
memory.DataRegisters[100] = 253;
memory.InternalRelays[10] = true;
memory.InputRelays[0x10] = true;
memory.LinkRegisters[20] = 777;

await using var app = ZeusHost.Create(builder =>
{
    builder.AddAcquisition(TimeSpan.FromMilliseconds(100));
    builder.AddVirtualChannel("plc-bus", new McSlaveResponder(memory));
    builder.AddMitsubishiMc("plc", "plc-bus", points: map =>
    {
        map.DataRegister("temperature", 100, 0.1, new PointAlarmLimits(low: 5, high: 80))
            .Writable("temperature");
        map.InternalRelay("run", 10).Writable("run");
        map.InputRelay("ready", 0x10);
    });
});

await app.StartAsync();

var plc = app.Devices.Get<McDevice>("plc");
var temperature = await WaitForPointAsync<double>(app, "temperature");
var run = await WaitForPointAsync<bool>(app, "run");
var ready = await WaitForPointAsync<bool>(app, "ready");

Console.WriteLine($"MC 点表：temperature = {temperature:F1}, run = {run}, ready = {ready}");

await app.Points.WriteAsync("temperature", 42.5);
await app.Points.WriteAsync("run", false);
Console.WriteLine($"点名写回后：D100 = {memory.DataRegisters[100]}, M10 = {memory.InternalRelays[10]}");

var linkRegisters = await plc.ReadLinkRegistersAsync(20, 1);
Console.WriteLine($"直接协议读取：W20 = {linkRegisters[0]}");

await plc.WriteRandomWordsAsync(
    [new McWordWrite(McDeviceCode.DataRegister, 110, 1234)],
    [new McDoubleWordWrite(McDeviceCode.FileRegister, 30, 0x11223344)]);
Console.WriteLine($"随机写入后：D110 = {memory.DataRegisters[110]}, R30/R31 = 0x{memory.FileRegisters[31]:X4}{memory.FileRegisters[30]:X4}");

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
