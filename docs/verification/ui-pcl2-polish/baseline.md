# 二期基线记录（组 0.1）

- 日期：2026-10-30
- 基线用例总数：**2085**（App 1018 / Core 78 / Launch 155 / Minecraft 834），0 失败
- 说明：tasks.md 记为 2084，实测多 1 项（`PageScreenshotGenerator` 用例已入库工作区），以 2085 为唯一基线口径。
- 未跟踪文件（本期首次提交须显式 `git add`）：
  - `tests/WMCL.App.Tests/PageScreenshotGenerator.cs`
  - `docs/verification/`（含一期 44 张截图）
- 构建：Release 0 warning / 0 error；format 与 `git diff --check` 通过。