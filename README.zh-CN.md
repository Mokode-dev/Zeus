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
- 📡 **协议** — 自定义帧、Modbus RTU / TCP / ASCII、Mitsubishi MC 1E/3E/4E Binary/ASCII、Siemens S7 TCP、Omron FINS UDP/TCP、Omron Host Link ASCII、Panasonic MEWTOCOL-COM、Allen-Bradley EtherNet/IP CIP、DL/T 645-2007，可挂虚拟从站/PLC/表计
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

| 包 | 版本 | 下载量 | 用途 |
| --- | --- | --- | --- |
| `Zeus.Abstractions` | [![nuget](https://img.shields.io/nuget/v/Zeus.Abstractions.svg)](https://www.nuget.org/packages/Zeus.Abstractions) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Abstractions.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Abstractions) | 通道、设备、点表与宿主契约 |
| `Zeus.Runtime` | [![nuget](https://img.shields.io/nuget/v/Zeus.Runtime.svg)](https://www.nuget.org/packages/Zeus.Runtime) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Runtime.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Runtime) | 运行时内核：通道状态机、设备基类与点表 |
| `Zeus.Hosting` | [![nuget](https://img.shields.io/nuget/v/Zeus.Hosting.svg)](https://www.nuget.org/packages/Zeus.Hosting) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Hosting.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Hosting) | 宿主与采集循环 |
| `Zeus.Communications` | [![nuget](https://img.shields.io/nuget/v/Zeus.Communications.svg)](https://www.nuget.org/packages/Zeus.Communications) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Communications.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Communications) | 串口 / TCP / UDP 客户端与服务端 / 虚拟通道 |
| `Zeus.Protocols.Framing` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.Framing.svg)](https://www.nuget.org/packages/Zeus.Protocols.Framing) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.Framing.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.Framing) | 自定义帧 |
| `Zeus.Protocols.Modbus` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.Modbus.svg)](https://www.nuget.org/packages/Zeus.Protocols.Modbus) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.Modbus.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.Modbus) | Modbus RTU / TCP / ASCII |
| `Zeus.Protocols.Mc` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.Mc.svg)](https://www.nuget.org/packages/Zeus.Protocols.Mc) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.Mc.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.Mc) | Mitsubishi MC 1E/3E/4E Binary/ASCII，3E/4E 随机读写 |
| `Zeus.Protocols.S7` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.S7.svg)](https://www.nuget.org/packages/Zeus.Protocols.S7) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.S7.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.S7) | Siemens S7 TCP，读写 DB/I/Q/M 区常用类型 |
| `Zeus.Protocols.Fins` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.Fins.svg)](https://www.nuget.org/packages/Zeus.Protocols.Fins) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.Fins.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.Fins) | Omron FINS UDP/TCP，读写 CIO/WR/HR/AR/DM/EM/TIM-CNT 区 |
| `Zeus.Protocols.HostLink` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.HostLink.svg)](https://www.nuget.org/packages/Zeus.Protocols.HostLink) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.HostLink.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.HostLink) | Omron Host Link ASCII，读写 CIO/LR/HR/AR/DM 区 |
| `Zeus.Protocols.Mewtocol` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.Mewtocol.svg)](https://www.nuget.org/packages/Zeus.Protocols.Mewtocol) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.Mewtocol.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.Mewtocol) | Panasonic MEWTOCOL-COM，读写 DT/LD/FL 数据寄存器和 X/Y/R/L 接点字 |
| `Zeus.Protocols.EtherNetIp` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.EtherNetIp.svg)](https://www.nuget.org/packages/Zeus.Protocols.EtherNetIp) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.EtherNetIp.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.EtherNetIp) | Allen-Bradley EtherNet/IP CIP，读写标量标签与 CIP 属性 |
| `Zeus.Protocols.Dlt645` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.Dlt645.svg)](https://www.nuget.org/packages/Zeus.Protocols.Dlt645) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.Dlt645.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.Dlt645) | DL/T 645-2007 电能表，读写 BCD 数据项与原始数据项 |
| `Zeus.Configuration` | [![nuget](https://img.shields.io/nuget/v/Zeus.Configuration.svg)](https://www.nuget.org/packages/Zeus.Configuration) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Configuration.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Configuration) | JSON 工程配置 |
| `Zeus.Presentation.Abstractions` | [![nuget](https://img.shields.io/nuget/v/Zeus.Presentation.Abstractions.svg)](https://www.nuget.org/packages/Zeus.Presentation.Abstractions) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Presentation.Abstractions.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Presentation.Abstractions) | UI 无关绑定抽象、`PointBindingSource` 与点表快照绑定 |
| `Zeus.Presentation.WinForms` | [![nuget](https://img.shields.io/nuget/v/Zeus.Presentation.WinForms.svg)](https://www.nuget.org/packages/Zeus.Presentation.WinForms) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Presentation.WinForms.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Presentation.WinForms) | WinForms 绑定、点表报警色与写回按钮绑定 |
| `Zeus.Presentation.Wpf` | [![nuget](https://img.shields.io/nuget/v/Zeus.Presentation.Wpf.svg)](https://www.nuget.org/packages/Zeus.Presentation.Wpf) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Presentation.Wpf.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Presentation.Wpf) | WPF 绑定、点表报警色与写回按钮绑定 |

加入 QQ 群 `771421105` 可与其它使用者交流，二维码见手册 [社区](https://docs.greekmythology.cn/docs/community)。

## 支持 Zeus

如果 Zeus 对你的项目有帮助，可以通过手册里的 [支持 Zeus](https://docs.greekmythology.cn/docs/sponsor/) 页面或 [爱发电](https://afdian.com/a/zeusnet) 赞助项目，也可以沟通企业支持。普通赞助用于支持开源维护；协议适配、现场联调和项目集成请单独沟通。

## 📄 许可证

[MIT](LICENSE)
