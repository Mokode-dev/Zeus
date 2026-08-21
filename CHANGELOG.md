# 更新记录

## 0.17.0

补齐宿主底座，并清掉 0.x 为旧版留下的别名与双字段。这是 1.0 前的冻结面：公开 API、JSON 规范值和已文档化行为在 `0.17.x` 只修缺陷，不再改签名。

### 包含

- 构建器：新增 `builder.Logging`、`builder.Configuration`、`builder.Environment`；不再额外叠一份 SimpleConsole，默认沿用 Generic Host 控制台记录器
- 设备注入：`AddDevice` 增加可接收 `IServiceProvider` 的工厂重载；官方协议设备构造函数增加可选 `ILogger<T>`，由 `AddModbusRtu` 等扩展从容器注入
- 事件编号：新增 `ZeusLogEvents`，覆盖通道开关/写入失败、重连、采集失败、点写回失败、配置热更新和报文追踪
- 作用域：通道、采集、重连、写回和配置热更新日志带上 `Channel` / `Device` / `Point` / `Path`
- 报文日志：`builder.AddCommunicationLogging()` 给已有和后续通道自动挂 `ChannelTraceLogger`；热重载时先退订再挂到新实例
- 协议：采集失败与点写回失败写入 `ILogger`，不再只进点表 `Error`
- 生命周期：`StopAsync` 关闭通道失败改为 Warning，不再静默吞掉
- MC：`Mc3EOptions` 正名为 `McOptions`；删除 `AddMitsubishiMc3E`，统一使用 `AddMitsubishiMc`
- JSON：设备类型、虚拟从站 `responder`、点字段只接受手册中的规范值；EtherNet/IP 标签只认 `tag`；MC 点必须写 `deviceCode`，不再从 `table` 回退

### 行为变化

- 默认控制台不再叠加第二份 SimpleConsole；需要单行时间戳格式时请自行 `builder.Logging.AddSimpleConsole(...)`
- 报文追踪写入 `ILogger` 时带 `ZeusLogEvents.PacketTrace`（6001）

### 破坏性变更

从 `0.16.x` 升级时请对照：

- 删除 `Iec104Options.TestFrameInterval`，改用 `T3`
- 删除 `AddMitsubishiMc3E`；`Mc3EOptions` 重命名为 `McOptions`
- 删除点配置的 `tagName`，EtherNet/IP 只用 `tag`
- JSON `type` / `responder` / `table` / `deviceCode` / `area` / `dataType` 不再接受同义别名（例如 `mc`、`modbusrtu`、`holdingregister`、`siemenss7`）
- Mitsubishi MC 点必须提供 `deviceCode`，不能再把软元件写在 `table` 里
- FINS / Host Link / MEWTOCOL 点必须提供 `area`，不能再回退到 `table`

### 兼容承诺

- `0.17.x` 补丁只修缺陷，不删除、重命名或改变已发布的公开类型、成员、扩展方法签名、JSON 规范字段和 `ZeusLogEvents` 编号
- 允许新增公开 API 与 JSON 字段
- 配方、权限、自动更新、新品牌驱动进入后续次版本或 `1.1`；破坏性变更进入 `1.0` 之后的大版本

## 0.16.0

补齐 IEC 60870-5-104 链路层 t1/t2/t3 与 k/w 窗口，并加固通道并发、协议接收缓冲和 TCP 服务端暴露面。同时补齐上位机运行时闭环：报警队列、点历史落盘、图表/仪表盘绑定、热重载订阅迁移和 TCP/UDP 会话写入，并加深 Modbus 与 Mitsubishi MC。

### 包含

