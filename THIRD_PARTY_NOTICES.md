# 第三方声明

本应用独立代码按根目录 MIT LICENSE 分发。以下改编代码保留原作者许可：

## ACC-DualSense-Feedback

作者 Adudumax，固定提交 e97b926be86a3c92789d9e5e2c4a3ad3eb741c7c。

改编文件：`Hardware/TriggerEffect.cs`、`Hardware/DualSenseReportBuilder.cs`（仅 USB 分支）、`Hardware/DualSenseHidDevice.cs`、`Hardware/NativeMethods.cs`。

MIT License

Copyright (c) 2026 Adudumax

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## 仅作事实/思想参考，未纳入代码

TinyPedal：GPL-3.0-or-later。未复制、链接、打包或作为运行依赖使用其 GPL Python 实现。
LMU-DualSense-Rust：核对时仓库树未找到明确 LICENSE；未复制或再分发其 Rust 源码。
pyLMUSharedMemory：仅作为公开 ABI 字段、类型、Pack/大小与偏移的核对来源；未打包其实现和辅助源码。字段事实表见 docs/layout-facts.json。

## Microsoft .NET

发布包含 Microsoft .NET 8 和 WindowsDesktop 自包含运行时，将实际运行时 NuGet 包附带的许可和第三方声明保留在 docs/licenses/。构建依赖在 Microsoft SDK/框架包中，不引入 ViGEm、DSX、HidHide、DS4Windows、TinyPedal 或其他驻留运行时。


## UI and road-feedback design references

SimHub ShakeIt wiki and Google Material 3 documentation were consulted for design concepts, not copied as application source/assets. The additional LMU surface/material parser is an independent implementation of public interface facts. No SimHub, Material package, font files or pyLMU implementation is bundled. See docs/REFERENCES.md for scope and URLs.


## Independent audio-haptics implementation

New WASAPI COM declarations, PCM DSP, routing and tests are independently written. No new runtime NuGet dependency is introduced. SimHub documentation was used for design concepts; upstream Linux/Sony and a DualSense haptics implementation were consulted for protocol/channel facts only, not copied source. References are in docs/REFERENCES.md. Existing third-party licenses and copyright notices above continue to apply to the inherited code.
