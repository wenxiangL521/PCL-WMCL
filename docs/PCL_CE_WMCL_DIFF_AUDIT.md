# PCL-CE / WMCL 差异化审计

日期：2026-10-19

## 0. 结论

PCL-CE 是 **WPF + `net10.0-windows`** 的 Windows 启动器；WMCL 是 **Avalonia + `net10.0`** 的跨平台重写。两者不能直接复用 UI/动画代码，只能对照页面结构、交互逻辑和功能边界。

当前 WMCL 已经覆盖启动、下载、实例、Mod、整合包、资源包/光影/存档、联机替代、设置、Java、登录、更新、任务等主链路；主要缺口集中在：

1. PCL-CE 的**组件卡种类**：Fabric API、OptiFabric、LiteLoader、LegacyFabric、Cleanroom、LabyMod；
2. 下载页的**组件收藏**；
3. 实例页的**截图、Jar-in-Jar、禁用 Mod 独立页**；
4. 启动器级页面：**主页新闻、更新/日志/语言/游戏管理独立页、游戏测试**；
5. 部分页面仍是**说明页**：反馈、投票；
6. 版本选择仍是弹窗，不是 PCL-CE 的独立版本选择页。

本文只记录可核对的项目；没有运行 PCL-CE（Windows-only）做逐页视觉对比。

## 1. 审计依据

PCL-CE 本地源码：

- `Plain Craft Launcher 2/Pages/PageDownload/*`
- `Plain Craft Launcher 2/Pages/PageInstance/*`
- `Plain Craft Launcher 2/Pages/PageLaunch/*`
- `Plain Craft Launcher 2/Pages/PageSetup/*`
- `Plain Craft Launcher 2/Pages/PageTools/*`
- `Plain Craft Launcher 2/Pages/PageSpeedLeft/Right.xaml`
- `Plain Craft Launcher 2/Pages/PageHomepage/*`
- `Plain Craft Launcher 2/Pages/PageHelpDetail.xaml`

WMCL 当前源码：

- `src/WMCL.App/Views/`
- `src/WMCL.App/ViewModels/`
- `src/WMCL.App/Services/`
- `README.md` 的功能验证记录

## 2. 页面矩阵

状态：✅ 已有且可用；🟡 部分实现；❌ 缺失或仅说明。

