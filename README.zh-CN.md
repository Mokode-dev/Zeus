<p align="center">
  <img src="Zeus.png" alt="Zeus" width="160" />
</p>

<h1 align="center">Zeus</h1>

<p align="center">
  <a href="https://www.nuget.org/packages/Zeus.Abstractions"><img src="https://img.shields.io/nuget/dt/Zeus.Abstractions.svg?label=downloads" alt="NuGet Downloads" /></a>
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4" alt=".NET 8.0" />
  <a href="https://github.com/Mokode-dev/Zeus/blob/main/code/LICENSE"><img src="https://img.shields.io/badge/license-MIT-green.svg" alt="License: MIT" /></a>
</p>

<p align="center">
  <strong>居于奥林匹斯，统御每一台设备。</strong>
</p>

<p align="center">
  面向 .NET 的上位机开发框架。<br />
  通信、协议、采集和生命周期由框架处理；WinForms、WPF 和控制台都可以作为界面。
</p>

<p align="center">
  <em>把复杂留给 Zeus，把简单留给用户。</em>
</p>

<p align="center">
  <a href="README.md">English</a> | <strong>简体中文</strong>
</p>

---

## ✨ 特性

- 🔌 **通道** — 串口、TCP、UDP 客户端 / 服务端、虚拟通道，同一套 API
- 📡 **协议** — 自定义帧、Modbus RTU / TCP / ASCII、Mitsubishi MC 1E/3E/4E Binary/ASCII、Siemens S7 TCP、Omron FINS UDP/TCP、Omron Host Link ASCII、Panasonic MEWTOCOL-COM、Allen-Bradley EtherNet/IP CIP、DL/T 645-2007、IEC 60870-5-104、MQTT 3.1.1，可挂虚拟从站/PLC/表计/Broker
- 📊 **采集** — 声明点表后按间隔自动轮询，保留最近成功采样，计算报警限；可写点按名称写回
- 🧭 **追踪** — 通道级 TX/RX 报文事件、滚动内存记录器、文件日志器与 `ILogger` 结构化日志
- 🖥️ **界面无关** — 业务代码不绑死 WinForms 或 WPF；点表可直接绑定文本、历史趋势、报警色、启用状态和写回按钮
- 🧾 **JSON 配置** — 现场改端口和从站地址，不必重新编译
- 🧪 **可先不接硬件** — 虚拟通道与真实设备用法相同

## 🚀 快速开始

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

桌面应用只需换绑定层，通道声明不用改：

```csharp
this.AttachZeus(builder => builder.AddVirtualChannel("meter"));
meter.BindTo(echoLabel);
app.Points.BindTo("temperature", temperatureLabel);
```

## 📦 程序集

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
| [Zeus.Configuration](https://www.nuget.org/packages/Zeus.Configuration) | JSON 工程配置 |
| [Zeus.Presentation.Abstractions](https://www.nuget.org/packages/Zeus.Presentation.Abstractions) | UI 无关绑定 |
| [Zeus.Presentation.WinForms](https://www.nuget.org/packages/Zeus.Presentation.WinForms) | WinForms 绑定 |
| [Zeus.Presentation.Wpf](https://www.nuget.org/packages/Zeus.Presentation.Wpf) | WPF 绑定 |

加入 QQ 群 `771421105` 可与其它使用者交流，二维码见手册 [社区](https://docs.greekmythology.cn/docs/community)。

## 支持 Zeus

如果 Zeus 对你的项目有帮助，可以通过手册里的 [支持 Zeus](https://docs.greekmythology.cn/docs/sponsor/) 页面或 [爱发电](https://afdian.com/a/zeusnet) 赞助项目，也可以沟通企业支持。普通赞助用于支持开源维护；协议适配、现场联调和项目集成请单独沟通。

## 📄 许可证

[MIT](LICENSE)
