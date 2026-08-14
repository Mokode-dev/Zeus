<p align="center">
  <img src="Zeus.png" alt="Zeus" width="160" />
</p>

<h1 align="center">Zeus</h1>

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

---

## ✨ 特性

- 🔌 **通道** — 串口、TCP、UDP、虚拟通道，同一套 API
- 📡 **协议** — 自定义帧、Modbus RTU / TCP，可挂虚拟从站
- 📊 **采集** — 声明点表后按间隔自动轮询，保留最近成功采样，并计算报警限状态
- 🧭 **追踪** — 通道级 TX/RX 报文事件、滚动内存记录器与文件日志器
- 🖥️ **界面无关** — 业务代码不绑死 WinForms 或 WPF
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

| 程序集 | 用途 |
| --- | --- |
| `Zeus.Hosting` | 宿主与采集循环 |
| `Zeus.Communications` | 串口 / TCP / UDP / 虚拟通道 |
| `Zeus.Protocols.Framing` | 自定义帧 |
| `Zeus.Protocols.Modbus` | Modbus RTU / TCP |
| `Zeus.Configuration` | JSON 工程配置 |
| `Zeus.Presentation.WinForms` | WinForms 绑定 |
| `Zeus.Presentation.Wpf` | WPF 绑定 |

当前版本为 `0.1.0-preview`。

## 📄 许可证

[MIT](LICENSE)
