# Security Policy / 安全政策

## 支持版本（Supported Versions）

LMU DualSense Haptics 目前仅对最新正式版本提供安全维护。

| 版本            | 安全维护 |
| ------------- | ---- |
| v1.0.1（当前正式版） | 支持   |
| v1.0.0 及更早版本  | 不再维护 |

建议始终通过本项目的 [GitHub Releases](https://github.com/zonda-xu/LMUDualSenseHaptics/releases) 获取最新版本。

## 报告安全漏洞（Reporting a Vulnerability）

如果发现可能影响用户、系统或连接设备安全的漏洞，请使用 GitHub 的 **Private Vulnerability Reporting** 功能进行私密报告。

报告入口：[Report a Vulnerability](https://github.com/zonda-xu/LMUDualSenseHaptics/security/advisories/new)

请尽可能提供以下信息：

- 漏洞描述及潜在影响。
- 受影响的应用版本和 Windows 环境。
- 可复现的操作步骤。
- 相关错误信息或经过脱敏处理的日志。
- 如有可能，提供建议的修复方式。

**请勿在公开 Issues、Discussions 或 Pull Requests 中披露尚未修复的安全漏洞。**

维护者将根据实际情况评估、复现并处理收到的安全报告。修复完成后，可通过 GitHub Security Advisories 或正式版本更新公开相关信息。

## 安全问题范围（Security Scope）

以下问题属于本项目重点关注的安全范围：

- USB HID 通信与 DualSense 设备控制中的潜在安全问题。
- WASAPI 音圈输出中的异常设备控制行为。
- LMU 遥测数据及外部配置文件解析中的潜在漏洞。
- 本地配置、日志及文件操作中的安全问题。
- 应用构建、分发及发布文件完整性问题。

一般性的功能故障、反馈参数异常或游戏兼容性问题，请通过 [GitHub Issues](https://github.com/zonda-xu/LMUDualSenseHaptics/issues) 报告。

## 官方发布与文件完整性（Official Releases）

官方版本仅通过本项目的 GitHub Releases 页面发布。

Windows 发布包同时提供 SHA-256 校验文件（`.sha256`）。用户可以通过 PowerShell 验证下载文件的校验值：

```powershell
Get-FileHash .\LMUDualSenseHaptics-win-x64-v1.0.1.zip -Algorithm SHA256
```

请将计算结果与官方发布的 SHA-256 校验值进行比对。

SHA-256 校验用于检查文件完整性，不等同于代码签名或发布者身份认证。

## 安全维护说明

LMU DualSense Haptics 是独立维护的开源社区项目。

安全问题将根据严重程度和可复现性进行评估。项目目前不承诺固定的漏洞响应或修复时限。

感谢所有以负责任的方式报告安全问题的用户与研究人员。
