# 回归数据说明

7 个 .synthetic.jsonl 文件由测试工程 --make-fixtures 生成：clean、left-kerb、right-kerb、heavy-brake、corner、landing、collision。它们不是 LMU 录制，不对应任何真实车辆。

Profile.DefaultParameters.Name 明确标为 SYNTHETIC，数值与本版初始值一致，仅用于合成场景断言；不声称是已验证车型参数。回放结果来自实际运行同一反馈管线，刻意不访问 HID。真实采集需按 docs/VALIDATION.md 的场景矩阵补齐。

`lmu14200-options0.snapshot.bin`：从用户 030537 记录提取的最小字段快照，保留玩家物理字段、版本/车型/类别和判定字段；清除其他车辆、玩家名称和未使用区域。不是完整原始录制，也不是合成路面标定。专门复现 OptionsLocation=0、realtime=true、Control=0、Garage=false 的 Driving 故障。

## v0.4 新场景

tagged-left-kerb / tagged-right-kerb：悬架与载荷不变，仅在第 50–149 帧注入 surface=5，验证独立路肩与左右定位。tc-toggle：第 50–99、150–151 帧开启真实 TC 标志，其他帧关闭，验证短脉冲和即时释放。均为合成数据，非实车记录。
