> v1.2.1 刹车行为以 [当前算法说明](BRAKE_1_2_1.md) 为准；下文保留此前功能和验证记录。

> 本文件继承自上传 v0.4.0，用于旧参数/布局背景；v0.5.0 音频配置和当前验证状态以 README、REVISION_v0.5.0.md、VALIDATION.md 为准。

# 候选 ABI 字段核对

**v0.4.0 说明：下文原有录制核对属于 v0.3 历史记录，本次未重跑；新增字段仅与公开结构交叉核对，尚待用户实机。**

所有偏移来自固定参考结构交叉核对，不是内存扫描或猜测。LMU 14200 的两份用户原始录制已用于解析回归；完整单位/轴与高级阈值仍需实车标定。v0.3.0 使用初始参数启用全部反馈，精细标定仅优化参数。完整事实表见 `layout-facts.json`。

总大小 324820；Pack=4；104 个车辆槽；车辆 1888 字节；车轮 260 字节。数字按 little-endian 读取。所有下列物理 double 在统一帧中检查有限/float 溢出后转换 float；没有将未知/非有限值变成 0。

| 基准 | 字段 | 相对偏移 | 类型/单位/用途 |
|---|---|---:|---|
| 根 | gameVersion |64| int32，游戏版本，非 ABI 结构版本 |
| 根 | appInfo.mOptionsLocation |96| byte，仅诊断；LMU 14200 实时驾驶可能为 0，不作 Driving gate |
| 根 | telemetry |128464| 玩家索引 1，hasVehicle 2，车辆数组 4 |
| 根 | scoring |1632| 车辆数 +104(int32)，gamePhase +108(byte)，realtime +115(bool) |
| scoring | vehScoringInfo |560| 每车 584；mID +0，mIsPlayer +196，mControl +197，类别文本 +200/32 |
| scoring vehicle | mInGarageStall |507| bool（584-byte / Pack=4）；true 时不判为驾驶 |
| vehicle | mID |0| int32；与 scoring 玩家对应 |
| vehicle | mElapsedTime |12| double，物理时间秒；识别实际新帧 |
| vehicle | mVehicleName / Model |32 /796| UTF-8 64 /30 字节；独立读取，不猜测类别枚举 |
| vehicle | mLocalVel x/y/z |184/192/200| double m/s；X 横向，Y 垂直，Z 纵向；正负待驾驶验证 |
| vehicle | mLocalAccel x/y/z |208/216/224| double m/s²；轴正负待验证 |
| vehicle | mGear |352| int32，-1..20 合理性检查 |
| vehicle | mEngineRPM / MaxRPM |356/532| double RPM；最大值无效时不做红线提示 |
| vehicle | throttle / brake / steering |388/396/404| double，0..1 /0..1 /-1..1 |
| vehicle | mLastImpactET / Magnitude |552/560| double；碰撞新增时刻证据，量纲/事件语义待核对，不直接作力度真值 |
| vehicle | mABSActive / mTCActive |746/747| bool；activation 与辅助级别不同，HUD 对照必须验证 |
| vehicle | mTCMax / mABSMax |751/757| byte；可用步骤候选证据，车型核对后才可完成 availability 验收 |
| vehicle | mWheels |848| FL、FR、RL、RR；每轮260字节 |
| wheel | mSuspensionDeflection |0| double m |
| wheel | mSuspForce |16| double N |
| wheel | mRotation |40| double rad/s |
| wheel | mLateralGroundVel / LongitudinalGroundVel |64/72| double m/s；轮胎局部方向需实测 |
| wheel | mTireLoad |104| double N，负载荷拒绝 |
| wheel | mTerrainName |160| char[16]，路面材质标签，UTF-8 解码 |
| wheel | mSurfaceType |176| byte；0干、1湿、2草、3土、4砾石、5路肩条纹、6特殊；未知保留 null |
| wheel | mVerticalTireDeflection / mWheelYLocation |180/188| double m |

TireDirty 不依赖：null 保留未知；内部轮序固定。来源未给出独立 ABI version，因此 gameVersion、尺寸和合理值只作为核对证据，不能自动证明当前 ABI 完全一致。

共享锁：现有 `LMU_SharedMemoryLockData` 八字节映射，int32 waiters @0、busy @4；CAS(0→1) 获取、volatile release、waiters 非零时 SetEvent。对象名 `LMU_SharedMemoryLockEvent`；数据事件 `LMU_Data_Event`。只打开已有对象，不创建私有同名替代锁。此协议经参考源码静态核对，仍待官方及实机核验。

## v1.2-preview 新增字段

| 基准 | 字段 | 偏移 | 语义 |
|---|---|---:|---|
| wheel | mBrakePressure | 32 | double，当前 0–1 刹车压力比例，非 kPa |
| wheel | mGripFract | 112 | double，0–1 接触面滑动比例估计，非剩余抓地力 |
| vehicle | mABS | 756 | byte，当前 ABS 档位；与 +746 的实际介入及 +757 的可用档位分开 |

新字段只读已公开的结构；线上游戏是否持续更新需实车验证。计算及验证范围见 BRAKE_PREVIEW.md。
