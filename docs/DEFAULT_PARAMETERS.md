> 当前无 ABS 刹车行为见 [v1.2.2 说明](BRAKE_1_2_2.md)。下文涉及 v1.2.1 的行为属于历史版本。

> v1.2.1 刹车行为以 [当前算法说明](BRAKE_1_2_1.md) 为准；下文保留此前功能和验证记录。

> 本文件继承自上传 v0.4.0，用于旧参数/布局背景；v0.5.0 音频配置和当前验证状态以 README、REVISION_v0.5.0.md、VALIDATION.md 为准。

# v0.4.0 初始参数

这些是工程起点，未在实体 DualSense 或所有车型/赛道上调校。完整 JSON 见 examples/default-parameters.json；代码以 Core/RoadResponse.cs 与 Core/FeedbackParameters.cs 为准。

## 新模式与强度

新建 Profile：ThrottleMode=TcOnly、TC 强度 75、R2 基础阻力关闭、Road=55、Kerb=75、RoadSensitivity=100、Engine=15（不变）、SpatialSeparation=60、UiTheme=Light。已有配置各强度保留；新的缺失成员用上述默认值。

## RoadResponse

悬架和车轮垂向高通速度：0.012–0.16 m/s。轮胎变形高通速度：0.004–0.065 m/s。归一化轮载变化率：0.5–8 s^-1。短冲击悬架速度：0.12–0.50 m/s，载荷冲击：2.5–15 s^-1。接地门槛 100 N。

输入先去除小于 0.015 的归一化包络，再使用 x^0.70 提升小信号，并以平滑门控叠加最高 0.12 的低端提升。这是本实现的指数定义，不等于 SimHub 界面 gamma 旋钮的方向或标度。零输入输出严格为零。

明确路肩标记的基础包络为 0.65，再随悬架/轮胎证据增强；速度门槛 0.6–6 m/s。名义路肩间距 0.45m 用于速度相关的合成幅值调制，限制在 12–48Hz，不代表实际条纹间距或硬件机械频率。路面 attack/release 为 6/90ms，路肩 8/65ms，短冲击 4/105ms。

四轮分别处理，前后同侧取较大值；交叉传递=0.28*(1-左右分离/100)。路面与路肩同侧取较大请求，避免重复累加。碰撞优先，背景引擎避让高优先级连续反馈。

## 保留项与旧预设

v1.2.1 的 GT3 分支恢复旧版 LT；其他车型预设临界位置，参数与机制见 [BRAKE_1_2_1.md](BRAKE_1_2_1.md)；Engine 仍为原有 RPM 比例乘固定 22Hz 脉冲。空转与碰撞参数保持；旧 LockRatio/LockFullRatio 已移除。

旧 V0/V1/F0/F1/L0/L1 仍参与旧学习/噪声统计，不再决定新版最终路面振幅；新的道路参数必须改 RoadResponse。旧车型预设自动补齐新的 RoadResponse，不应把其历史 Verified 当成新路面体验已经实测。
