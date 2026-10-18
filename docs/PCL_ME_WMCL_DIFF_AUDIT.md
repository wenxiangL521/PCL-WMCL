# PCL-ME / WMCL 差异化审计

日期：2026-10-19

## 0. 结论

PCL-ME 与 WMCL 同为 **.NET 10 + Avalonia**，技术栈接近，适合直接对照页面与交互实现。PCL-ME 的页面组织是按 **RightPane + Section** 拆分，WMCL 是 **View + ViewModel**；两者不需要复制代码，但需要对齐功能边界和小交互。

当前已修的代表性缺陷：

- 下载页版本选择原来把 919 个官方版本平铺在一个下拉/列表里；
- 现在按 **最新版本 / 正式版 / 快照 / 旧版本 / 愚人节版本** 分组，并保留搜索；
- 另外补了实例截图页和设置中的游戏管理页。

## 1. 页面对照

状态：✅ 已有；🟡 部分；❌ 缺失。

### 下载

| PCL-ME | WMCL | 状态 | 差异 |
|---|---|---|---|
| DownloadCatalogRightPaneView | 无独立页 | ❌ | PCL-ME 有下载目录/分类页 |
| DownloadInstallRightPaneView | InstallView | 🟡 | WMCL 有 5 类组件卡 + Fabric API / OptiFabric 开关，PCL-ME 组件蓝图有 13 类 |
| DownloadFavoritesRightPaneView | 组件卡收藏状态 + 摘要 | 🟡 | 有持久化收藏星标和摘要；没有独立收藏页/资源收藏 |
| DownloadResourceRightPaneView | InstanceContent/Mods | 🟡 | 资源/整合包/Mod 分散在多个页 |
| DownloadClient | 无独立页 | ❌ | 没有独立客户端下载页 |

### 实例

| PCL-ME | WMCL | 状态 | 差异 |
|---|---|---|---|
| InstanceSelectRightPaneView | InstanceView/VersionSelect | ✅ | 版本选择入口 |
| InstanceOverviewRightPaneView | InstanceView 概览 | ✅ | 图标/分类/描述/收藏 |
| InstanceInstallRightPaneView | 实例→安装 | ✅ | 资源/整合包安装 |
| InstanceSetupRightPaneView | 实例设置 | ✅ | 内存/Java/窗口/服务器 |
| InstanceExportRightPaneView | 实例导出 | ✅ | 导出选项 |
| InstanceResourceRightPaneView | InstanceContent | ✅ | 资源/光影/存档 |
| InstanceScreenshotRightPaneView | ScreenshotWindow | ✅ | **本轮新增** |
| InstanceWorldRightPaneView | InstanceContent | 🟡 | 存档列表/备份已有 |
| InstanceServerRightPaneView | InstanceView 服务器 | ✅ | 列表/Ping/控制台 |
| VersionSave* | 无独立页 | ❌ | 没有版本存档/数据包独立管理 |
| Jar-in-Jar | 无 | ❌ | 没有嵌套 Jar 管理 |

### 启动/登录/设置/工具

| PCL-ME | WMCL | 状态 | 差异 |
|---|---|---|---|
| Launch / LaunchLeftPanel | LaunchView | 🟡 | 账号/皮肤并入启动页，没有独立 Profile |
| Login prompts | 登录对话框 | ✅ | 微软/Authlib/离线 |
| SetupLaunch | 设置→启动 | ✅ | 内存/Java/命令/隔离 |
| SetupJava | 设置→启动内 | 🟡 | Java 管理没有独立页 |
| SetupGameManage | 设置→游戏管理 | 🟡 | 新增版本管理；下载线程/限速已在设置中，PCL-ME 的版本列表源未补 |
| SetupLog | LogsView | ✅ | 独立日志列表/打开/删除/清理 |
| SetupUpdate | UpdateView | ✅ | 独立更新检查/下载/安装页 |
| SetupLauncherMisc | 设置→其他 | 🟡 | 部分 |
| SetupUi | 设置→个性化 | ✅ | 主题/背景/Logo |
| SetupAbout/Feedback | About/MorePage | 🟡 | 反馈仍是说明页 |
| ToolsHelp | HelpView | 🟡 | 有搜索/详情，内容规模不同 |
| ToolsTest | Toolbox 自定义下载 | 🟡 | 已有自定义 URL 下载；没有头像/成就/皮肤预览等工具 |
| TaskManager | TaskManagerWindow | 🟡 | 有任务窗口，不是完整任务树 |

## 2. 小功能缺陷清单

这些是“不是缺整页，但用起来不对”的点：

### 已修

