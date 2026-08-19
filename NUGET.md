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
| [Zeus.Abstractions](https://www.nuget.org/packages/Zeus.Abstractions) | 通道、设备、点表与宿主契约 |
| [Zeus.Runtime](https://www.nuget.org/packages/Zeus.Runtime) | 运行时内核 |
| [Zeus.Hosting](https://www.nuget.org/packages/Zeus.Hosting) | 宿主与采集循环 |
| [Zeus.Communications](https://www.nuget.org/packages/Zeus.Communications) | 串口 / TCP / UDP / 虚拟通道 |
| [Zeus.Protocols.Framing](https://www.nuget.org/packages/Zeus.Protocols.Framing) | 自定义帧 |
| [Zeus.Protocols.Modbus](https://www.nuget.org/packages/Zeus.Protocols.Modbus) | Modbus RTU / TCP / ASCII |
| [Zeus.Protocols.Mc](https://www.nuget.org/packages/Zeus.Protocols.Mc) | Mitsubishi MC |
| [Zeus.Protocols.S7](https://www.nuget.org/packages/Zeus.Protocols.S7) | Siemens S7 |
| [Zeus.Protocols.Fins](https://www.nuget.org/packages/Zeus.Protocols.Fins) | Omron FINS |
| [Zeus.Protocols.HostLink](https://www.nuget.org/packages/Zeus.Protocols.HostLink) | Omron Host Link |
| [Zeus.Protocols.Mewtocol](https://www.nuget.org/packages/Zeus.Protocols.Mewtocol) | Panasonic MEWTOCOL |
| [Zeus.Protocols.EtherNetIp](https://www.nuget.org/packages/Zeus.Protocols.EtherNetIp) | Allen-Bradley EtherNet/IP |
| [Zeus.Protocols.Dlt645](https://www.nuget.org/packages/Zeus.Protocols.Dlt645) | DL/T 645-2007 |
| [Zeus.Protocols.Iec104](https://www.nuget.org/packages/Zeus.Protocols.Iec104) | IEC 60870-5-104 |
| [Zeus.Protocols.Mqtt](https://www.nuget.org/packages/Zeus.Protocols.Mqtt) | MQTT 3.1.1 |
| [Zeus.Protocols.Snmp](https://www.nuget.org/packages/Zeus.Protocols.Snmp) | SNMP v2c |
| [Zeus.Configuration](https://www.nuget.org/packages/Zeus.Configuration) | JSON 工程配置 |
| [Zeus.Presentation.Abstractions](https://www.nuget.org/packages/Zeus.Presentation.Abstractions) | UI 无关绑定 |
| [Zeus.Presentation.WinForms](https://www.nuget.org/packages/Zeus.Presentation.WinForms) | WinForms 绑定 |
| [Zeus.Presentation.Wpf](https://www.nuget.org/packages/Zeus.Presentation.Wpf) | WPF 绑定 |

手册：[docs.greekmythology.cn](https://docs.greekmythology.cn)　·　QQ 群：`771421105`
