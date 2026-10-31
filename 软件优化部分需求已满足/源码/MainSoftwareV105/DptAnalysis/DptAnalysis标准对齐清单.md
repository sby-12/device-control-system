# DptAnalysis 标准对齐清单

生成时间：2026-10-31
依据：IEC 60747-9（IGBT 开关时序与能量，现行 2019 第 3 版）、IEC 60747-8（MOSFET）、IEC 60747-2（二极管反向恢复）、JEDEC JEP182 / JESD24（宽禁带专项）。
方法来源对照：是德 PD1000A 帮助页（参数→IEC 条款号索引）、泰克 AFG31000 双脉冲手册与 WBG-DPT 资料（公式层公开；实现层常数各厂商不公开，属行业常态）。

> 重要更正（2026-10-31）：细时间参数已实现 IEC 的 10%/90% 参考电平穿越（v10/v90/i10/i90），
> 50% 迟滞仅用于粗定位脉冲位置，不影响参数定义对齐。

## 一、已对齐项（公式与 IEC 定义一致，可直接采信）

| 参数 | IEC 定义 | 实现位置 | 结论 |
|---|---|---|---|
| td(on) | 10%VGE → 10%ICM | DptSwitching.cs `tv10 → ti10on` | 一致 |
| tr | 10%ICM → 90%ICM | `ti10on → ti90on` | 一致 |
| ton | td(on)+tr | 相加 | 一致 |
| td(off) | 90%VGE → 90%ICM | `tv90 → ti90off` | 一致 |
| tf | 90%ICM → 10%ICM | `ti90off → ti10off` | 一致 |
| tz（拖尾） | 10%ICM → 2%ICM | 继续搜 2% 穿越 | 一致（IGBT 少子拖尾；MOSFET 无拖尾则为 0） |
| toff | td(off)+tf | 相加 | 一致 |
| Eon | ∫VCE×IC dt，10%VGE 起积到 VCE 降至 2%VCC | 10%VGE 起积到 VCE ≤ Lo+2%×span | 一致（span≈VCC 时等价） |
| Eoff | ∫VCE×IC dt，90%VGE 起积到 IC 降至 2%ICM | 完全相同 | 一致 |
| Qrr | ∫Irr dt（过零起积） | 过零 → 25%Irr 回落点积分 | 公式一致，区间定义见待确认项 1 |

## 二、需确认项（标准有自由度或做了工程取舍）

| 优先级 | 事项 | 现状 | 要确认什么 |
|---|---|---|---|
| 高 1 | trr 终点定义 | 过零 → \|Irr\| 回落到 25%Irr | IEC 60747-2 现行版"25% 法"与"切线外推法"并存，datasheet 多用切线法，两者能差 10~20%。确认实验室泰克系统用哪种 |
| 高 2 | Qrr 积分区间 | 与 trr 同窗口 | 须与 trr 定义联动修改，保持 Qrr=∫(同一区间) |
| 高 3 | 通道 deskew（时间对齐） | 软件未处理，假定三通道已对齐 | V/I 探头延迟差必须校准（泰克原话："纳秒级错位就会算错能量"）。确认实验室流程是硬件校准还是软件校准；若软件校，提取界面需加 deskew 输入 |
| 高 4 | 探头比例链 | 波形值直接当伏/安用 | 差分探头衰减比（如 100:1）、电流探头 A/V 系数是否已折进波形？若原始数据是示波器伏数，Eon/Pmax 会错固定倍率 |
| 中 5 | dv/dt、di/dt | 两参考电平间平均斜率 | 是德/泰克报"参考电平间导数最大值"（Keysight: "Report the maximum"），我们是均值，同一颗管子数值会不同。需决定跟随哪家口径 |
| 中 6 | Err 定义 | ∫\|Vd·Irr\| dt | 反向恢复能量无强标准，各家口径不同，需对标一次 |
| 中 7 | 栅极电荷 Qg | 需 Ig 通道恒流充电波形 | 确认工装有无栅极电流探头、接哪一路；米勒平台用"斜率<15% 最长段"启发式识别，需与 datasheet 曲线比对 |
| 中 8 | UIS 雪崩窗判定 | Vds > 母线+20% | EAS 标准测试条件（单脉冲、规定 L 值、起始结温 25℃）由操作员保证；窗口启发式需真波形验证 |
| 中 9 | Tsc 短路时间 | 电流 >10%Imax 持续期 | IEC 短路耐量=短路发生到关断/失效的时间，当前为近似口径，需确认可接受 |
| 低 10 | Icm 取 95% 分位 | 标准语义为集电极电流最大值 | 分位法抗毛刺、更接近平台真值，可保留 |
| 低 11 | Eon 终点 2% 判定基准 | 实测电平跨度 span | IEC 原文为 2%×VCC（母线设定值）。建议提取界面加"母线电压"输入，终点改判 2%×VCC 设定值 |

## 三、下一步（按优先级）

1. 真机对标前必须先解决高 3 / 高 4（deskew 与探头比例），否则能量类参数没有可比性；
2. 高 1 / 高 2 与实验室泰克系统确认 trr 口径后联动修改；
3. 中优先级各项在对标时逐项定案；
4. 完成后删除代码中"简化版"字样，在 DptMath/DptSwitching 头部注释写明所遵循的标准条款号。

## 参考来源

- 是德 PD1000A 双脉冲术语与定义（参数→IEC 条款索引）：https://helpfiles.keysight.com/sp/PD1000A/Content/PD1500A%20DPT%20Tests/DPTTermsAndDefinitions.htm
- Infineon IGBT 数据手册应用笔记（IEC 60747-9 开关定义图示）：https://www.infineon.com/assets/row/public/documents/60/42/infineon-discrete-igbt-datasheet-explanation-applicationnotes-en.pdf
- 泰克 AFG31000 双脉冲应用手册（Eon/Eoff 公式与测量图示）：https://download.tek.com/manual/077163100_AFG31000_Double_Pulse_Application_EN_Sept2019_0.pdf
- 泰克 WBG-DPT 资料（宣称 per JEDEC and IEC standards，积分区间可视化）：https://download.tek.com/document/double-pulse-testing---global.pdf