1. **版本选择平铺**
   - 原：919 个官方版本混在一列；
   - 现：树形分组，最新组展开，其余组可展开；
   - 搜索后保持分组；
   - 已安装标记保留。

2. **实例截图**
   - 原：实例左栏没有截图；
   - 现：截图窗口，列表/预览/打开/删除。

3. **游戏管理**
   - 原：只能逐个实例操作；
   - 现：设置→游戏管理，列出已安装版本，支持修复/删除/打开目录。

### 未修

4. **组件收藏**：已加组件卡收藏星标并持久化；PCL-ME 的独立 DownloadFavorites 资源收藏页仍没有。
5. **组件蓝图**：PCL-ME 有 Forge/Cleanroom/NeoForge/Fabric/LegacyFabric/Fabric API/Legacy Fabric API/Quilt/QFAPI/LabyMod/OptiFine/OptiFabric/LiteLoader；WMCL 只有 5 类。
6. **实例截图 hover 动画**：PCL-ME 有缩放/遮罩/标题过渡，WMCL 是静态列表+预览。
7. **版本存档/数据包**：PCL-ME 有独立 VersionSave 页面；WMCL 只在存档备份/内容页零散支持。
8. **ToolsTest**：已有自定义 URL 下载，仍没有头像/成就/皮肤预览、完整测试页。
9. **SetupLog/SetupUpdate/SetupJava**：没有独立页。
10. **主页新闻/欢迎引导**：无。
11. **下载目录页/客户端页**：无。
12. **任务树**：TaskManagerWindow 只有简化任务列表。

## 3. 逻辑对照

| 逻辑 | PCL-ME | WMCL | 状态 |
|---|---|---|---|
| 版本目录分组 | 最新/正式/预览/旧版/愚人节 | 已实现树形分组 | ✅ |
| 下载组件组合 | 13 类组件、合并安装 | 5 类、基础加载器 + OptiFine | 🟡 |
| 组件互斥自动清理 | 有 | 有兼容校验，不完全自动清理 | 🟡 |
| 版本状态 | 安装后刷新 | IsInstalled + InstallCompleted | ✅ |
| 截图 | 列表+预览+hover | 列表+预览 | 🟡 |
| 游戏管理 | 下载源/线程/限速/版本 | 修复/删除/目录 | 🟡 |
| 动画 | MotionDurations/Motion/SelectionBar | 已借时长、入场、展开、modal | 🟡 |
| 版本选择 | 独立页/卡组 | 搜索树弹窗 | 🟡 |

## 4. 优先级

### P0

1. 组件收藏；
2. LiteLoader / LegacyFabric / Cleanroom / LabyMod（Fabric API、OptiFabric 已补）；
3. 版本存档/数据包独立页；
4. 任务树完善。

### P1

5. ToolsTest；
6. SetupJava 独立页；
7. 主页新闻/欢迎引导；
8. 截图 hover 动画；
9. 下载目录/客户端页。

### P2

10. 独立 Profile/皮肤页；
11. 完整多语言；
12. 反馈/投票真实入口。

## 5. 本轮实现

- `VersionPickerWindow`：版本树分组 + 搜索 + 最新组 + 正式版/快照/旧版/愚人节；
- `ScreenshotWindow`：实例截图；
- `GameManageViewModel` / `GameManageView`：设置→游戏管理；
- `MainWindowViewModel`：新增 `settings-games` 路由。

## 6. 验证与限制

- 更新页通过 `--page settings-update` 启动验证，截图：`docs/screenshots/wmcl-update.png`。

- Fabric API 组合安装实测：`1.21.1 + Fabric 0.19.5 + Fabric API`，实例 mods 目录生成 `fabric-api-0.116.17+1.21.1.jar`。
- OptiFabric 已接入 Fabric + OptiFine 组合，但没有 1.20.4 及以下实例做实际安装验证。

- 日志页通过 `--page settings-log` 启动验证，截图：`docs/screenshots/wmcl-logs.png`。

- 已通过临时自动打开验证版本选择器截图（见 `docs/screenshots/wmcl-version-picker.png`）；
- 已通过临时自动打开验证截图窗口空状态（见 `docs/screenshots/wmcl-instance-screenshots.png`）；
- 游戏管理页通过 `--page settings-games` 启动验证；截图见 `docs/screenshots/wmcl-game-manage.png`；
- 未运行 PCL-ME 做逐页视觉对比；
- 没有真实鼠标点击截图/删除/修复；
- 组件收藏、Jar-in-Jar、ToolsTest 等仍未实现。

## 7. 下一步

先补 **组件收藏** 和 **Fabric API / OptiFabric / LiteLoader**，再做 **VersionSave** 和 **ToolsTest**。
