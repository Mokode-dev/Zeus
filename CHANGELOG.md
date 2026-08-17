# 更新记录

## 0.5.0

补齐 TCP 服务端通道、Mitsubishi MC 协议与 Modbus 诊断能力，同时完善 JSON 配置声明。

### 包含

- 通信：新增 `TcpServerChannel`、`TcpServerOptions`、`AddTcpServer` 与 `AddTcpServerAsync`，支持多客户端接收、最近客户端回复与广播
- Modbus：新增功能码 07、08、11，支持异常状态、诊断回显和服务器 ID 读取
- 三菱 MC：新增 `Zeus.Protocols.Mc`，支持 MC 1E/3E/4E Binary/ASCII、X/Y/M/D/W/R/ZR 常用软元件、3E/4E 随机读写和虚拟 PLC 联调
- 配置：JSON 支持声明 Mitsubishi MC 设备、MC 虚拟 PLC、帧型、编码、3E/4E 路由字段，以及 MC 点表采集和按点名写回

### 兼容承诺

- 只新增公开 API，不删除或改变 0.4 已发布的类型、成员和扩展方法签名
- `0.5.x` 补丁只修缺陷；破坏性变更进入后续次版本

## 0.4.0

补齐通信诊断、UDP 监听和常用界面启停绑定，同时扩展 Modbus 保持寄存器读写事务。

### 包含

- Modbus：新增功能码 17 / `ReadWriteMultipleRegistersAsync`，一次事务内先写多个保持寄存器，再读回保持寄存器
- 通信：新增 `UdpServerChannel`、`UdpServerOptions`、`AddUdpServer` 与 `AddUdpServerAsync`，可监听本地 UDP 端口并回复最近发送方
- 配置：JSON 通道新增 `type: "udp-server"`，支持 `localAddress`、`localPort`
- 追踪：新增 `ChannelTraceLogger`，把 TX/RX 原始报文写入 `ILogger` 结构化日志
- 界面：WinForms / WPF 新增 `BindEnabled`，按通道状态自动控制控件启用状态

### 兼容承诺

- 只新增公开 API，不删除或改变 0.3 已发布的类型、成员和扩展方法签名
- `0.4.x` 补丁只修缺陷；破坏性变更进入后续次版本

## 0.3.0

点表从只读公告栏变成读写口。界面和业务可以按点名下发设定值，不必再拿设备拼寄存器地址。

### 包含

- `IPointTable.WriteAsync`：按短名或 `设备.点` 把工程值写回所属设备
- `PointDefinition.Writable`：声明该点是否允许写回；默认只读
- `IPointWriter`：自定义设备实现后即可接入同一条写回路径
- Modbus：`HoldingRegister(name, address, scale)` 保留线性系数以便反算；`.Writable("setpoint")` 把保持寄存器或线圈标为可写
- 写成功后立刻更新点表快照；写失败把原因记到该点的 `Error` 并重新抛出
- JSON 点字段增加 `writable`。`input` / `discrete` 不能标为可写

### 兼容承诺

- 只新增公开 API，不删除或改变 0.2 已发布的类型、成员和扩展方法签名
- `0.3.x` 补丁只修缺陷；破坏性变更进入后续次版本

## 0.2.0

解开 `0.1` 的生命周期限制。破坏性变更已列在下方；从 `0.1.x` 升级时请对照公开 API 基线。

### 包含

- 宿主停止后再 `StartAsync` 会重新打开通道并恢复采集，不必重建宿主
- 通道从 `Closed` / `Faulted` 再次 `OpenAsync` 会先清理残留传输再打开
- 默认开启故障自动重连（1 秒起、指数退避、上限 30 秒），可用 `AddReconnect` 或 JSON `reconnect` 关闭或调整
- 运行中可通过 `Add*Async` / `AddModbus*` / `RemoveChannelAsync` / `RemoveDeviceAsync` 增删通道与设备
- JSON 监视与 `ReloadAsync` 按差异同步采集间隔、重连选项以及通道/设备拓扑
- 点表支持按点或按设备卸载

### 破坏性变更

- `IDevice` 新增 `Channel`。自定义设备必须暴露所绑定的通道，以便卸载通道时级联移除设备
- `IChannelRegistry` / `IDeviceRegistry` 新增 `Add`、`RemoveAsync`、`Changed`；设备目录新增 `TryGet`
- `IPointTableWriter` 新增 `Unregister` / `UnregisterDevice`
- `IZeusHost` 新增 `IsRunning`。`StopAsync` 不再关闭底层 Generic Host，只暂停采集并关闭通道
- JSON 热更新不再忽略拓扑变更：保存文件可能增删运行中的通道与设备

### 兼容承诺

- `0.2.x` 补丁只修缺陷，不删除、重命名或改变已发布的公开类型、成员和扩展方法签名
- 允许新增公开 API
- 破坏性变更将进入后续次版本，并在发布说明中列出

## 0.1.0

首个正式切片。`0.1.x` 只修缺陷，不改已发布的公开签名；破坏性变更和新能力进入 `0.2`。

### 包含

- 宿主生命周期：`ZeusHost.Create`、`StartAsync` / `StopAsync`
- 通道：串口、TCP/UDP 客户端、虚拟通道、TX/RX 报文追踪、滚动内存记录器与文件日志器
- 协议：自定义帧、按匹配器等待应答、Modbus RTU/TCP（功能码 01–06、0F、10、16）
- 设备与点表：登记设备、周期采集、连续地址合并读取、成功采样历史缓冲、点表报警限
- 配置：JSON 装载通道与设备；采集间隔可在运行中更新
- 界面：WinForms / WPF 的 `AttachZeus`、`BindTo`、`AsBindingSource`
- 分发包：`0.1.0` NuGet 正式包

### 已知限制

这些行为按设计保留，不视为 `0.1.0` 回归：

- 通道进入 `Faulted` 后，框架不会自动重连。可对同一实例再次调用 `OpenAsync`，或重建宿主。
- JSON 文件监视默认只热更新采集间隔。改 COM 口、地址、增删通道或设备需要重启进程。
- 宿主停止后再 `StartAsync` 不可靠。需要重新运行时，请再创建一次宿主。
- 不能在运行中增删通道或设备。

### 兼容承诺

- `0.1.x` 补丁只修缺陷，不删除、重命名或改变已发布的公开类型、成员和扩展方法签名。
- 允许新增公开 API。
- 允许收紧从未文档化的内部行为，只要公开契约不变。
- 破坏性变更将进入 `0.2.0`，并在发布说明中列出。