- IEC104：实现 t1 确认超时复位、t2/w 窗口 S 格式确认、t3 空闲 TESTFR act；JSON 支持 `t1Milliseconds`、`t2Milliseconds`、`t3Milliseconds`、`maxUnacknowledgedIFrames`、`acknowledgeWindow`
- 通道：写入串行化，关闭前排空在途写入；虚拟通道把回写推迟到写锁释放后，避免 MQTT 在 `DataReceived` 栈上自死锁
- 协议缓冲：各协议客户端接收缓冲默认上限 1 MiB；FINS/S7/Modbus TCP 长度字段先校验；DL/T 645 与 IEC104 坏帧滑动而不是抛死整轮
- TCP 服务端：新增 `MaxClients`（默认 32）；默认监听地址改为 `127.0.0.1`，避免未配置时把虚拟从站暴露到全部网卡
- 运行时：采集循环并行轮询设备；通道打开失败记 Error；热重载中途失败尽量回滚拓扑；桌面关闭改为后台释放宿主
- 其它上限：S7 虚拟 PLC 限制单个 DB 与块数量；自定义帧收件箱上限；点表历史点数上限；配置指纹不再写入明文密码
- 报警：`IPointAlarmTable` / `app.Alarms`，越限产生活动记录，支持确认、全部确认和自动复归
- 历史：可选 `AddPointHistoryFile` 或自定义 `IPointHistoryStore`；JSON 可用 `pointHistoryFile` 声明 JSONL 路径
- 界面：`BindChart`、`BindDashboard`、`BindGauge`、`BindAlarms` 与报警绑定源，WinForms / WPF 均可接到第三方图表
- 热重载：通道参数变更重建实例时，把旧实例上的 `DataReceived` / `StateChanged` / `PacketTraced` 迁到同名新通道
- 会话写入：TCP/UDP 服务端实现 `ISessionChannel`；`DataReceived` 带 `RemoteEndPoint`，可按远端 `WriteAsync`
- Modbus：功能码 0x2B/0x0E 读设备识别，0x14/0x15 读写文件记录；虚拟从站同步支持
- MC：3E/4E 多块批量读取（0x0406）与远程 RUN/STOP/PAUSE/锁存清除/复位；虚拟 PLC 同步支持

### 行为变化

- IEC104 默认 `T3` 为 20 秒，空闲会发送 TESTFR act；联调虚拟站可把 `t3Milliseconds` 设为 0
- `TcpServerOptions.LocalAddress` 默认从 `0.0.0.0` 改为 `127.0.0.1`；对外监听需显式指定
- TCP/UDP 服务端无参 `WriteAsync` 仍回复最近对端；多客户端请改用带远端的重载
- 热重载重建通道后，业务代码持有的旧 `IChannel` 引用不再收数据，应通过 `Channels.Get(name)` 取新实例，或依赖自动迁移的事件订阅

### 兼容承诺

- 只新增公开 API，不删除或改变 0.15 已发布的类型、成员和扩展方法签名
- `0.16.x` 补丁只修缺陷；破坏性变更进入后续次版本

## 0.15.0

新增 SNMP v2c 协议栈，覆盖 OID GET/SET、community 访问控制、点表采集、点名写回、JSON 配置与虚拟 Agent 联调。

### 包含

- SNMP：新增 `Zeus.Protocols.Snmp`，支持 community、request-id、单 OID GET/SET 与 Integer、Gauge32、Counter32、TimeTicks、OCTET STRING、OID、IPv4
- 点表与写回：支持 OID 点表、工程值缩放、报警限与按点名写回
- 虚拟 Agent：新增 `SnmpAgentResponder` 与 `SnmpAgentMemory`，无需硬件即可验证 SNMP 会话和点表链路
- 配置与示例：JSON 支持声明 `snmp` 设备、`responder: "snmp"` 虚拟 Agent；新增 SNMP 控制台示例、协议测试和文档指南

### 兼容承诺

- 只新增公开 API，不删除或改变 0.14 已发布的类型、成员和扩展方法签名
- `0.15.x` 补丁只修缺陷；破坏性变更进入后续次版本

## 0.14.0

新增 MQTT 3.1.1 协议栈，覆盖常用客户端会话能力、QoS 握手、保留消息、遗嘱、保活、重连、点表采集与 JSON 配置。

### 包含

- MQTT 客户端：支持 CONNECT、DISCONNECT、PINGREQ/PINGRESP、SUBSCRIBE/UNSUBSCRIBE 和 QoS 0/1/2 发布确认
- 会话能力：支持保留消息、空载荷删除保留消息、遗嘱消息、最大报文长度、UTF-8 与主题通配符校验
- 可靠性：支持自动保活、通道恢复后的自动重连与订阅恢复
- 点表：支持文本、布尔、32/64 位整数、双精度和字节载荷，支持 QoS、retain、报警限与可写点
- 虚拟 Broker：新增 `MqttBrokerResponder` 与 `MqttBrokerMemory`，无需硬件即可验证 MQTT 会话和点表链路
- 配置与示例：JSON 支持声明 `mqtt` 设备、`responder: "mqtt"` 虚拟 Broker；新增 MQTT 控制台示例与协议测试

