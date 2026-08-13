# Zeus

**居于奥林匹斯，统御每一台设备。**

Zeus 是面向 .NET 的上位机开发框架。通信、协议、采集和生命周期由框架处理；WinForms、WPF 和控制台都可以作为界面。

> 把复杂留给 Zeus，把简单留给用户。

```csharp
await using var app = ZeusHost.Create(builder =>
{
    builder.AddVirtualChannel("meter");
    // 有硬件时：builder.AddSerialPort("meter", "COM3", 115200);
});

await app.StartAsync();
var meter = app.Channels.Get("meter");
meter.DataReceived += (_, e) => { /* 把数据交给界面 */ };
await meter.WriteAsync("PING"u8.ToArray());
```

桌面应用用适配器绑定控件即可，通道声明不用改：

```csharp
this.AttachZeus(builder => builder.AddVirtualChannel("meter"));
meter.BindTo(echoLabel);
app.Points.BindTo("temperature", temperatureLabel);
```

## 目录

| 路径 | 内容 |
| --- | --- |
| `src/` | 框架程序集 |
| `samples/` | 控制台、WinForms、WPF 示例 |
| `tests/` | 单元测试 |

## 运行示例

```bash
dotnet test Zeus.sln
dotnet run --project samples/Zeus.Samples.Console.Headless
dotnet run --project samples/Zeus.Samples.WinForms.QuickStart
dotnet run --project samples/Zeus.Samples.Wpf.QuickStart
dotnet run --project samples/Zeus.Samples.Console.Modbus
dotnet run --project samples/Zeus.Samples.Console.Config
```

生成 NuGet 预览包（输出到 `artifacts/nuget`）：

```powershell
.\pack.ps1
```

当前版本为 `0.1.0-preview`，安装时请加上 `--prerelease`。
