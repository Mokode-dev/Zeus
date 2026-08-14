using Zeus;

// 现场工程师改 zeus.json 即可换端口、从站地址与采集间隔，不必重新编译。
// 保存后采集间隔、重连选项以及通道/设备拓扑都会热更新。
await using var app = ZeusHost.Create(builder =>
{
    builder.AddJsonFile("zeus.json");
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
Console.WriteLine($"配置装载成功。temperature = {temperature}");
Console.WriteLine("修改 zeus.json 并保存：采集间隔、重连选项以及通道/设备拓扑都会热更新。");
await app.StopAsync();