### 兼容承诺

- 只新增公开 API，不删除或改变 0.13 已发布的类型、成员和扩展方法签名
- `0.14.x` 补丁只修缺陷；破坏性变更进入后续次版本

## 0.13.0

新增 IEC 60870-5-104 协议栈，覆盖 STARTDT 启动、总召唤、单点命令、归一化/标度化/短浮点设点、点表采集、点名写回、虚拟站、JSON 配置与控制台示例。

### 包含

- IEC104：新增 `Zeus.Protocols.Iec104`，支持 I/S/U 帧、STARTDT、总召唤确认和激活终止
- 信息对象：支持 `M_SP_NA_1`、`M_ME_NA_1`、`M_ME_NB_1`、`M_ME_NC_1` 采集
- 命令与设点：支持 `C_SC_NA_1`、`C_SE_NA_1`、`C_SE_NB_1`、`C_SE_NC_1` 写回
- 虚拟站：新增 `Iec104SlaveResponder` 与 `Iec104StationMemory`，无硬件即可验证主站逻辑
- 配置：JSON 支持声明 `iec104` 设备、`responder: "iec104"` 虚拟站和 IEC104 点表
- 示例与文档：新增 IEC104 控制台示例、JSON 配置样例、NuGet 安装说明和文档指南

### 兼容承诺

- 只新增公开 API，不删除或改变 0.12 已发布的类型、成员和扩展方法签名
- `0.13.x` 补丁只修缺陷；破坏性变更进入后续次版本

## 0.12.0

新增 DL/T 645-2007 电能表协议栈，覆盖电表唤醒前导、BCD 数据项、原始数据项、写数据、点表采集、点名写回、虚拟表计、JSON 配置与控制台示例。

### 包含

- DL/T 645：新增 `Zeus.Protocols.Dlt645`，支持 12 位表地址、前导 `0xFE`、控制码、数据域加减 `0x33` 和校验
- 数据项：支持四字节 DI 读数据，覆盖 BCD 数据项、原始数据项和可配置数据长度
- 写回：支持按点名写回 BCD 或原始数据项，并可配置密码与操作者代码
- 虚拟表计：新增虚拟 DL/T 645 responder，无硬件即可验证采集、写回和点表绑定
- 配置：JSON 支持声明 `dlt645` 设备、`responder: "dlt645"` 虚拟表计和 DL/T 645 点表
- 示例与文档：新增 DL/T 645 控制台示例、JSON 配置样例、NuGet 安装说明和文档指南

### 兼容承诺

- 只新增公开 API，不删除或改变 0.11 已发布的类型、成员和扩展方法签名
- `0.12.x` 补丁只修缺陷；破坏性变更进入后续次版本

## 0.11.0

新增 Modbus ASCII 封装，覆盖冒号起始帧、十六进制文本编码、LRC 校验、CRLF 定界、虚拟从站、点表采集和 JSON 配置。

### 包含

- Modbus ASCII：新增 `ModbusTransport.Ascii` 与 `AddModbusAscii`，保留现有 Modbus PDU 和读写 API
- 帧处理：支持 `:...LRC\r\n` 请求/响应封装，接收侧按 CRLF 拆帧并校验 LRC
- 虚拟从站：`ModbusSlaveResponder` 支持 ASCII 封装，可无硬件验证主站逻辑
- 配置：JSON 支持声明 `modbus-ascii` 设备和 `transport: "ascii"` 虚拟 Modbus 从站
- 文档：更新 Modbus 指南、NuGet 包说明和故障排查中的 RTU/TCP/ASCII 选项

### 兼容承诺

- 只新增公开 API，不删除或改变 0.10 已发布的类型、成员和扩展方法签名
- `0.11.x` 补丁只修缺陷；破坏性变更进入后续次版本

