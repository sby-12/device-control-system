# 提取三份通讯手册的文本为 md
import sys
from pathlib import Path

sys.path.insert(0, str(Path(sys.executable).parent.parent.parent))  # managed runtime

from pypdf import PdfReader

base = Path(r"D:/孙博煜/半导体器件相关/器件检测代码/器件控制系统/新设备通讯手册")

files = [
    ("maui-remote-control-and-automation-manual.pdf", "MAUI远程控制手册.md"),
    ("SDG6000X_ProgrammingGuide_CN03A.pdf", "SDG6000X信号源编程手册.md"),
    ("IT16000D Programming Guide-CN.pdf", "IT16000D电源编程手册.md"),
]

for pdf_name, md_name in files:
    pdf_path = base / pdf_name
    reader = PdfReader(str(pdf_path))
    n = len(reader.pages)
    out = [f"# {pdf_name} 文本提取\n", f"（共 {n} 页，自动提取，公式/表格可能有排版损失，以原PDF为准）\n"]
    for i, page in enumerate(reader.pages):
        try:
            text = page.extract_text() or ""
        except Exception as ex:
            text = f"[第{i+1}页提取失败: {ex}]"
        out.append(f"\n\n----- 第 {i+1} 页 -----\n\n{text}")
    md_path = base / md_name
    md_path.write_text("".join(out), encoding="utf-8")
    print(f"{pdf_name}: {n} 页 -> {md_path.name}, {md_path.stat().st_size} 字节")
