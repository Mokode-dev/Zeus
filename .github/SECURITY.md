# Security Policy / 安全策略

## Supported Versions / 支持版本

Security fixes are prioritized for the latest released minor version and the current `main` branch.

安全修复优先覆盖最新发布的小版本和当前 `main` 分支。

| Version / 版本 | Supported / 支持 |
| --- | --- |
| Latest minor / 最新小版本 | Yes / 是 |
| Older prerelease or development snapshots / 更早预发布或开发快照 | Best effort / 尽力处理 |

## Reporting a Vulnerability / 报告漏洞

Please do not open a public issue for vulnerabilities. Use GitHub private vulnerability reporting if it is available on this repository. If that entry is not visible, contact the maintainers first through the community channel and share only that you have a security report, not the exploit details.

请不要通过公开 Issue 报告漏洞。如仓库启用了 GitHub 私有漏洞报告，请优先使用该入口；如果看不到入口，请先通过社区渠道联系维护者，只说明你有安全报告，不要公开利用细节。

Useful details include:

- Affected package, version, branch, or commit / 受影响的包、版本、分支或提交
- Minimal reproduction or proof of concept / 最小复现或概念验证
- Expected impact and affected deployment scenario / 预期影响和受影响部署场景
- Whether the issue is already public / 问题是否已经公开

## Scope / 范围

Examples of security-sensitive issues include credential exposure, unsafe file handling, denial-of-service triggered by untrusted input, dependency supply-chain risk, or protocol parsing behavior that can crash a host process.

安全敏感问题包括但不限于凭据泄露、不安全文件处理、不可信输入触发拒绝服务、依赖供应链风险，或协议解析导致宿主进程崩溃的行为。

## Disclosure / 披露

Maintainers will confirm receipt, assess severity, prepare a fix, and coordinate disclosure timing when needed.

维护者会确认收到报告、评估严重程度、准备修复，并在需要时协调披露时间。