## 0.10.0

新增 Panasonic MEWTOCOL-COM ASCII 协议栈，覆盖 BCC 校验、DT/LD/FL 数据寄存器、X/Y/R/L 接点字读写、点表采集、点名写回、虚拟 PLC、JSON 配置与控制台示例。

### 包含

- MEWTOCOL：新增 `Zeus.Protocols.Mewtocol`，支持 `%NN#` 请求帧、`$` 正常响应、`!` 错误响应和两位 BCC 校验
- 内存区：支持 DT、LD、FL 数据寄存器和 X、Y、R、L 接点字读写；位点通过读改写所在字实现
- 点表与写回：支持 `Bit`、`Word`、`Int16`、`UInt32`、`Int32`、`Real`，并支持 32 位值字序配置、工程值缩放、报警限与按点名写回
- 虚拟 PLC：新增 `MewtocolSlaveResponder` 与 `MewtocolSlaveMemory`，无硬件即可验证 MEWTOCOL 帧、点表采集和写回链路
- 配置：JSON 支持声明 `panasonic-mewtocol` 设备、`responder: "mewtocol"` 虚拟 PLC 和 MEWTOCOL 点表
- 示例与文档：新增 MEWTOCOL 控制台示例、真实串口配置样例、Panasonic MEWTOCOL 指南和 NuGet 安装说明

### 兼容承诺

- 只新增公开 API，不删除或改变 0.9 已发布的类型、成员和扩展方法签名
- `0.10.x` 补丁只修缺陷；破坏性变更进入后续次版本

## 0.9.0

新增 Omron Host Link ASCII 协议栈，覆盖 ASCII 帧 FCS 校验、CIO/LR/HR/AR/DM 常用字区读写、点表采集、点名写回、虚拟 PLC、JSON 配置与控制台示例。

### 包含

- Host Link：新增 `Zeus.Protocols.HostLink`，支持 `@UU` 站号帧、FCS 校验和两位十六进制结束码异常
- 内存区：支持 CIO/IR、LR、HR、AR、DM 字区读写；位点通过读改写所在字实现
- 点表与写回：支持 `Bit`、`Word`、`Int16`、`UInt32`、`Int32`、`Real`，并支持 32 位值字序配置、工程值缩放、报警限与按点名写回
- 虚拟 PLC：新增 `HostLinkSlaveResponder` 与 `HostLinkSlaveMemory`，无硬件即可验证 Host Link 帧、点表采集和写回链路
- 配置：JSON 支持声明 `omron-host-link` 设备、`responder: "host-link"` 虚拟 PLC 和 Host Link 点表
- 示例与文档：新增 Host Link 控制台示例、真实串口配置样例、Omron Host Link 指南和 NuGet 安装说明

### 兼容承诺

- 只新增公开 API，不删除或改变 0.8 已发布的类型、成员和扩展方法签名
- `0.9.x` 补丁只修缺陷；破坏性变更进入后续次版本

## 0.8.0

新增 Allen-Bradley EtherNet/IP CIP 协议栈，覆盖 Register Session、SendRRData、CIP 标量标签读写、CIP 属性访问、点表采集、点名写回、虚拟 PLC、JSON 配置与控制台示例。

### 包含

- EtherNet/IP：新增 `Zeus.Protocols.EtherNetIp`，支持 TCP 44818 上的 Register Session 与 SendRRData
- CIP 标签：支持 Read Tag / Write Tag，覆盖 Bool、SInt、Int、DInt、LInt、USInt、UInt、UDInt、ULInt、Real、LReal 标量类型
- CIP 属性：支持 Get Attribute Single / Set Attribute Single，便于访问标准对象或设备自定义对象
- 点表与写回：支持标签点工程值缩放、报警限、周期采集和按点名写回
- 虚拟 PLC：新增 `EtherNetIpSlaveResponder` 与 `EtherNetIpSlaveMemory`，无硬件即可验证标签读写和点表采集
- 配置：JSON 支持声明 `ethernet-ip` 设备、`responder: "ethernet-ip"` 虚拟 PLC 和 `tag` / `tagName` 标签点
- 示例与文档：新增 EtherNet/IP 控制台示例、指南、NuGet 安装说明和公开 API 基线

