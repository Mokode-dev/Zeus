# Zeus

**居于奥林匹斯，统御每一台设备。**

面向 .NET 的上位机开发框架。通信、协议、点表和生命周期由框架处理；WinForms、WPF 和控制台都可以作为界面。

> 把复杂留给 Zeus，把简单留给用户。

```csharp
await using var app = ZeusHost.Create(builder =>
{
    builder.AddVirtualChannel("meter");
    // 有硬件时：builder.AddSerialPort("meter", "COM3", 115200);
});
await app.StartAsync();
```

按功能选择程序集：

| 包 | 用途 |
| --- | --- |
| `Zeus.Hosting` | 宿主与采集循环 |
| `Zeus.Communications` | 串口 / TCP / UDP / 虚拟通道 |
| `Zeus.Protocols.Framing` | 自定义帧 |
| `Zeus.Protocols.Modbus` | Modbus RTU/TCP |
| `Zeus.Configuration` | JSON 工程配置 |
| `Zeus.Presentation.WinForms` | WinForms 绑定 |
| `Zeus.Presentation.Wpf` | WPF 绑定 |

点表支持最近成功采样历史与报警限状态，适合在界面上直接显示当前值、采集错误和高低报。

通道支持 `PacketTraced` 报文追踪事件与 `ChannelTraceBuffer` 滚动内存记录器，可直接接入通信诊断窗口或现场故障快照。

当前版本为 `0.1.0-preview`，安装时请加上 `--prerelease`。
