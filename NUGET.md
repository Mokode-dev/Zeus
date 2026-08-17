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

| 包 | 版本 | 用途 |
| --- | --- | --- |
| `Zeus.Abstractions` | [![nuget](https://img.shields.io/nuget/v/Zeus.Abstractions.svg)](https://www.nuget.org/packages/Zeus.Abstractions) | 通道、设备、点表与宿主契约 |
| `Zeus.Runtime` | [![nuget](https://img.shields.io/nuget/v/Zeus.Runtime.svg)](https://www.nuget.org/packages/Zeus.Runtime) | 运行时内核：通道状态机、设备基类与点表 |
| `Zeus.Hosting` | [![nuget](https://img.shields.io/nuget/v/Zeus.Hosting.svg)](https://www.nuget.org/packages/Zeus.Hosting) | 宿主与采集循环 |
| `Zeus.Communications` | [![nuget](https://img.shields.io/nuget/v/Zeus.Communications.svg)](https://www.nuget.org/packages/Zeus.Communications) | 串口 / TCP / UDP 客户端与服务端 / 虚拟通道 |
| `Zeus.Protocols.Framing` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.Framing.svg)](https://www.nuget.org/packages/Zeus.Protocols.Framing) | 自定义帧 |
| `Zeus.Protocols.Modbus` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.Modbus.svg)](https://www.nuget.org/packages/Zeus.Protocols.Modbus) | Modbus RTU/TCP |
| `Zeus.Protocols.Mc` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.Mc.svg)](https://www.nuget.org/packages/Zeus.Protocols.Mc) | Mitsubishi MC 1E/3E/4E Binary/ASCII，3E/4E 随机读写 |
| `Zeus.Protocols.S7` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.S7.svg)](https://www.nuget.org/packages/Zeus.Protocols.S7) | Siemens S7 TCP，读写 DB/I/Q/M 区常用类型 |
| `Zeus.Configuration` | [![nuget](https://img.shields.io/nuget/v/Zeus.Configuration.svg)](https://www.nuget.org/packages/Zeus.Configuration) | JSON 工程配置 |
| `Zeus.Presentation.Abstractions` | [![nuget](https://img.shields.io/nuget/v/Zeus.Presentation.Abstractions.svg)](https://www.nuget.org/packages/Zeus.Presentation.Abstractions) | UI 无关绑定抽象、`PointBindingSource` 与点表快照绑定 |
| `Zeus.Presentation.WinForms` | [![nuget](https://img.shields.io/nuget/v/Zeus.Presentation.WinForms.svg)](https://www.nuget.org/packages/Zeus.Presentation.WinForms) | WinForms 绑定、点表报警色与写回按钮绑定 |
| `Zeus.Presentation.Wpf` | [![nuget](https://img.shields.io/nuget/v/Zeus.Presentation.Wpf.svg)](https://www.nuget.org/packages/Zeus.Presentation.Wpf) | WPF 绑定、点表报警色与写回按钮绑定 |

点表支持最近成功采样历史、报警限，以及按点名写回可写点，适合在界面上直接显示当前值、采集错误和高低报，并下发设定值。

0.6 起支持点表 `PointBindingSource`、`PointHistoryBindingSource`、快照绑定、历史绑定、报警色绑定和写回按钮绑定；0.5 起支持 TCP 服务端、Mitsubishi MC、Siemens S7 TCP 与虚拟 PLC；0.4 起支持 Modbus 功能码 17、UDP 服务端、`ChannelTraceLogger` 与 `BindEnabled`；0.3 起可写点走 `Points.WriteAsync`；0.2 起宿主停止后可再次启动，通道故障默认自动重连，运行中可增删通道与设备，JSON 监视会同步拓扑。

0.2 起宿主停止后可再次启动；通道故障默认自动重连；运行中可增删通道与设备；JSON 监视会同步拓扑。

通道支持 `PacketTraced` 报文追踪事件与 `ChannelTraceBuffer` 滚动内存记录器，可直接接入通信诊断窗口或现场故障快照。

手册：[docs.greekmythology.cn](https://docs.greekmythology.cn)　·　QQ 群：`771421105`