| 功能域 | PCL-CE 页面 | WMCL 对应 | 状态 | 差异 |
|---|---|---|---|---|
| 启动页 | PageLaunchLeft/Right | LaunchView | ✅ | WMCL 账号页并入启动页，没有独立 Profile 页 |
| 登录 | PageLoginMs/Auth/Offline/Profile | 登录对话框 + LaunchView | 🟡 | 有微软/Authlib/离线/皮肤上传，但没有独立 Profile 管理页 |
| 版本选择 | PageSelectLeft/Right | VersionSelectView + 下载页内目录阶段 | 🟡 | 启动版本选择是独立页；下载页按 PCL2 是页内选择阶段 |
| 下载：原版安装 | PageDownloadInstall | InstallView | 🟡 | 有 Minecraft/Fabric/Forge/NeoForge/OptiFine + Fabric API / OptiFabric；缺 LiteLoader/Cleanroom/LabyMod/LegacyFabric |
| 下载：组件收藏 | PageDownloadCompFavorites | 组件卡收藏星标 + 收藏摘要 | 🟡 | 有收藏状态/摘要；没有独立收藏/排序页 |
| 下载：客户端 | PageDownloadClient | 无 | ❌ | 没有 PCL-CE 的独立客户端下载页 |
| 下载：Mod | PageInstance/Mod | ModsView | ✅ | 搜索、安装、启停、删除、更新 |
| 下载：整合包 | PageInstance/Install、PageDownload | ModpackView | ✅ | Modrinth、本地 `.mrpack`、CurseForge、导出 |
| 下载：资源包/光影/存档 | PageInstance/CompResource、Saves | InstanceContent | ✅ | 列表、导入、删除、存档备份、Modrinth/CurseForge |
| 下载：数据包 | PageInstance/CompResource | InstanceContent | 🟡 | 有数据包模式和世界选择，但没有独立数据包页 |
| 实例：概览 | PageInstanceOverall | InstanceView 概览 | ✅ | 图标、分类、描述、收藏、隔离 |
| 实例：设置 | PageInstanceSetup | InstanceView 设置 | ✅ | 内存/Java/窗口/服务器/隔离 |
| 实例：服务器 | PageInstanceServer | InstanceView 服务器 | ✅ | 列表、Ping、MOTD、导入导出、服务端文件/控制台 |
| 实例：导出 | PageInstanceExport | 导出与操作 | ✅ | 导出选项、实例目录 |
| 实例：安装 | PageInstanceInstall | 实例→安装 | ✅ | 安装资源/整合包 |
| 实例：Mod | PageInstanceMod | ModsView | ✅ | 本地 Mod 启停/删除/更新 |
| 实例：禁用 Mod | PageInstanceModDisabled | ModsView 内部 | 🟡 | 没有独立“已禁用 Mod”页 |
| 实例：截图 | PageInstanceScreenshot | **本轮新增 ScreenshotWindow** | ✅ | 列表/预览/打开/删除/打开目录 |
| 实例：Jar-in-Jar | PageInstanceCompJarInJar | 无 | ❌ | 没有嵌套 Jar 管理 |
| 实例：组件资源 | PageInstanceCompResource | InstanceContent | ✅ | 资源包/光影/存档 |
| 主页新闻 | PageHomepageNewsView | 无 | ❌ | 没有主页新闻/公告 |
| 设置：启动 | PageSetupLaunch | SettingsView 启动设置 | ✅ | 内存、Java、命令、隔离等 |
| 设置：Java | PageSetupJava | JavaView | ✅ | 独立 Java 扫描/选择/安装页 |
| 设置：游戏管理 | PageSetupGameManage | GameManageView | 🟡 | 已补版本列表/修复/删除/目录；PCL-CE 的下载源/线程/限速分散在设置中 |
| 设置：联机 | PageSetupGameLink | LinkSettings | 🟡 | EasyTier / Terracotta 替代，不是 PCL2 官方房间服务 |
| 设置：UI | PageSetupUI | SettingsView 个性化 | ✅ | 主题、背景、Logo、主页 |
| 设置：语言 | PageSetupLauncherLanguage | 无 | ❌ | 仅中文 UI |
| 设置：其他 | PageSetupLauncherMisc | SettingsView 其他 | 🟡 | 更新源/代理/API 有，杂项未一一对应 |
| 设置：更新 | PageSetupUpdate | UpdateView | ✅ | 独立检查/下载/安装页 |
| 设置：日志 | PageSetupLog | LogsView | ✅ | 已补独立日志管理页 |
| 关于/反馈 | PageSetupAbout/Feedback | AboutView/MorePage | 🟡 | 反馈仍是说明页，链接原版仓库 |
| 帮助详情 | PageHelpDetail | HelpView | 🟡 | 有搜索/详情，但内容规模不同 |
| 工具：联机 | PageToolsGameLink | LinkView | 🟡 | 手动 EasyTier/Terracotta |
| 工具：测试 | PageToolsTest | Toolbox 自定义下载 + 服务器查询 | 🟡 | 已有 URL 下载、服务器 Ping；没有完整测试工具集 |
| 百宝箱 | PageToolsLeft（未开源部分） | ToolboxView | 🟡 | 仅目录入口，README 已声明不含官方百宝箱 |
| 任务管理 | PageSpeedLeft/Right | TaskManagerWindow | 🟡 | 有任务窗口，但不是 PCL2 的完整任务树/权重/球体体系 |
| 新功能投票 | 外部链接 | MorePage | 🟡 | 说明页，不是投票系统 |

## 3. 功能逻辑矩阵

