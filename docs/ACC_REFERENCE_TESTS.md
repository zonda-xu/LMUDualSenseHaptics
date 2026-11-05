# ACC 参考回归测试

测试场景参考 [ACC-DualSense-Feedback 的 GripLossFeedbackChecks.cs](https://github.com/Adudumax/ACC-DualSense-Feedback/blob/0c84c1901842fa1b864a3445226ddd64f991ffad/tests/GripLossFeedbackChecks.cs)，固定提交 `0c84c1901842fa1b864a3445226ddd64f991ffad`，作者 Adudumax，MIT 许可见 `THIRD_PARTY_NOTICES.md`。测试输入由本项目生成，是合成的 LMU 帧，不是 ACC 原始驾驶日志。

| ACC 参考场景 | 本项目验证 |
|---|---|
| 正常、稳定负载转弯、前轮推头 | 不误判后轴失抓；有无偏航信息时，L2/R2 编码一致 |
| 正常滑移基线 | 首个可信侧滑角、首次有效纵向滑移分别初始化；合成回放另验证较高但可信的稳定转弯不会在启动时产生滑移提示 |
| 单轮异常、路肩、草地 | 左右轮分别验证；不凭单轮跳变触发后轴事件，定向 PCM 不错误串到对侧 |
| 纯 TC／纵向空转 | 先学习有效轮胎半径，再施加逐轮纵向滑移；不误报后轴横向失抓，扳机保持原行为 |
| 成对后轮滑移／车身横移支持 | 两种条件分别能确认失抓；验证左右音圈中独立的后轴声道 |
| 制动、全油门、TC | 后轴横向提示不被这些状态错误关闭；两扳机编码不受该检测影响 |
| 反打、偏航反向、恢复正常弯 | 提示可持续进入恢复阶段；确认结束后按 100 ms 释放并最终归零 |
| 低速、倒车、未知偏航、重复帧、过期、序号或时钟回退 | 清除或拒绝旧后轴事件，并验证后续可信帧可重新触发 |
| 非有限输入、关闭抓地反馈 | 错误输入无法使状态保持为 NaN；关闭后输出静音且扳机一致 |
| 计算开销 | 预热后连续 100,000 次 `RearDragState.Update` 不产生托管分配；这不等于整个应用零分配 |

`AccReferenceRegressionTests.cs` 随现有测试项目及 `scripts/build.ps1` 自动执行。`scripts/verify-acc-reference-replays.py` 在 CI 中另外生成 11 个合成 LMU 场景，调用生产遥测管线与四声道 PCM 合成，检查路肩定位、后轴恢复、纯 TC/空转、HID/音频路由和最终停止状态。结果写入 `artifacts/validation/acc-reference-replays.json`。原有 10 组 `verify-replays.py` 回放保留，且为独立的 CI 步骤。

在完成 Release 构建后，也可在仓库根目录运行：

```powershell
python scripts/verify-replays.py
python scripts/verify-acc-reference-replays.py
```

## 与 ACC 原测试的边界

ACC 原测试可使用 ACC 原生 XInput 信号、SlipVibration 和 ACC 的转向角等输入；LMU 没有完全对应的实时字段，测试不能声称逐帧或手感相等。本项目使用 LMU 的接地证据、归一化转向输入与局部角速度。不同于 ACC 测试中的 120 ms 冻结包清除，本项目在**新物理帧**间隔超过 120 ms 时重置新的路面／后轴状态；冻结帧的全输出停止由现有可配置遥测过期时间控制，默认 250 ms。

关闭“轮胎打滑”会关闭对应 PCM 输出；内部检测状态可能继续更新，与 ACC 将状态清零的策略不同。测试检验最终输出，不将二者的内部状态视为完全相同。合成回放、单元测试和构建不证明真实游戏数据有效、触觉力度合适或实体手柄体验已通过验证。
