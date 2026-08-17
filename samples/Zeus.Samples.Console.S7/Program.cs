using Zeus;

// 无硬件演示：虚拟 Siemens S7 PLC 应答 S7 TCP Read/Write Var。
// 现场联调时，把 AddVirtualChannel 换成 AddTcpClient("plc-link", "192.168.1.10", 102) 即可。
var memory = new S7SlaveMemory();
var db1 = memory.GetDataBlock(1);
WriteSingle(db1.AsSpan(0, 4), 25.5f);
WriteInt16(db1.AsSpan(4, 2), 250);
memory.Markers[10] = 0x01;

await using var app = ZeusHost.Create(builder =>
{
    builder.AddAcquisition(TimeSpan.FromMilliseconds(100));
    builder.AddVirtualChannel("plc-link", new S7SlaveResponder(memory));
    builder.AddSiemensS7("plc", "plc-link", points: map => map
        .DbReal("temperature", dbNumber: 1, byteOffset: 0)
        .DbInt("setpoint", dbNumber: 1, byteOffset: 4, scale: 0.1).Writable("setpoint")
        .MarkerBool("running", byteOffset: 10, bitOffset: 0).Writable("running"));
});

await app.StartAsync();

var plc = app.Devices.Get<S7Device>("plc");
var temperature = await WaitForPointAsync<float>(app, "temperature");
var setpoint = await WaitForPointAsync<double>(app, "setpoint");
var running = await WaitForPointAsync<bool>(app, "running");

Console.WriteLine($"S7 点表：temperature = {temperature:F1}, setpoint = {setpoint:F1}, running = {running}");

await app.Points.WriteAsync("setpoint", 18.6);
await app.Points.WriteAsync("running", false);
Console.WriteLine($"点名写回后：DB1.DBW4 = {ReadInt16(db1.AsSpan(4, 2))}, M10.0 = {await plc.ReadBoolAsync(S7Area.Merkers, 10, 0)}");

await plc.WriteRealAsync(S7Area.DataBlock, byteOffset: 8, value: 12.75f, dbNumber: 1);
await plc.WriteDIntAsync(S7Area.DataBlock, byteOffset: 12, value: -123456, dbNumber: 1);
Console.WriteLine($"直接协议读取：DBD8 = {await plc.ReadRealAsync(S7Area.DataBlock, 8, dbNumber: 1):F2}, DBD12 = {await plc.ReadDIntAsync(S7Area.DataBlock, 12, dbNumber: 1)}");

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

static void WriteInt16(Span<byte> destination, short value)
{
    destination[0] = (byte)((value >> 8) & 0xFF);
    destination[1] = (byte)(value & 0xFF);
}

static short ReadInt16(ReadOnlySpan<byte> source)
    => unchecked((short)((source[0] << 8) | source[1]));

static void WriteSingle(Span<byte> destination, float value)
{
    var raw = BitConverter.SingleToInt32Bits(value);
    destination[0] = (byte)((raw >> 24) & 0xFF);
    destination[1] = (byte)((raw >> 16) & 0xFF);
    destination[2] = (byte)((raw >> 8) & 0xFF);
    destination[3] = (byte)(raw & 0xFF);
}
