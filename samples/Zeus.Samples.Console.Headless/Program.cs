using System.Text;
using Zeus;

// 无界面快速上手：声明虚拟通道、启动宿主、写入后观察回显。
// 真实项目把 AddVirtualChannel 换成 AddSerialPort("meter", "COM3") 即可，其余代码不变。
await using var app = ZeusHost.Create(builder =>
{
    builder.AddVirtualChannel("meter");
});

var meter = app.Channels.Get("meter");
meter.DataReceived += (_, e) =>
{
    var text = Encoding.ASCII.GetString(e.Data.Span);
    Console.WriteLine($"收到回显：{text}");
};

await app.StartAsync();
Console.WriteLine($"通道 {meter.Name} 状态：{meter.State}");

await meter.WriteAsync(Encoding.ASCII.GetBytes("PING"));
await Task.Delay(200);

await app.StopAsync();
Console.WriteLine("宿主已停止。把复杂留给 Zeus，把简单留给用户。");
