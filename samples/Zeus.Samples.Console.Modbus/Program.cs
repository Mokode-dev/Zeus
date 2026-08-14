using Zeus;

// 无硬件演示：内存从站应答 RTU 请求。现场只需把虚拟通道换成串口或 TCP。
var memory = new ModbusSlaveMemory();
memory.HoldingRegisters[0] = 185;

await using var app = ZeusHost.Create(builder =>
{
    builder.AddAcquisition(TimeSpan.FromMilliseconds(200));
    builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
    builder.AddModbusRtu("oven", "bus", unitId: 1, points: map =>
    {
        map.HoldingRegister("temperature", 0, 0.1);
        map.HoldingRegister("setpoint", 1, 0.1).Writable("setpoint");
    });
    // 现场串口：builder.AddSerialPort("bus", "COM3", 9600); builder.AddModbusRtu("oven", "bus", points: ...);
});

var ready = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
app.Points.Changed += (_, e) =>
{
    if (e.Current.Definition.Name == "temperature" && e.Current.Value is double value)
    {
        ready.TrySetResult(value);
    }
};

await app.StartAsync();
var temperature = await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
Console.WriteLine($"点表 temperature = {temperature}");

await app.Points.WriteAsync("setpoint", 20.0);
Console.WriteLine($"写入后 setpoint = {app.Points.Get<double>("setpoint")}");

await app.StopAsync();
