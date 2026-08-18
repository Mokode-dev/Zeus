# Contributing to Zeus / 参与贡献

Thanks for contributing to Zeus. Keep changes focused, reproducible, and easy to review.

感谢参与 Zeus。请保持改动聚焦、可复现、便于审阅。

## Before You Start / 开始前

- Search existing issues and pull requests first. / 请先搜索现有 Issue 和 PR。
- Open an issue for large features, new protocols, public API changes, or breaking changes. / 大功能、新协议、公开 API 变更或破坏性变更请先开 Issue。
- Keep one pull request focused on one goal. / 一个 PR 聚焦一个目标。

## Development Guidelines / 开发约定

- Follow the existing project structure and naming style. / 遵循现有项目结构和命名风格。
- Prefer small public APIs that match existing Zeus patterns. / 公开 API 尽量小，并匹配现有 Zeus 风格。
- Add tests for behavior changes and protocol parsing logic. / 行为变更和协议解析逻辑需要测试。
- Update samples, README, NuGet notes, or docs when user-facing behavior changes. / 面向用户的行为变化需要同步示例、README、NuGet 说明或文档。
- Do not commit secrets, private device addresses, customer logs, or credentials. / 不要提交密钥、私有设备地址、客户日志或凭据。

## Protocol Contributions / 协议贡献

When adding or extending a protocol, document:

新增或扩展协议时，请说明：

- Supported transports and frame formats / 支持的传输方式与帧格式
- Supported memory areas, commands, and data types / 支持的数据区、命令和数据类型
- Address limits, station or unit limits, byte order, and word order / 地址范围、站号或单元号范围、字节序和字序
- Virtual slave or PLC behavior if included / 如包含虚拟从站或 PLC，请说明行为
- Representative request and response frames / 代表性请求与响应报文

## Commit Messages / 提交信息

Use readable Conventional Commits style. Existing commits use an emoji prefix, for example:

提交信息使用可读的 Conventional Commits 风格。现有提交默认带 emoji，例如：

```text
✨ feat(mewtocol): 新增 Panasonic MEWTOCOL 协议栈
📚 docs(host-link): 同步 Omron Host Link 文档
🐛 fix(config): 修复点表配置校验
```

## Pull Request Checklist / PR 检查

- Explain the motivation and scope. / 说明动机和影响范围。
- Include verification evidence. / 提供验证证据。
- Link related issues when applicable. / 如有关联 Issue，请链接。
- Call out breaking changes clearly. / 清晰标注破坏性变更。
