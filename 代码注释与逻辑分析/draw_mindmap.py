# -*- coding: utf-8 -*-
"""生成《双脉冲控制系统》代码逻辑思维导图（鲜艳配色 + 曲线连接）v2"""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(sys.executable).parent.parent.parent))
from daimon_runtime import setup_plot
setup_plot()
import matplotlib.pyplot as plt
from matplotlib.patches import FancyBboxPatch, FancyArrowPatch

fig, ax = plt.subplots(figsize=(17, 11))
ax.set_xlim(0, 100); ax.set_ylim(0, 102); ax.axis('off')

def node(x, y, w, h, text, fc, fontsize=10, tc='white'):
    box = FancyBboxPatch((x - w/2, y - h/2), w, h,
                         boxstyle="round,pad=0.3,rounding_size=1.5",
                         fc=fc, ec='#333333', lw=1.1, zorder=3)
    ax.add_patch(box)
    ax.text(x, y, text, ha='center', va='center', fontsize=fontsize,
            color=tc, zorder=4, weight='bold' if fontsize >= 12 else 'normal')

def link(x1, y1, x2, y2, color='#666666', rad=0.22, arrow=True):
    style = '-|>' if arrow else '-'
    arr = FancyArrowPatch((x1, y1), (x2, y2), connectionstyle=f"arc3,rad={rad}",
                          arrowstyle=style, mutation_scale=13, lw=1.5, color=color, zorder=2)
    ax.add_patch(arr)

# ========== 中心 ==========
node(8, 51, 13, 9, '双脉冲\n控制系统', '#E63946', fontsize=14)

# ========== 分支1：界面层（顶部带） ==========
node(25, 86, 19, 8, '界面层\nMainSoftwareV100', '#F3722C', fontsize=11.5)
link(14.5, 51, 15.5, 86, '#F3722C')
ui = [
    ('MainWindow.xaml(.cs)\n主窗口 · 全部按钮事件 · 保存逻辑', 97),
    ('ConnectInit\n连接弹窗（IP+端口）', 89.5),
    ('MainViewModel\nOxyPlot 绘图模型（四条曲线）', 82),
    ('PlotSeriesViewModel1\n曲线显隐勾选绑定', 74.5),
    ('MyControl 自定义控件\nLed / NumericUpDown 等', 67),
]
for t, y in ui:
    node(69, y, 30, 5.8, t, '#F9844A', fontsize=9.5)
    link(34.5, 86, 54, y, '#F9844A')

# ========== 分支2：驱动层（中上带） ==========
node(25, 54, 19, 8, '驱动层\n三个仪器类库', '#F8961E', fontsize=11.5)
link(14.5, 51, 15.5, 54, '#F8961E')
drv = [
    ('OscilloscopeClassLibrary\n示波器 · SCPI 文本协议', 60.5),
    ('SingalClassLibrary\n信号源 · SCPI（只发不收）', 54),
    ('ValueSourceN2410ClassLibrary\nN2410/N36200 电压源 · Modbus-RTU', 47.5),
]
for t, y in drv:
    node(69, y, 32, 5.6, t, '#F9C74F', fontsize=9.5, tc='#333333')
    link(34.5, 54, 53, y, '#F9C74F')

# ========== 分支3：核心数据流（示波器取数链，纵向流水线） ==========
node(25, 25, 19, 8, '核心数据流\n示波器取数链', '#43AA8B', fontsize=11.5)
link(14.5, 51, 15.5, 25, '#43AA8B')
flow = [
    '① 1秒定时器\n按 CHANNum 位掩码选通道',
    '② 发 :XINCrement?\n状态机 ExpectQuery=1',
    '③ 收到增量 → 问 :XORigin?\nExpectQuery=2',
    '④ 收到原点 → 问 :DATA?\n仪器回1000个ASCII电压值',
    '⑤ 接收线程解析\n回调 SomeActMethod（主程序）',
    '⑥ 真实时间=原点+序号×增量\n刷新图表 + 写入Excel缓存',
]
prev_y = None
for i, t in enumerate(flow):
    y = 43.5 - i * 7.2
    node(73, y, 34, 5.9, t, '#90BE6D', fontsize=9.5)
    link(34.5, 25, 56, y, '#90BE6D')
    if prev_y is not None:
        link(73, prev_y - 3.0, 73, y + 3.0, '#2F855A', rad=0, arrow=True)
    prev_y = y

# ========== 分支4：支撑机制（左下带，放在根下方） ==========
node(25, 7.5, 19, 7.5, '支撑机制\n（贯穿全局）', '#577590', fontsize=11)
link(14.5, 47, 15.5, 7.5, '#577590')
sup = [
    ('委托回调 ×2\nreciveState：日志/状态栏\nthisNetPublicTool：波形/电压电流', 12),
    ('日志系统\nexe目录\\Logs\\log_日期.txt\n记录连接与异常', 3.5),
]
for t, y in sup:
    node(64, y, 27, 6.4, t, '#4D908E', fontsize=9)
    link(34.5, 7.5, 50.5, y, '#4D908E')

# ========== 底部图例 ==========
ax.text(50, -3.5,
        '协议对比：示波器/信号源 = SCPI 文本指令（问句带 ?，回答为 ASCII）   |   电压源 = Modbus-RTU 二进制帧（地址+功能码+寄存器+CRC16 校验）',
        ha='center', fontsize=10.5, color='#333333',
        bbox=dict(boxstyle='round,pad=0.55', fc='#FFE8D6', ec='#CB997E'))

fig.savefig(r'D:\孙博煜\半导体器件相关\器件检测代码\器件控制系统\代码注释与逻辑分析\代码逻辑思维导图.png',
            bbox_inches='tight', dpi=160)
print('OK')
