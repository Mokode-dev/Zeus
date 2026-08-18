## Protocol Summary / 协议摘要

<!-- Name the vendor, protocol, transport, and target device families. / 写明厂商、协议、传输方式和目标设备系列。 -->

## Supported Scope / 支持范围

- [ ] Client read / 客户端读取
- [ ] Client write / 客户端写入
- [ ] Point table acquisition / 点表采集
- [ ] Write by point name / 按点名写回
- [ ] Virtual slave or PLC responder / 虚拟从站或 PLC 应答器
- [ ] JSON configuration / JSON 配置
- [ ] Console sample / 控制台示例
- [ ] Public API baseline / 公开 API 基线

## Memory Areas and Data Types / 存储区与数据类型

<!-- List supported areas, commands, frame types, data types, byte order, word order, address limits, and known gaps. / 列出支持的数据区、命令、帧类型、数据类型、字节序、字序、地址范围和已知缺口。 -->

## Verification / 验证

<!-- Include unit tests, integration checks, virtual PLC checks, real hardware results, and representative TX/RX frames. / 填写单元测试、集成检查、虚拟 PLC 检查、真实硬件结果和代表性 TX/RX 报文。 -->

## Documentation / 文档

- [ ] README or NuGet notes updated / 已更新 README 或 NuGet 说明
- [ ] Configuration sample added or updated / 已新增或更新配置样例
- [ ] User guide added or updated / 已新增或更新用户指南
- [ ] Changelog updated when release-facing / 面向发布时已更新变更日志

## Checklist / 检查清单

- [ ] Invalid frames, protocol errors, and timeouts are handled explicitly. / 已显式处理非法帧、协议错误和超时。
- [ ] Address, station, unit, and data type validation matches protocol limits. / 地址、站号、单元号和数据类型校验符合协议限制。
- [ ] Public types and extension methods follow existing Zeus naming patterns. / 公开类型和扩展方法遵循现有 Zeus 命名风格。
- [ ] Tests cover codec behavior, read/write operations, and point mapping. / 测试覆盖编解码、读写操作和点表映射。
