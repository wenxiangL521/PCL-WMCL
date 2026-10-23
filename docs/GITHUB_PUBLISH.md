# GitHub 发布信息

> 非法律意见；公开发布前请再次核对 PCL2 指南与素材来源。

## 仓库字段

- **Name**: `PCL-WMCL`
- **Description**: `Plain Craft Launcher (PCL) 的第三方跨平台重写 · .NET 10 + Avalonia · Windows / Linux / macOS`
- **Website**: 留空或仓库地址
- **Visibility**: Public
- **License**: **Other**（自有代码 MIT + PCL2 指南 + 第三方声明，不是单一 MIT）
- **Default branch**: `main`

## Topics

```text
minecraft
minecraft-launcher
pcl
avalonia
dotnet
csharp
cross-platform
windows
linux
macos
```

## 发布前检查

- [ ] 显示名为 `Plain Craft Launcher (PCL) WMCL`，不使用 `PCL 3` / `PCL Pro` 等易混淆名称。
- [ ] About 首位保留龙腾猫跃署名、赞助链接、正版购买入口。
- [ ] 保留 `LICENSE.md`、`PCL-LICENCE.md`、`THIRD_PARTY_NOTICES.md`。
- [ ] 不使用 PCL 官方图标；当前应用图标为 `PCL WMCL` 第三方变体。
- [ ] `dist/` 不提交，exe 只作为 Release asset。
- [ ] 不宣称与 PCL 官方等价或完整还原。

## Release

**Tag**: `v0.1.1`

**Title**: `Plain Craft Launcher (PCL) WMCL v0.1.1 — Windows 主题合规修复`

**Asset**: `WMCL.exe`（Windows x64，self-contained single-file，约 48 MB）

**Notes**:

```markdown
首个公开预览版本。

## 下载
- Windows x64：`WMCL.exe`
  - 自包含单文件，无需安装 .NET
  - PE32+ GUI，不弹控制台
  - 未签名，首次运行可能出现 SmartScreen 提示

SHA256:
fb68e51838aeeb627bd93a826964b20e483690af11805580cac33f8b7742e609

## 已知限制
- 第三方非官方重写，不代表 PCL 官方。
- Windows/macOS 自动更新尚未实机验证。
- 真实 OAuth、CurseForge、高 DPI、跨平台组合矩阵仍未完整验收。
- 请购买 Minecraft 正版。
```

## 版本与哈希

- 版本：`0.1.1`
- 当前 exe SHA256：`fb68e51838aeeb627bd93a826964b20e483690af11805580cac33f8b7742e609`
- 重新打包后必须同步更新 Release notes；不要沿用旧哈希。

## 命名与许可

PCL2 指南要求重度使用者的名称以 `Plain Craft Launcher (PCL)` 开头，并加第三方后缀。本项目使用 `Plain Craft Launcher (PCL) WMCL`；`WMCL` 为 wenxiangL Minecraft Launcher 的缩写。

GitHub 仓库整体选 **Other**；自有代码的 MIT 声明在 `LICENSE.md`，PCL2 指南在 `PCL-LICENCE.md`，第三方组件在 `THIRD_PARTY_NOTICES.md`。
