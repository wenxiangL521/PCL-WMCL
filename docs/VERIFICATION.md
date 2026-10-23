# 验证状态

更新：2026-10-23。本文只保留当前摘要；详细历史证据在仓库外归档，不随仓库发布。

## 自动测试

- 全解决方案：**1844 通过**，0 失败/跳过。
- App **866**、Core **29**、Launch **125**、Minecraft **824**。
- 命令：`dotnet test WMCL.slnx -c Release`。
- 最近 TRX：`../wmcl-validation-tmp/priority4/news-click/`。
- Release 构建：**0 warning / 0 error**；`dotnet format whitespace --verify-no-changes` 与 `git diff --check` 通过。

## 原生静态/交互证据

| 项目 | 证据 |
|---|---|
| Cleanroom 真实启动 | `../wmcl-validation-tmp/cleanroom-real-launch6.png`（1.12.2 主菜单，人工终止） |
| 启动页新闻 | `../wmcl-validation-tmp/home-news-final.png`（自动加载、带图、整卡点击） |
| 侧栏图标 | `../wmcl-validation-tmp/download-icons.png`、`settings-icons.png` |
| Windows exe | `dist/WMCL.exe`，PE32+ GUI x64，48 MB，内嵌 6 尺寸图标 |

## 已验证范围

- 原版安装/校验/修复、Java 自动下载、CLI/GUI 启动。
- Fabric/Forge/NeoForge/OptiFine/Cleanroom 安装与部分真实启动。
- Modrinth/CurseForge 搜索/文件/依赖（CurseForge 需 Key）。
- 本地 Mod、资源包、光影、存档、截图、实例克隆/重命名/删除。
- 整合包 `.mrpack` 导入/导出、实例 zip 导入/导出。
- 微软设备码/刷新（mock）、Authlib/Nide、皮肤/披风基础操作。
- EasyTier/陶瓦流程（受 TUN/中继条件限制）。
- Windows 单文件 exe 构建；Linux tar.gz/AppImage/deb（历史验证）。

## 未完成/不可宣称

- Windows/macOS 实机运行、签名、SmartScreen 体验；exe 未签名。
- 真实 Azure OAuth、真实 CurseForge Key 的完整 E2E。
- 高 DPI、跨平台组合矩阵、原生逐帧动画。
- 进入世界、声音、输入注入、长时间运行；Cleanroom 只到主菜单。
- 资源详情每行保存/默认目录、任务逐任务日志/持久化、Windows/macOS 自动更新。
- 所有下载入口的 HTTP 取消/内部进度覆盖。
- PCL2/PCL-CE 方块素材公开分发的版权确认。

## 证据分类

Headless、静态截图、原生交互、动画录像、公共服务烟测、真实 Minecraft E2E 是不同证据层级，不能相互替代。提交 UI 改动时必须在 PR/提交说明中注明证据类型。
