# LMU DualSense Haptics

为 **Le Mans Ultimate（勒芒终极版）** 提供 DualSense USB 自适应扳机和左右握把音圈反馈。当前版本：**v1.3.5-preview**。界面为中文。

## 功能

- **L2 刹车**：自动反复识别车型；GT3 使用 v1.0.1 LT；其他车型使用可调踏板曲线、渐进滑移增阻，严重抱死短脉冲后恢复持续阻力。
- **R2 油门**：默认只在牵引力控制（TC）介入时增加阻力；可启用基础阻力、空转、转速上限及升挡提示。
- **左右握把**：根据车辆数据呈现路面纹理、颠簸、路肩、打滑、碰撞、升挡和引擎震动。
- **调节与保存**：各效果独立开关和强度、路面灵敏度、左右分离、震动频率、浅色/深色外观、车型参数导入导出。
- **连接与排障**：手柄检查、实时状态、驾驶数据记录和限额日志。

程序直接读取 LMU 车辆数据，通过 USB 控制手柄；不采集游戏声音，不提供摇杆/按键映射。音圈模式通过手柄四声道音频设备输出，扳机由独立的 USB 控制通路输出。

## 使用条件

- Windows x64，支持 .NET 8 Windows Desktop 的系统。
- DualSense 手柄和支持数据传输的 USB 连接线。本版提供 USB 连接。
- 手柄播放设备已启用，具有四声道配置；建议使用 48 kHz 格式。
- LMU 在本机运行。

## 下载与开始使用

1. 在仓库 **Releases** 页面下载 `LMUDualSenseHaptics-win-x64-v1.3.5-preview.zip`，完整解压后运行 `LMUDualSenseHaptics.exe`。自包含版本无需另外安装 .NET。
2. 用 USB 数据线连接 DualSense。关闭 Steam Input、DSX、DS4Windows 等软件对同一手柄的震动输出。按键映射可按需继续使用；映射软件也可能占用手柄，连接异常时先退出相关软件排查。
3. 让游戏声音继续通过耳机或音箱播放。不要将 DualSense 设为游戏声音输出设备。
4. 在“路面与震动”中启用“使用 USB 音圈震动”，选择当前手柄音频设备；通常可使用自动选择。
5. 离开驾驶状态，在“连接与工具”中点击“左握把震动”和“右握把震动”。各持续 1 秒，确认方向和实际震动。
6. 启动 LMU 并进入驾驶，程序自动启用反馈。先调节路面和路肩强度，再增加引擎等效果，最后点击“保存配置”。

在“自适应扳机”中选择自动切换，可在 GT3 与其他车型间反复切换。未标定时不输出持续参考墙；界面可设置刹车轴死区、满刹行程和曲线指数。详见 [v1.3.5-preview 刹车算法说明](docs/BRAKE_1_3_5_PREVIEW.md)。

顶部左右握把读数是软件输出电平；L2/R2 数值表示扳机输出等级。握把震动频率用于调节触感。详细使用及故障排查见 [使用指南](docs/USER_GUIDE.md)。

## 配置和日志

配置位于 `%LOCALAPPDATA%\LMUDualSenseHaptics\profile.json`，保存时会备份已有配置。BrakeMode 默认 Auto；BrakeResponse 提供新预测参数。GT3 v1.0.1 分支默认使用 30%/60% 抱死阈值，其他车型使用新算法。

日志位于同目录下的 `logs` 文件夹：自动诊断日志每文件上限 **16 MiB**；主动记录的驾驶数据每文件上限 **128 MiB**。达到上限即停止写入，旧日志不会自动删除。录制期间反馈设置锁定；点击“停止记录驾驶数据”后可继续调整。

## 从源码构建

Windows 安装 **.NET 8 SDK**，在仓库根目录运行 `build.cmd`，或在 PowerShell 中运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
```

脚本构建 Release、执行 C# 回归测试、发布自包含 Windows x64 应用，并生成 `artifacts/LMUDualSenseHaptics-win-x64-v1.3.5-preview.zip`。无需安装额外的运行时 NuGet 依赖。

```powershell
# 仅运行算法与协议回归测试
dotnet run --project tests/LMUDualSenseHaptics.Tests -c Release
```

[GitHub 发布指南](docs/GITHUB_RELEASE.md) 介绍上传仓库、自动构建和正式发布。测试范围见 [验证说明](docs/VALIDATION.md)。

## 许可证与致谢

项目采用 [MIT License](LICENSE)。部分 USB 协议与扳机代码改编自 Adudumax 的 [ACC-DualSense-Feedback](https://github.com/Adudumax/ACC-DualSense-Feedback)，版权与许可保留在 [第三方声明](THIRD_PARTY_NOTICES.md)。其他接口及设计参考见 [REFERENCES](docs/REFERENCES.md)。

本项目是独立社区工具，与游戏及手柄厂商没有官方关联。
