# 器件控制系统（Device Control System）

功率半导体器件动态测试上位机软件（WPF / .NET Framework 4.8），面向双脉冲测试（DPT）实验台。

## 功能概览

- 多设备统一接入：示波器（RIGOL MHO984 / Teledyne LeCroy）、电源（NGI N2410 / N36200、ITECH IT16000D）、信号源（原机型 / SIGLENT SDG6000X），连接窗口下拉选机型，IP 自动存档
- 实验控制：电压/电流/输出设置、示波器 RUN/STOP/AUTO、触发与档位滑杆、深存储波形保存到 Excel
- 参数提取（DptAnalysis）：开关时序与能量（IEC 60747-9 定义）、反向恢复（IEC 60747-2）、栅极电荷（JESD24）、UIS 雪崩、短路耐量，公式对齐情况见 `源码/MainSoftwareV105/DptAnalysis/DptAnalysis标准对齐清单.md`
- AI 助手：波形截图 + 知识库（AI知识库目录）辅助判读

## 目录结构

- `软件优化部分需求已满足/源码/` — 主程序 MainSoftwareV105 + 各设备协议库（库文件设计）
- `软件优化部分需求已满足/未实现清单.md` — 新设备适配状态与全部修复记录
- `软件优化部分需求已满足/源码/MainSoftwareV105/AI知识库/` — AI 助手知识库（增删 .md 即生效）
- `DPT参考源码/` — 开源双脉冲分析项目子模块（github.com/TaoCultivator/DPT），DptAnalysis 数学逻辑的移植来源
- `代码注释与逻辑分析/` — 源码逐行中文注释版与思维导图

## 构建

Visual Studio 打开 `软件优化部分需求已满足/源码/MainSoftwareV105/MainSoftwareV100.sln`，还原 NuGet 包后编译。

## 许可证

本仓库采用 CC BY-NC 4.0（知识共享署名-非商业性使用 4.0 国际许可协议）。

- 允许：自由查看、复制、修改、再发布
- 条件：必须署名原作者
- 禁止：商业性使用

详见 LICENSE 文件。协议全文：https://creativecommons.org/licenses/by-nc/4.0/deed.zh-Hans

## 安全提示

短路（SC）与雪崩（UIS）测试存在炸管风险，软件仅做事后波形分析；大功率实验请遵守实验室安全规范。
