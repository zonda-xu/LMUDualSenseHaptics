# 应用图标

- `AppIcon.png`：使用者提供的原始图片，保留原始内容及尺寸。
- `AppIcon.ico`：由原图转换的 Windows 图标，包含 16、24、32、48、64、128、256 像素尺寸。

项目的 `ApplicationIcon` 将 ICO 嵌入 Windows 可执行文件；WPF `Resource` 与窗口 `Icon` 将同一图标用于窗口和任务栏。图标资源随编译嵌入，不需要将外部图标文件放在 EXE 旁边。