| 逻辑 | PCL-CE | WMCL | 结论 |
|---|---|---|---|
| 原版下载 | Mojang manifest + 镜像 | 官方 manifest only；官方已安装标“已安装” | ✅ 本轮修正 |
| 版本选择 | 独立 PageSelect | 文件夹页 + 下载弹窗 | 🟡 下载弹窗不是原版独立页 |
| 组件安装 | 一个合并请求，按组件组合合并 JSON/libraries/mods | `InstallCombined` 合并实例；支持 Minecraft + 一个基础加载器 + OptiFine | 🟡 缺 Fabric API/OptiFabric/LiteLoader/Cleanroom/LabyMod |
| 组件互斥 | 实时互斥/自动清除不兼容选项 | 依赖 `LoaderCompatibility` 校验；UI 选择卡片切换，不自动清 OptiFine 等 | 🟡 需要补自动清理矩阵 |
| 实例数量 | 合并成一个实例 | 已做 MergedInstanceBuilder；实际 Fabric/Forge/NeoForge/OptiFine 冒烟成功 | ✅ |
| 组件收藏 | PageDownloadCompFavorites | 无 | ❌ |
| 下载任务 | Task tree、权重、任务栏 | TaskManagerWindow + 页面进度 | 🟡 |
| 版本官方过滤 | 下载列表只含官方版本 | 已修：本地自定义不再混入 | ✅ |
| 安装后状态 | 刷新实例/下载状态 | IsInstalled + InstallCompleted 刷新 | ✅ |
| 加载器列表并发 | 请求生命周期管理 | 增加 Fabric/Forge/OptiFine 请求序号 | ✅ 本轮修复 |
| NeoForge 库下载 | 镜像/失败重试 | 5 次重试 + 官方 Maven 兜底 | ✅ |
| 登录/皮肤 | 独立 Profile / 皮肤页 | LaunchView 皮肤上传；无独立 Profile | 🟡 |
| 实例截图 | 页面 | 本轮新增截图窗口 | ✅ |
| Jar-in-Jar | 页面 | 无 | ❌ |
| 主页新闻 | 页面 | 无 | ❌ |
| 更新 | 独立页面 | 设置更新源 + 自动更新 | 🟡 |
| 语言 | 多语言资源 | 中文 | ❌ |
| 反馈/投票 | 外部/页面 | 说明页 | 🟡 |

## 4. 优先级

### P0：影响主流程

1. **组件组合补齐**：Fabric API、OptiFabric、LiteLoader；同时补齐互斥/自动清理。
2. **组件收藏**：PCL-CE 下载页有独立收藏组件入口。
3. **启动器级任务管理**：任务树/权重/失败重试，而不是页面进度条。
4. **实例 Jar-in-Jar**：缺失会导致部分整合包/资源包依赖不可见。

### P1：页面完整性

5. 主页新闻；
6. 设置中的游戏管理、日志、语言、独立更新页；
7. 游戏测试/诊断页；
8. 独立 Profile/皮肤页；
9. 禁用 Mod 独立页；
10. 截图（本轮已完成）。

### P2：细节

11. 版本选择改成 PCL-CE 独立页，而不是下载弹窗；
12. 本地化；
13. 反馈/投票真实入口；
14. PCL-ME 的列表交错/选择条动画。

## 5. 本轮已补

### 实例截图

新增 `ScreenshotWindow`：

- 读取实例目录下 `screenshots/`；
- 支持 png/jpg/jpeg/bmp/webp；
- 左侧列表、右侧预览；
- 打开截图目录、打开图片、删除、刷新；
- 实例左栏新增“截图”入口。

文件：

- `src/WMCL.App/Views/ScreenshotWindow.axaml`
- `src/WMCL.App/Views/ScreenshotWindow.axaml.cs`
- `src/WMCL.App/Views/InstanceView.axaml`
- `src/WMCL.App/Views/InstanceView.axaml.cs`

## 6. 验证与限制

- 本次审计是源码/页面级审计，没有运行 Windows-only 的 PCL-CE 做逐页对照。
- 截图页已通过临时自动打开验证：窗口、空状态、路径和按钮正常；当前实例没有截图，因此没有文件预览/删除的 E2E。
- 既有构建/测试记录：142 个测试通过；截图页新增后需重新跑构建测试。

## 7. 下一步

优先做 **组件收藏 + Fabric API/OptiFabric/LiteLoader 组件**，再做 **Jar-in-Jar** 和 **主页新闻**。
