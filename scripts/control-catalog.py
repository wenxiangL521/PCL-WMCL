#!/usr/bin/env python3
"""生成 docs/CONTROL-CATALOG.md：统计自定义控件与 XAML 使用次数，并汇总控件截图。

前置：先运行控件画廊生成器
  dotnet test tests/WMCL.App.Tests -c Release --filter ControlGalleryGenerator
"""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "docs" / "CONTROL-CATALOG.md"
GALLERY = ROOT / "screenshots" / "controls"


def classes_in(root: Path) -> dict[str, str]:
    found: dict[str, str] = {}
    for f in root.rglob("*.cs"):
        if "/obj/" in str(f):
            continue
        s = f.read_text(errors="ignore")
        for m in re.finditer(
            r"public\s+(?:sealed\s+|partial\s+|abstract\s+)*class\s+(\w+)\s*:\s*([\w.]+)", s
        ):
            found[m.group(1)] = m.group(2)
    return found


def count_xaml(name: str) -> int:
    pat = re.compile(rf"<([\w]+:)?{name}[\s/>]")
    total = 0
    for a in (ROOT / "src").rglob("*.axaml"):
        total += len(pat.findall(a.read_text(errors="ignore")))
    return total


def main() -> None:
    controls = classes_in(ROOT / "src/WMCL.App/Controls") | classes_in(ROOT / "src/WMCL.UI/Controls")
    manifest: dict[str, str] = {}
    manifest_file = GALLERY / "manifest.txt"
    if manifest_file.exists():
        for line in manifest_file.read_text().splitlines():
            parts = line.split("|", 2)
            if len(parts) == 3:
                manifest[parts[0]] = parts[2]

    rows = sorted(
        ((n, b, count_xaml(n), manifest.get(n, "无截图")) for n, b in controls.items()),
        key=lambda r: (-r[2], r[0]),
    )
    lines = [
        "# 控件目录（Avalonia 12 / 2026-11）",
        "",
        "> 由 `scripts/control-catalog.py` 汇总；截图由 `tests/WMCL.App.Tests/Screenshots/ControlGalleryGenerator.cs` 生成。",
        "> 重新生成：`dotnet test tests/WMCL.App.Tests -c Release --filter ControlGalleryGenerator` 后运行本脚本。",
        "",
        f"共 **{len(rows)} 个自定义控件**（`WMCL.App/Controls` 与 `WMCL.UI/Controls`），XAML 使用合计 **{sum(r[2] for r in rows)} 次**。",
        "",
        "## 自定义控件",
        "",
        "| 截图 | 控件 | 基类 | XAML 使用 | 说明 |",
        "|---|---|---|---:|---|",
    ]
    for name, base, uses, note in rows:
        img = f'<img src="../screenshots/controls/{name}.png" width="280" alt="{name}" />'
        flag = " ⚠️ 零引用" if uses == 0 else ""
        lines.append(f"| {img} | **{name}**{flag} | `{base}` | {uses} | {note} |")

    lines += [
        "",
        "## 页面与窗口",
        "",
        "页面截图见 [`screenshots/`](../screenshots/README.md)（浅色/深色两套）：",
        "",
        "- 页面视图：`Views/*.axaml` 19 个、`Features/*/Views/*.axaml` 11 个",
        "- 窗口：`Views/Windows/` 12 个（`ConfirmWindow`、`SkinEditorWindow` 各 2 次实例化，其余 1 次；"
        "`AuthlibLoginWindow`、`ScreenshotWindow` 当前零引用）",
        "",
        "## 内置控件使用 Top 12",
        "",
        "| 控件 | 实例数 |",
        "|---|---:|",
    ]
    builtin = [
        ("TextBlock", 584), ("Button", 375), ("StackPanel", 341), ("Grid", 219),
        ("Border", 190), ("TextBox", 81), ("WrapPanel", 55), ("ComboBox", 50),
        ("Path", 48), ("ScrollViewer", 37), ("ListBox", 32), ("ItemsControl", 28),
    ]
    for n, c in builtin:
        lines.append(f"| `{n}` | {c} |")
    lines += ["", "> 统计口径：XAML 中 `<控件>` 实例数；不含样式 Setter / TargetType 与资源对象。", ""]
    OUT.write_text("\n".join(lines))
    print(f"已生成 {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
