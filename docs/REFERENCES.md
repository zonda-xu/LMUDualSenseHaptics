# 接口与设计参考

源码记录的参考日期：2026-10-12。以下为公开的一手文档/实现；只采用接口事实和设计概念。本程序 DSP、WASAPI 后端和测试为独立编写，未复制 SimHub 或 Linux 驱动的实现代码。

## SimHub / ShakeIt

- 官方效果配置：https://github.com/SHWotever/SimHub/wiki/ShakeIt-V3-Effects-configuration
- 官方音频输出配置：https://github.com/SHWotever/SimHub/wiki/ShakeIt-V3-Bass-Shakers---Audio-Output-Configuration

参考效果级/通道级增益、将遥测效果电平映射到声音频率、独立频率与振幅及短冲击保持。不是使用其内部核心，也没有把其 bass-shaker 默认频率直接称作 DualSense 已验证预设。

## DualSense 接口事实

- Linux 上游 Sony 驱动：https://raw.githubusercontent.com/torvalds/linux/master/drivers/hid/hid-playstation.c
  查阅输出报告标志定义与选择 classic rumble 的逻辑：兼容震动位、HAPTICS_SELECT、compatible-vibration-v2。这里只使用协议定义，不复制 GPL 驱动代码。本项目原 Windows 48 字节报告格式保留，不把 Linux 报告长度直接搬过来。
- 实作者原始项目：https://github.com/HotspringDev/DualSense-haptic-helper
  查阅四声道设备中零基 2/3 声道用于触觉的说明。平台为 Linux，作为通道事实参考，不构成本项目 Windows WASAPI 已运行或固件兼容的证据。

## Microsoft Windows API

- Rendering a Stream：https://learn.microsoft.com/en-us/windows/win32/coreaudio/rendering-a-stream
- IAudioClient::Initialize：https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-initialize
- IAudioClient：https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-iaudioclient
- IMMDeviceEnumerator：https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immdeviceenumerator
- IPropertyStore：https://learn.microsoft.com/en-us/windows/win32/api/propsys/nn-propsys-ipropertystore
- DEVPKEY_Device_ContainerId：https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-containerid

参考 COM 调用顺序、共享/事件驱动音频初始化、原生混音格式、缓冲提交、设备端点及物理设备分组。运行时接口是否正常仍需 Windows 上编译与设备验收。

## 遥测、界面与 USB 控制

- ACC-DualSense-Feedback： https://github.com/Adudumax/ACC-DualSense-Feedback 。USB 与扳机代码的改编范围、固定提交及 MIT 许可见根目录 THIRD_PARTY_NOTICES.md。
- pyLMUSharedMemory： https://github.com/TinyPedal/pyLMUSharedMemory/blob/master/lmu_data.py 。用于核对公开共享内存结构、车轮字段、地表类型和 TC 标志；未纳入其 Python 实现。事实表见 layout-facts.json。
- Google Material 3： https://developer.android.com/develop/ui/compose/designsystems/material3 。用于界面颜色、形状与排版概念，原生 WPF 独立实现。