### 兼容承诺

- 只新增公开 API，不删除或改变 0.7 已发布的类型、成员和扩展方法签名
- `0.8.x` 补丁只修缺陷；破坏性变更进入后续次版本

## 0.7.0

新增完整 Omron FINS 协议栈，覆盖 FINS/UDP、FINS/TCP、点表采集、点名写回、虚拟 PLC、JSON 配置与现场示例配置。

### 包含

- Omron FINS：新增 `Zeus.Protocols.Fins`，支持 FINS/UDP 与 FINS/TCP
- FINS/TCP：支持节点地址握手，可自动接收并应用客户端节点号与服务端节点号
- FINS 命令：支持 Memory Area Read / Write / Fill / Multiple Memory Area Read，并保留原始命令执行入口
- FINS 内存区：支持 CIO、WR、HR、AR、DM、TIM/CNT、当前 EM 与 EM Bank 0–18 的字/位访问
- FINS 点表：支持 `Bit`、`Word`、`Int16`、`UInt32`、`Int32`、`Real`，并支持 32 位值字序配置、工程值缩放、报警限与按点名写回
- 虚拟 PLC：新增 FINS 虚拟从站，可在无硬件环境验证 UDP/TCP、读写、填充、多点读和点表采集
- 配置：JSON 支持声明 `omron-fins-udp` / `omron-fins-tcp` 设备、FINS 路由字段、字序、节点握手参数和 FINS 点表
- 示例与文档：新增 FINS 控制台示例、真实 UDP/TCP 配置样例、Omron FINS 指南和 NuGet 安装说明

### 兼容承诺

- 只新增公开 API，不删除或改变 0.6 已发布的类型、成员和扩展方法签名
- `0.7.x` 补丁只修缺陷；破坏性变更进入后续次版本

## 0.6.0

补齐点表到桌面界面的绑定能力，让值、报警、错误和写回按钮可以直接从 `IPointTable` 驱动。

### 包含

- 界面绑定：新增 `PointBindingSource`，投影单点的 `Value`、`Text`、`Error`、`AlarmState`、`IsAlarmed`、`UpdatedAt` 与 `Writable`
- 界面绑定：新增 `PointHistoryBindingSource` 与 `BindHistory`，可把点表最近成功采样历史推到趋势图或报警时间线
- 界面绑定：新增 `IPointTable.BindSnapshot`，可把完整 `PointSnapshot` 封送到 UI 线程
- 界面绑定：新增点表级 `BindEnabled`，默认按“可写且无错误”控制按钮或控件启用状态
- WinForms / WPF：新增点表 `AsBindingSource`、报警前景/背景色绑定和 `BindWriteBack` 按钮写回辅助方法
- 示例：WinForms / WPF QuickStart 增加本地点表温度点、报警色与状态展示

### 兼容承诺

- 只新增公开 API，不删除或改变 0.5 已发布的类型、成员和扩展方法签名
- `0.6.x` 补丁只修缺陷；破坏性变更进入后续次版本

## 0.5.0

补齐 TCP 服务端通道、Mitsubishi MC、Siemens S7 与 Modbus 诊断能力，同时完善 JSON 配置声明。

### 包含

- 通信：新增 `TcpServerChannel`、`TcpServerOptions`、`AddTcpServer` 与 `AddTcpServerAsync`，支持多客户端接收、最近客户端回复与广播
- Modbus：新增功能码 07、08、11，支持异常状态、诊断回显和服务器 ID 读取
- 三菱 MC：新增 `Zeus.Protocols.Mc`，支持 MC 1E/3E/4E Binary/ASCII、X/Y/M/D/W/R/ZR 常用软元件、3E/4E 随机读写和虚拟 PLC 联调
- Siemens S7：新增 `Zeus.Protocols.S7`，支持 S7 TCP 握手、Read/Write Var、DB/I/Q/M 区 Bool/Byte/Word/DWord/Int/DInt/Real 读写、点表采集、点名写回和虚拟 PLC 联调
- 配置：JSON 支持声明 Mitsubishi MC 与 Siemens S7 设备、虚拟 PLC、协议选项、点表采集和按点名写回

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
