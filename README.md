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
  <strong>From Olympus, orchestrating every device.</strong>
</p>

<p align="center">
  A .NET framework for industrial host applications.<br />
  Communications, protocols, acquisition, and lifecycle management are handled by the framework, while WinForms, WPF, or console apps can provide the UI.
</p>

<p align="center">
  <em>Leave the complexity to Zeus, and keep the user experience simple.</em>
</p>

<p align="center">
  <strong>English</strong> | <a href="README.zh-CN.md">简体中文</a>
</p>

---

## ✨ Features

- 🔌 **Channels** — Serial, TCP, UDP client/server, and virtual channels through one consistent API
- 📡 **Protocols** — Custom frames, Modbus RTU/TCP/ASCII, Mitsubishi MC 1E/3E/4E Binary/ASCII, Siemens S7 TCP, Omron FINS UDP/TCP, Omron Host Link ASCII, Panasonic MEWTOCOL-COM, and Allen-Bradley EtherNet/IP CIP, with virtual slave/PLC support
- 📊 **Acquisition** — Define a point table once, poll on intervals, keep the latest successful sample, calculate alarm limits, and write writable points back by name
- 🧭 **Tracing** — Channel-level TX/RX packet events, rolling in-memory records, file logging, and structured `ILogger` logs
- 🖥️ **UI agnostic** — Business code is not tied to WinForms or WPF; point tables can bind directly to text, historical trends, alarm colors, enabled states, and write buttons
- 🧾 **JSON configuration** — Change ports and slave addresses in the field without recompiling
- 🧪 **Hardware optional at first** — Virtual channels use the same programming model as real devices

## 🚀 Quick Start

```csharp
await using var app = ZeusHost.Create(builder =>
{
    builder.AddVirtualChannel("meter");
    // With hardware: builder.AddSerialPort("meter", "COM3", 115200);
});

await app.StartAsync();
var meter = app.Channels.Get("meter");
meter.DataReceived += (_, e) => { /* hand data to the UI */ };
await meter.WriteAsync("PING"u8.ToArray());
```

Desktop apps only need to swap the binding layer. The channel declaration stays the same:

```csharp
this.AttachZeus(builder => builder.AddVirtualChannel("meter"));
meter.BindTo(echoLabel);
app.Points.BindTo("temperature", temperatureLabel);
```

## 📦 Assemblies

| Package | Version | Downloads | Purpose |
| --- | --- | --- | --- |
| `Zeus.Abstractions` | [![nuget](https://img.shields.io/nuget/v/Zeus.Abstractions.svg)](https://www.nuget.org/packages/Zeus.Abstractions) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Abstractions.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Abstractions) | Contracts for channels, devices, point tables, and hosting |
| `Zeus.Runtime` | [![nuget](https://img.shields.io/nuget/v/Zeus.Runtime.svg)](https://www.nuget.org/packages/Zeus.Runtime) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Runtime.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Runtime) | Runtime core: channel state machines, device base classes, and point tables |
| `Zeus.Hosting` | [![nuget](https://img.shields.io/nuget/v/Zeus.Hosting.svg)](https://www.nuget.org/packages/Zeus.Hosting) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Hosting.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Hosting) | Host integration and acquisition loop |
| `Zeus.Communications` | [![nuget](https://img.shields.io/nuget/v/Zeus.Communications.svg)](https://www.nuget.org/packages/Zeus.Communications) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Communications.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Communications) | Serial / TCP / UDP clients and servers / virtual channels |
| `Zeus.Protocols.Framing` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.Framing.svg)](https://www.nuget.org/packages/Zeus.Protocols.Framing) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.Framing.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.Framing) | Custom frame support |
| `Zeus.Protocols.Modbus` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.Modbus.svg)](https://www.nuget.org/packages/Zeus.Protocols.Modbus) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.Modbus.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.Modbus) | Modbus RTU / TCP / ASCII |
| `Zeus.Protocols.Mc` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.Mc.svg)](https://www.nuget.org/packages/Zeus.Protocols.Mc) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.Mc.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.Mc) | Mitsubishi MC 1E/3E/4E Binary/ASCII, including 3E/4E random read/write |
| `Zeus.Protocols.S7` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.S7.svg)](https://www.nuget.org/packages/Zeus.Protocols.S7) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.S7.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.S7) | Siemens S7 TCP, reading/writing common DB/I/Q/M area types |
| `Zeus.Protocols.Fins` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.Fins.svg)](https://www.nuget.org/packages/Zeus.Protocols.Fins) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.Fins.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.Fins) | Omron FINS UDP/TCP, reading/writing CIO/WR/HR/AR/DM/EM/TIM-CNT areas |
| `Zeus.Protocols.HostLink` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.HostLink.svg)](https://www.nuget.org/packages/Zeus.Protocols.HostLink) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.HostLink.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.HostLink) | Omron Host Link ASCII, reading/writing CIO/LR/HR/AR/DM areas |
| `Zeus.Protocols.Mewtocol` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.Mewtocol.svg)](https://www.nuget.org/packages/Zeus.Protocols.Mewtocol) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.Mewtocol.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.Mewtocol) | Panasonic MEWTOCOL-COM, reading/writing DT/LD/FL data registers and X/Y/R/L contact words |
| `Zeus.Protocols.EtherNetIp` | [![nuget](https://img.shields.io/nuget/v/Zeus.Protocols.EtherNetIp.svg)](https://www.nuget.org/packages/Zeus.Protocols.EtherNetIp) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Protocols.EtherNetIp.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Protocols.EtherNetIp) | Allen-Bradley EtherNet/IP CIP, reading/writing scalar tags and CIP attributes |
| `Zeus.Configuration` | [![nuget](https://img.shields.io/nuget/v/Zeus.Configuration.svg)](https://www.nuget.org/packages/Zeus.Configuration) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Configuration.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Configuration) | JSON project configuration |
| `Zeus.Presentation.Abstractions` | [![nuget](https://img.shields.io/nuget/v/Zeus.Presentation.Abstractions.svg)](https://www.nuget.org/packages/Zeus.Presentation.Abstractions) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Presentation.Abstractions.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Presentation.Abstractions) | UI-independent binding abstractions, `PointBindingSource`, and point snapshot binding |
| `Zeus.Presentation.WinForms` | [![nuget](https://img.shields.io/nuget/v/Zeus.Presentation.WinForms.svg)](https://www.nuget.org/packages/Zeus.Presentation.WinForms) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Presentation.WinForms.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Presentation.WinForms) | WinForms binding, point alarm colors, and write button binding |
| `Zeus.Presentation.Wpf` | [![nuget](https://img.shields.io/nuget/v/Zeus.Presentation.Wpf.svg)](https://www.nuget.org/packages/Zeus.Presentation.Wpf) | [![downloads](https://img.shields.io/nuget/dt/Zeus.Presentation.Wpf.svg?label=downloads)](https://www.nuget.org/packages/Zeus.Presentation.Wpf) | WPF binding, point alarm colors, and write button binding |

Join QQ group `771421105` to talk with other users. The QR code is available on the documentation [Community](https://docs.greekmythology.cn/docs/community) page.

## Support Zeus

If Zeus helps your project, you can sponsor it through the documentation [Support Zeus](https://docs.greekmythology.cn/docs/sponsor/) page or [Afdian](https://afdian.com/a/zeusnet). Enterprise support is also available by direct discussion. Regular sponsorship supports open-source maintenance; protocol adapters, on-site commissioning, and project integration should be discussed separately.

## 📄 License

[MIT](LICENSE)
