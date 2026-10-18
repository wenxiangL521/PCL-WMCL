# PCL2 全功能与实现逻辑审计

> 基线：`Hex-Dragon/PCL2` @ `ade6e04`（2.14.0.0，VB.NET + WPF + .NET Framework 4.8）
> 方法：按模块、页面、关键链路逐项阅读源码；不把“未在开源版中实现/被删减”的官方功能当成已知逻辑。
> 结论用于校正本项目（PCL Linux 重写）的功能模型，不代表对官方闭源版本的完整还原。

---

## 0. 先说结论：之前的实现错在哪

### 0.1 PCL2 的“安装新版本 + 加载器”是**一次合并安装**，不是逐个安装

PCL2 安装页的选择结果会组成一个 `McInstallRequest`，核心字段包括：

- `NewInstanceName`、`InstanceFolder`
- `Components`（Minecraft 版本 + `ModLoaderVersions` 字典 + OptiFine/Fabric API/OptiFabric 信息）
- `MinecraftJson`
- `ForgeEntry`、`NeoForgeEntry`、`LiteLoaderEntry`
- `FabricApi`、`OptiFabric`

`McInstallLoadersGet()` 会把原版、Forge、NeoForge、Fabric、LiteLoader、OptiFine、Fabric API、OptiFabric 的下载与安装组织成**一个任务链**，最后：

1. 将各加载器的版本 JSON 按顺序合并成**同一个版本 JSON**；
2. 把各加载器的 libraries 合并到同一 `libraries`；
3. 把 Fabric API / OptiFabric / 作为 Mod 安装的 OptiFine 放入同一实例的 `mods`；
4. 只生成**一个实例目录**。

所以 PCL2 的模型是：

```text
Minecraft 1.20.1 + Forge 47.3.0 + OptiFine I6
        ↓ 合并
versions/MyPack/MyPack.json  （一个版本）
```

而不是：

```text
versions/1.20.1
versions/1.20.1-forge-47.3.0
versions/1.20.1-OptiFine_HD_U_I6
```

我们之前的实现是后者：每个加载器独立创建实例；组合安装也只支持“选一个加载器”。这既不符合 PCL2，也无法正确处理 Forge+OptiFine、Fabric+OptiFabric 等组合。

### 0.2 PCL2 有完整的加载器冲突判定，我们之前没有

PCL2 不是“把卡片都摆出来让用户乱选”，而是在 `ReloadSelected()` 和各个 `Load*GetError()` 中做实时互斥：

- Forge ↔ NeoForge ↔ Fabric 基本互斥；
- NeoForge 与 OptiFine 互斥；
- Forge 与 OptiFine 只在 OptiFine 标注的精确 Forge 版本下兼容；
- Fabric 与 OptiFine 只在 1.20.4 及以下、且安装 OptiFabric 时兼容；
- Fabric API 只在 Fabric 下出现；
- OptiFabric 只在 Fabric+OptiFine 下出现；
- 选择某个组件时，会自动清除与新选择不兼容的旧选择。

详见第 4 节矩阵。

### 0.3 PCL2 有全局任务系统和右下角进度球

PCL2 的下载不是“页面内一个进度条”：

- `LoaderTask` / `LoaderCombo` / `LoaderWorker` 构成任务树；
- 每个任务有 `ProgressWeight`、`Show`、`Block`；
- `LoaderTaskbarAdd()` 把顶层任务加入任务栏；
- `BtnExtraDownload` 是右下角额外按钮，只有存在下载任务时显示；
- 点击进入 `PageSpeedLeft/PageSpeedRight` 任务管理页，显示总进度、速度、剩余文件/线程和子任务；
- 页面内进度只是任务的一个视图。

我们之前只有页面内进度条，没有全局任务球和任务管理页；后来补了球，但没有任务列表/取消/权重体系。

### 0.4 PCL2 的页面切换不是统一横向滑动

PCL2 `PageChangeAnim()` 的实际行为：

- 新页面先以 `Opacity=0` 插入，延迟约 30ms 后触发 `PageOnEnter()` 并淡入；
- 旧页面 `PageOnExit()` / `PageOnForceExit()`；
- 左侧栏宽度变化用 180ms 动画；
- 标题栏进入子页面时用约 150/350ms 的淡入+位移；
- 页面自身的卡片/控件在 `PageOnEnter` 中做各自的入场动画。

它是一个**淡入 + 页面级入场动画 + 侧栏宽度动画**的组合，不是全页面统一横滑。我们之前的 `PageSlide` 方向就是错的，后来改成 CrossFade + 卡片错峰，但仍不是每个页面独立的 `PageOnEnter`。

### 0.5 开源版 PCL2 有被删减的功能

`ModSecret.vb` 明确说明部分代码被删除：

- 更新检查/替换/重启：`UpdateCheckByButton()` 只提示“该版本中不包含更新功能……”；
- 遥测：`ClsBaseUrl = ""`；
- 赞助等级/土豆码/主题解锁：返回空或 `False`；
- 下载最新官方 PCL：空实现；
- 部分安全信息/加密逻辑被删。

所以：
- 不能从开源版推断官方版更新器、遥测、赞助主题的完整实现；
- 本项目自行实现的自动更新不属于“复刻开源 PCL2”，需要单独评估；
- 分析“PCL2 全部功能”时，必须区分**开源版可见功能**与**官方版额外功能**。

---

## 1. 代码结构与运行框架

### 1.1 规模

- VB.NET `Modules`：约 18.9k 行；
- `Pages` + `Controls`：约 26.5k 行；
- XAML：63 个文件，约 5.9k 行；
- `PCLCS`：约 920 行 C#，承担部分 Java/启动/常量逻辑；
- 总计约 49k 行。

### 1.2 关键模块

| 模块 | 职责 |
|---|---|
| `ModBase.vb` | 全局工具、INI/注册表、线程/UI 调度、搜索、进程、权限 |
| `ModLoader.vb` | `LoaderTask`/`LoaderCombo`/`LoaderWorker` 任务框架、任务栏、文件夹监听 |
| `ModNet.vb` | HTTP、NetFile/NetThread/NetManager、多线程分块下载、源回退、限速、任务统计 |
| `ModMinecraft.vb` | 游戏目录/实例、版本 JSON、规则、库、natives、资源索引、皮肤 |
| `ModDownload.vb` | Mojang/BMCLAPI 版本列表、Forge/NeoForge/Fabric/OptiFine/LiteLoader 列表与下载源 |
| `ModLaunch.vb` | 启动参数、登录、Java、natives、进程启动、日志 |
| `ModModpack.vb` | CurseForge/Modrinth/HMCL/MMC/MCBBS/LauncherPack/压缩包整合包安装 |
| `ModCrash.vb` | 崩溃日志收集、规则匹配、堆栈/Mod 名分析、报告输出 |
| `ModWatcher.vb` | 游戏进程/窗口/日志监控、启动器隐藏/关闭、音乐联动 |
| `ModMain.vb` | Hint/消息框、帮助、反馈、遥测、临时目录、主时钟 |
| `PageSetup/*` | 启动/联机/个性化/系统设置 |
| `PageInstance/*` | 实例总览/设置/Mod/导出 |
| `PageDownload/*` | 安装页、资源搜索、资源详情 |
| `PageLogin*` | 微软/统一通行证/Authlib/离线登录与皮肤 |
| `PageLinkMain` | EasyTier 联机房间 |
| `PageSpeed*` | 全局任务管理 |
| `ModAnimation.vb` | 自研 `AniStart` 动画引擎 |

### 1.3 UI 框架

- `FormMain`：自绘无边框窗口，850×500，最小 810×470；`PanBack` Margin=10，顶部标题栏 48px；
- 顶栏五个 RadioButton：启动/下载/联机/设置/更多；
- 页面通过 `PanMainLeft` + `PanMainRight` 动态插入；
- `PageStack` 支持子页面返回栈；
- 全局 `Hint`、`MyMsgBox`、`LoaderTaskbar`、`BtnExtra*` 浮动按钮。

---

## 2. 页面与功能地图

### 2.1 顶层页面

| 顶层 | 子页面/功能 |
|---|---|
| 启动 | 账户登录、版本选择、启动、启动日志、皮肤、版本设置入口 |
| 下载 | 原版/加载器组合安装、Mod、整合包、数据包、资源包、光影、资源详情 |
| 联机 | EasyTier 创建/加入房间、邀请码、节点/NAT/延迟、房间进程 |
| 设置 | 启动、联机、个性化、其他（系统） |
| 更多 | 帮助、关于与鸣谢、百宝箱、反馈、新功能投票 |

### 2.2 启动页

- 左侧 300px：
  - 登录区：正版/离线/统一通行证/Authlib；
  - 版本选择按钮、版本设置；
  - 启动按钮、加载动画、取消；
- 右侧：启动日志卡片、提示/崩溃入口；
- 启动后可由 Watcher 控制启动器隐藏/关闭。

### 2.3 实例页（版本设置）

- 实例列表：收藏/分类/隐藏/错误/API 版本等 `McInstanceCardType`；
- 总览：
  - 图标、分类、重命名、描述、收藏；
  - 版本/存档/Mod/截图文件夹；
  - 导出启动脚本、补全文件、删除版本；
- 设置：
  - 版本隔离、窗口标题/大小；
  - Java 版本区间/自动选择/自定义；
  - 内存、内存优化、JVM/游戏参数、启动前命令；
  - 登录方式（Nide/Authlib 服务器 ID/认证地址/注册链接/自动进服）；
  - 禁止更新 Mod、关闭文件校验、禁用 JLW、禁用 LWJGL Unsafe Agent；
- Mod：
  - 搜索/筛选（全部/启用/禁用/可更新）；
  - 启用/禁用/删除/更新/全选/批量操作；
  - 从文件安装、下载新 Mod、打开文件夹；
- 导出：
  - 整合包名称/版本；
  - 选项化文件清单（配置、Mod、资源、存档、PCL 等）；
  - 资源打包/仅 Modrinth 下载；
  - `.mrpack` 与包含 PCL 的 zip 二选一；
  - 导出配置/导入配置。

### 2.4 下载页

- 安装页：单页选择 Minecraft + 加载器，固定底部“开始下载”；
- Mod/整合包/数据包/资源包/光影：双平台搜索、筛选、详情、版本选择、依赖下载；
- 资源详情：CurseForge/MC 百科跳转、名称复制、版本列表。

### 2.5 设置页

- 启动：版本隔离默认值、窗口、可见性、优先级、内存优化、皮肤、JVM/游戏参数、启动前命令、Java 列表/内存/显卡/SSL/JLW/Unsafe；
- 联机：EasyTier 相关；
- 个性化：主题（HSL/预设/隐藏主题）、背景（多图/透明度/模糊/彩色叠加）、Logo、音乐、自定义主页、隐藏页面；
- 其他：下载源/版本列表源/线程/限速、更新通道、公告、缓存、语言（实际游戏语言）、动画速度、调试、遥测、导出/导入设置。

---

## 3. 安装链路：PCL2 到底怎么装

### 3.1 选择页

`PageDownloadInstall` 的卡片：

- Minecraft
- Forge
- NeoForge
- Fabric
- Fabric API
- OptiFine
- OptiFabric
- LiteLoader（1.12.2 及以下）

卡片不是独立安装按钮；它们共同组成一个选择状态。底部 `BtnStart`（“开始下载”）提交 `McInstallRequest`。

### 3.2 `McInstallLoadersGet()` 任务链

源码：`Pages/PageDownload/ModDownloadLib.vb`。

关键步骤：

1. **临时目录**：
   - Forge/NeoForge/OptiFine 的安装器对路径空格敏感，使用无空格临时目录；
2. **重复版本检查**：
   - 目标版本 JSON 已存在则拒绝；
   - 安装前写入 `.pclignore` 临时标记，从版本列表隐藏；
3. **OptiFine 形态判定**：
   - `OptiFineAsMod = OptiFineEntry IsNot Nothing AndAlso Modable`；
   - 有任意 Mod 加载器时，OptiFine 作为 Mod 放进 `mods`；否则作为独立版本 JSON 合并；
4. **下载顺序**：
   - Fabric API / OptiFabric；
   - 原版客户端（JSON/jar/libraries/assets）；
   - OptiFine；
   - Forge；
   - NeoForge；
   - LiteLoader；
   - Fabric；
5. **合并安装**：
   - `MergeJson()` 按 MC → OptiFine → Forge → NeoForge → LiteLoader → Fabric 合并；
   - 删除 `inheritsFrom`、`jar`、`_comment_`，设置新 `id`；
   - 处理 `minecraftArguments` 的合并/去重；
   - 复制临时 `libraries`；
   - 创建/复制 `mods`、`resourcepacks`；
6. **补全支持库**：
   - 用合并后的实例重新解析 libraries 并下载；
7. **写安装清单**：
   - `McInstallManifest` 记录 PCL 管理的 Mod 文件、加载器版本，供修改/更新/导出使用；
8. **删除 `.pclignore`**。

### 3.3 版本隔离

- `new McInstance(VersionFolder).PathIndie` 在安装阶段决定隔离目录；
- 受全局/实例隔离设置影响；
- 不是“只有加载器版本才隔离”这么简单，PCL2 可配置。

### 3.4 任务与后台

- 整个安装是一个 `LoaderCombo`，带子任务权重；
- `LoaderTaskbarAdd()` 加入任务栏；
- `FrmMain.BtnExtraDownload.ShowRefresh()` + `Ribble()` 触发右下角任务按钮提示；
- 安装页退出后任务继续在后台；
- 任务管理页显示总进度、速度、文件数、线程数和子任务。

### 3.5 我们之前的偏差

| 点 | PCL2 | 我们之前 |
|---|---|---|
| 实例模型 | 多加载器合并为一个版本 | 每个加载器单独实例 |
| 组合安装 | 任意兼容组合 | 单加载器 |
| 冲突判定 | 有完整互斥/版本匹配 | 无 |
| 安装按钮 | 固定底栏一次提交 | 每张卡独立安装 |
| 后台任务 | 全局任务栏/进度球/任务页 | 页面进度（后来补球） |
| 安装清单 | `McInstallManifest` | 无 |
| 隔离 | 可配置 | 自动按是否 Modable |

---

## 4. 加载器组合与冲突矩阵（PCL2 真实规则）

源码位置：

- `LoadOptiFineGetError()`、`IsOptiFineSuitForForge()`、`LoadForgeGetError()`、`LoadNeoForgeGetError()`、`LoadFabricGetError()`、`LoadLiteLoaderGetError()`
- `OptiFine_Selected()`、`Forge_Selected()`、`NeoForge_Selected()`、`Fabric_Selected()`
- `ReloadSelected()`

### 4.1 版本可见性

| 加载器 | 可见条件 |
|---|---|
| Forge | Minecraft 是标准版本格式；非标准版本隐藏 |
| NeoForge | 发布时间 >= 2023-06-11（大致 1.20.1+ 与部分愚人节版本） |
| Fabric | Minecraft > 1.13（低版本隐藏） |
| LiteLoader | Minecraft < 1.13（1.12.2 及以下） |
| OptiFine | 有对应 MC 大版本列表；否则显示“无” |
| Fabric API | 只有选择 Fabric 后出现 |
| OptiFabric | 只有 Fabric + OptiFine 同时选择后出现 |

### 4.2 互斥与条件

| 组合 | PCL2 规则 |
|---|---|
| Forge + NeoForge | 禁止；选择 NeoForge 时 Forge 报“与 NeoForge 不兼容” |
| Forge + Fabric | 禁止；Fabric 报“与 Forge 不兼容” |
| NeoForge + Fabric | 禁止；NeoForge 报“与 Fabric 不兼容” |
| NeoForge + OptiFine | 禁止；OptiFine 报“与 NeoForge 不兼容” |
| Forge + OptiFine | 仅在 OptiFine 标注的 `RequiredForgeVersion` 与所选 Forge **精确匹配**时允许；否则“与 Forge 不兼容/仅兼容特定版本的 Forge” |
| Forge 1.13–1.14.3 + OptiFine | 全部禁止 |
| Fabric + OptiFine | MC <= 1.20.4 且安装 OptiFabric 才可用；MC >= 1.20.5 禁止（无 OptiFabric） |
| Fabric + Fabric API | 非强制，但未装 Fabric API 时提示“大多数 Mod 会无法使用” |
| Fabric + OptiFabric | OptiFabric 只在 Fabric+OptiFine 且版本匹配时列出 |
| LiteLoader | 只匹配精确原版版本；1.13+ 隐藏；与 Forge 的旧版组合由时代逻辑限制 |

### 4.3 选择联动

PCL2 在用户选择组件时主动清除冲突：

- 选择 OptiFine 时，如果当前 Forge 不匹配该 OptiFine，则清空 Forge；
- 选择 Forge 时，如果当前 OptiFine 与该 Forge 不匹配，则清空 OptiFine；
- 选择 NeoForge 后，Forge/Fabric/OptiFine 卡片显示不兼容并折叠；
- 选择 Fabric 后，Forge/NeoForge 不兼容；OptiFine 仅在满足 OptiFabric 条件时保留；
- `ReloadSelected()` 会强制折叠有错误的卡片，避免“同时展开但不可用”的误操作。

### 4.4 OptiFine 作为 Mod 的特殊逻辑

- 有任意 Mod 加载器时：`OptiFineAsMod = True`，OptiFine jar 被放入 `mods`，不合并版本 JSON；
- 无加载器时：OptiFine 作为独立版本 JSON 合并；
- 若有 LiteLoader，OptiFine 的临时目录还特殊处理（`#8147`）；
- Forge+Fabric 等组合本身互斥，所以不会出现“Fabric 加载器 + Forge 专属 OptiFine”这种无效合并。

### 4.5 我们的实现与 PCL2 的差距

当前重写版：

- 安装页底部只允许选择 **一个** 加载器（无/Fabric/Forge/NeoForge/OptiFine）；
- 选择 Forge 后仍可在高级卡片里安装 Fabric，彼此没有 UI 冲突判定；
- 没有 Forge+OptiFine 的版本匹配；
- 没有 Fabric+OptiFabric / Fabric API；
- 没有把多个加载器合并成一个实例；
- 没有安装清单；
- 组合安装时先确认原版，再调用某个独立安装器，得到独立实例。

**结论：这一块目前只能算“单加载器快捷安装”，不是 PCL2 的组合安装。**

---

## 5. 启动链路（ModLaunch.vb）

### 5.1 总体顺序

`McLaunchStart` → `McLaunchPrecheck` → 登录 → 内存优化 → Java 选择 → 参数构造 → natives → 启动前命令 → 进程启动 → Watcher。

### 5.2 预检查

- 实例/版本文件是否存在、是否完整；
- Java 是否可用；
- 内存是否合理；
- 版本是否正在被修改/运行；
- 登录是否可用；
- 失败时给出可操作提示，而不是仅抛异常。

### 5.3 登录

`McLoginType`：

| 类型 | 逻辑 |
|---|---|
| `Legacy` (0) | 离线用户名；可带正版皮肤名；生成离线 UUID；支持自定义皮肤 |
| `Nide` (2) | 统一通行证：账号/密码 + Nide 服务端；缓存 access/client/uuid/name |
| `Auth` (3) | Authlib-Injector / Yggdrasil：邮箱/密码 + 自定义认证服务器；可强制重选角色 |
| `Ms` (5) | 微软 OAuth → XBL → XSTS → Minecraft → profile；refresh token 缓存 |

- `McLoginServer` 表示 Nide/Auth 的服务器登录；
- 实例设置可覆盖登录方式（服务器 ID、认证地址、注册链接、自动进服）；
- 启动参数注入 `accessToken`、`uuid`、`userName`、`userType`、`clientToken`、`profileJson`；
- authlib-injector 需要 Java Agent 和服务器地址。

### 5.4 Java

- `JavaInit` / `JavaListRefreshWorker` 扫描并缓存；
- `SelectOrDownloadJava` 按 MC 版本要求选择；缺失时下载 Mojang Runtime；
- 支持手动导入/移除 Java、版本区间、每实例 Java；
- 兼容性检查包括 Java 版本过高/过低、JDK vs JRE、OpenJ9 等。

### 5.5 参数

- `McLaunchArgumentsJVM`：内存、GC、classpath、natives、launcher 名称/版本、auth、分辨率、服务器、日志配置等；
- `McLaunchArgumentsGame`：用户名/UUID/token/版本/游戏目录/assets/assetIndex/userType/versionType/demo/宽高/服务器等；
- `McLaunchArgumentsReplace`：占位符替换（`${...}`）；
- `SplitJavaArguments` / `DeduplicateJavaArguments`：参数拆分、去重；
- 支持额外 JVM/游戏参数、启动前命令（可选等待）、窗口标题、高 DPI、进程优先级、显卡偏好、SSL 验证、JLW、Unsafe Agent。

### 5.6 启动后

- `McLaunchNatives` 解压 natives；
- `McLaunchRun` 启动进程；
- `McLaunchWait` / Watcher 监控输出、窗口标题、退出码；
- 启动器可见性可配置（隐藏/关闭/恢复）；
- 游戏退出后按设置暂停/恢复音乐；
- 崩溃时进入 `ModCrash` 分析。

---

## 6. 实例与隔离

### 6.1 实例模型

`McInstance`：

- 版本目录、JSON、jar、libraries、natives、assets；
- `PathIndie` 由隔离设置决定；
- 实例配置（PCL.ini / 注册表）保存内存、Java、窗口、登录、命令、隔离、Mod 更新开关等；
- 实例列表有缓存，按文件夹写入时间增量刷新；
- 状态分类：Star/Auto/Hidden/API/OriginalLike/Rubbish/Fool/Error。

### 6.2 隔离

PCL2 的隔离不是只按加载器判断：

- 全局默认隔离设置；
- 实例级覆盖；
- 决定 `PathIndie`（版本目录还是根目录）；
- 安装阶段就把隔离信息写入实例配置；
- 修改隔离会影响 Mod/存档/资源路径。

我们的重写版：

- `MinecraftVersionUtil.IsModable` 自动判断，加载器/OptiFine 实例用版本目录；
- 原版共享根目录；
- 没有全局默认隔离选项和实例级覆盖。

### 6.3 实例管理

- 重命名、描述、图标、收藏、分类；
- 打开版本/存档/Mod/截图目录；
- 导出启动脚本；
- 补全文件（校验/修复）；
- 删除（含确认、可选保留存档/Mod）；
- 导出/导入/克隆；
- 脚本/自定义命令。

---

## 7. Mod、资源与整合包

### 7.1 资源类型

`PCLCS/Constants.cs`：

```text
Mod, ModPack, ResourcePack, Shader, DataPack, Plugin, Map
```

平台：CurseForge + Modrinth。

### 7.2 搜索

`ResourceSearcher.SearchRequest`：

- 资源类型；
- 标签（CurseForge 分类 ID / Modrinth category slug）；
- Mod 加载器；
- MC 版本；
- 搜索文本；
- 来源（CurseForge/Modrinth/Any）。

逻辑：

- CurseForge 使用 `/v1/mods/search`，按 classId 区分类型；
- Modrinth 使用 `/v2/search` + facets；
- 两家分别维护 offset/total，交替加载 40 条/页；
- 合并排序、中文无结果提示、MCIM/镜像回退；
- 资源详情支持 CurseForge/MC 百科跳转、复制名称、版本列表。

### 7.3 版本与依赖

`ResourceVersion`：

- 下载 URL 多源（官方 + 镜像）；
- CurseForge CDN 地址重写；
- 依赖关系、Modrinth `required` 依赖；
- 下载原因：独立/依赖/整合包/更新；
- 本地缓存 JSON。

### 7.4 本地 Mod

`LocalResourceFile` / `LocalResourceLoaders`：

- 扫描 `mods/`，读取 Forge `mcmod.info`/Fabric `fabric.mod.json` 元数据；
- 识别 CurseForge 项目/文件 ID 与 Modrinth 项目 ID；
- 启用/禁用（重命名 `.disabled`）、删除、更新；
- 缓存本地 Mod 信息，支持“可更新”筛选、批量更新；
- 搜索/过滤/全选操作。

### 7.5 整合包格式

`ModModpack.vb` 支持：

| 格式 | 识别 | 逻辑 |
|---|---|---|
| CurseForge | `manifest.json` + `minecraft`/`modLoaders` | 下载 Mod、处理 overrides、缺文件提示 |
| Modrinth | `modrinth.index.json` | 读取 dependencies/files，Fabric/Forge/NeoForge |
| HMCL | HMCL 整合包结构 | 解析 |
| MMC | `mmc-pack.json` | 读取组件版本 |
| MCBBS | `mcbbs.packmeta` | 解析 |
| LauncherPack | 特定结构 | 解析 |
| 通用压缩包 | 只有目录结构 | 按覆盖安装 |

- 安装时同样走 `McInstallLoadersGet` 合并加载器；
- 支持 OptiFine 文件匹配、Overrides、缺失资源提示；
- 不支持 Quilt 整合包（源码明确提示）。

我们的重写版：

- 只支持 Modrinth `.mrpack`（Fabric/Forge/NeoForge）+ 自定义 zip；
- 不支持 CurseForge/HMCL/MMC/MCBBS；
- 资源搜索只覆盖 Modrinth（CurseForge 仅基础 Mod）；
- 无本地 Mod 元数据指纹/批量选择/整合包导出高级选项。

---

## 8. 网络与任务系统

### 8.1 NetManager

`ModNet.vb`：

- `NetFile`：一个目标文件，多下载源；
- `NetThread`：分块线程，支持范围请求、断点、重试、速度统计；
- `NetManager`：全局线程池、任务队列、速度限制、文件数/剩余线程统计；
- 源回退：官方 → BMCLAPI → MCIM/其他镜像；
- `LoaderDownload`：把一个 `NetFile` 组包装成 Loader 任务；
- 已存在文件检查/提示；
- 下载后 SHA1/大小校验；
- 速度限制、最大线程数、任务优先级。

### 8.2 Loader 任务框架

- `LoaderTask(Of TIn,TOut)`、`LoaderCombo`、`LoaderWorker`；
- 任务可嵌套，权重 `ProgressWeight`；
- `Show` 控制是否显示在任务栏；
- `Block` 控制是否阻塞后续任务；
- `LoaderTaskbarAdd` / `LoaderTaskbarProgressRefresh` / `LoaderTaskbarProgressGet`；
- 任务可取消、失败、重试；
- 任务的 `OnStateChanged` 驱动 UI。

### 8.3 任务管理页

`PageSpeedLeft/Right`：

- 总进度、下载速度、剩余文件数、剩余线程数；
- 子任务列表（名称、进度、状态）；
- 取消任务；
- 任务完成提示；
- 右下角 `BtnExtraDownload` 只在有下载任务时显示。

我们的重写版：

- 有 `DownloadManager`（并发、断点、SHA1），但没有 PCL2 的全局线程池/限速/任务树；
- 有页面进度；
- 后来补了右下角任务球，但没有任务列表、取消全部、速度/剩余线程统计，也没有统一的 Loader 任务框架。

---

## 9. UI、主题、动画与个性化

### 9.1 窗口与布局

- 850×500（注册表默认 900×550），最小 810×470；
- 无边框，自绘标题栏 48px；
- 顶部 5 个 RadioButton；
- 左侧子导航宽度随页面变化（0/150/230 等），带 180ms 宽度动画；
- 页面右侧滚动区，卡片化布局；
- `PageStack` 支持子页面返回。

### 9.2 主题

`ModSecret.ThemeRefresh`：

- 颜色由 HSL 生成：主色、浅色、背景、渐变；
- 顶部栏三段渐变，可设置 hue delta；
- 预设主题 + 自定义色调/饱和度/亮度；
- 隐藏主题/解锁主题（开源版禁用）；
- 背景图片列表、透明度、模糊、彩色叠加；
- 自定义 Logo（预设/文本/图片）；
- 背景音乐、自定义主页、隐藏页面。

### 9.3 动画

`ModAnimation`：

- 自研 `AniStart` 动画引擎，支持 X/Y/Width/Height/Opacity/Scale/Rotate/Color/TextAppear 等；
- 多种缓动：Fluent/Back/Elastic/Car 等；
- 页面切换使用 110ms 插入、30ms 延迟显示、标题栏 150/350ms；
- 卡片/列表项有各自的 `TriggerShowAnimation`/`PageOnEnter`；
- 按钮/列表有悬停、按下、选中动画；
- 动画速度可配置。

我们的重写版：

- 只有 CrossFade + 卡片统一错峰 + 侧栏宽度 + 按钮缩放；
- 没有自研动画引擎/多缓动/每页独立入场；
- 主题只有浅色/深色/跟随系统，没有 HSL/预设/隐藏主题；
- 背景图只有单文件路径，不支持列表/随机/彩色叠加；
- 没有自定义 Logo/音乐/自定义主页/隐藏页面。

---

## 10. 联机（EasyTier）

`PageLinkMain`：

- 内置/下载 EasyTier；
- 创建房间、加入房间（邀请码）；
- 生成配置、启动 EasyTier 进程；
- 读取节点/peer/NAT 类型/延迟；
- 选择目标 peer；
- 监听进程输出、崩溃、退出；
- 复制邀请码/IP；
- 房间结束清理。

我们的重写版：

- 只有页面和“暂未实现”按钮；
- 没有 EasyTier 下载/配置/进程/房间逻辑。

---

## 11. 崩溃分析与 Watcher

### 11.1 Watcher

- 监控游戏进程 stdout/stderr；
- 监控窗口标题（支持 `{date}`/`{time}` 占位符）；
- 判定 Loading/Running/Crashed/Ended/Canceled；
- 全局检测是否有运行中的 Minecraft；
- 根据设置隐藏/关闭/恢复启动器；
- 游戏退出时暂停/恢复音乐；
- 关闭启动器前确认。

### 11.2 崩溃分析

`ModCrash.CrashReason` 覆盖约 45 类：

- Java 参数/版本过高/不兼容/32 位/OpenJ9/JDK；
- 内存不足；
- 显卡/OpenGL/驱动/像素格式；
- Mod 被解压、Mixin/Bootstrap、Mod 冲突、重复安装、缺少前置、Mod 过多；
- OptiFine/Forge/Fabric/ShadersMod 兼容；
- 资源包/光影/材质过大；
- 文件校验失败、安装不完整；
- 特定方块/实体；
- 日志为空/极短输出。

分析流程：

1. 收集 latest.log、hs_err、crash-report；
2. 高优先级精确匹配；
3. 堆栈关键字/Mod 名模糊分析；
4. 低优先级规则；
5. 输出原因、建议、日志/报告。

我们的重写版：

- 有基础 CrashAnalyzer（Java/OOM/Mod/GLFW/权限等）；
- 没有 PCL2 这么细的规则、Mod 名匹配、hs_err/崩溃报告结构化输出；
- 没有 Watcher 的窗口标题/隐藏启动器/音乐联动。

---

## 12. 设置、更新、遥测与其他

### 12.1 设置

`Settings.vb` 中定义约 457 个设置引用，包含：

- 启动：隔离、窗口、可见性、优先级、内存优化、皮肤、Java、命令、JLW/Unsafe/GPU/SSL；
- 联机；
- 个性化：主题 HSL、背景、Logo、音乐、主页、隐藏页面；
- 系统：下载源/版本源/线程/限速、更新通道、公告、缓存、语言、动画速度、调试、遥测；
- 导入/导出设置。

存储：注册表 + PCL.ini/实例配置；敏感项加密（官方版）。

我们的重写版：

- JSON 设置，覆盖常用项；
- 没有 HSL/背景列表/Logo/音乐/主页/隐藏页面/设置导入导出/遥测/调试/限速等。

### 12.2 更新/遥测

开源版 `ModSecret`：

- `UpdateCheckByButton()` 只提示“不包含更新功能”；
- `UpdateRestart`/`UpdateReplace`/`DownloadLatestPCL` 空实现；
- `ClsBaseUrl=""`，遥测无效；
- 赞助/土豆码/主题解锁返回空/False。

因此：

- 官方版更新器/遥测/赞助逻辑无法从开源版还原；
- 我们的自动更新是独立实现，需要在文档中明确不是 PCL2 复刻；
- 不应把官方版未公开功能当作“源码规范”。

### 12.3 其他

- 帮助：从版本化资源包解压 HTML/XML 帮助条目，支持搜索/详情；官方帮助内容不在仓库里（下载资源）。
- 音乐：播放列表、随机、循环、游戏启动/退出自动暂停/恢复。
- 自定义主页：预设/本地/网络 XML，`GetObjectFromXML` 动态渲染；有安全警告。
- 皮肤：本地/在线/正版/Nide/Auth 皮肤，保存/缓存/重置。
- 反馈/投票：外部网页。
- 百宝箱：开源版明确不包含。
- 语言：源码中未发现 i18n 资源；PCL2 主要是中文，设置里的“语言”是游戏语言。

---

## 13. 我们的重写版 vs PCL2：功能差距表

| 领域 | PCL2 | 当前重写 | 级别 |
|---|---|---|---|
| 安装模型 | 多组件合并为一个实例 | 独立实例/单加载器 | **严重** |
| 加载器冲突 | 完整互斥/版本匹配 | 无 | **严重** |
| Forge+OptiFine | 精确版本匹配 | 不支持 | 高 |
| Fabric+OptiFabric | 支持 | 不支持 | 高 |
| Fabric API | 自动/提示 | 不支持 | 中 |
| 安装清单 | `McInstallManifest` | 已加最小 `PCL/install.json` | 中 |
| 隔离 | 全局/实例可配置 | 自动判断 | 中 |
| 登录 | Ms/Nide/Auth/Legacy + 服务器登录 | 离线/Ms/多 Authlib + 实例认证服务器 + 皮肤 | 中 |
| 皮肤 | 本地/在线/多登录源 | 无 | 中 |
| 任务系统 | Loader 树/任务页/进度球/限速 | 任务球 + 页面进度 | 高 |
| 下载 | 多源/多线程/限速/重试 | 基础多线程/断点 | 中 |
| 资源 | CF+Modrinth、7 类型、依赖 | CF/Modrinth 资源包/光影/数据包/Mod | 中 |
| 整合包 | CF/Modrinth/HMCL/MMC/MCBBS/压缩 | Modrinth + CurseForge + MMC/HMCL/MCBBS | 中 |
| 实例 | 总览/设置/Mod/导出/脚本 | 基础 | 中 |
| Java | 扫描/下载/区间/每实例/兼容 | 基础 | 中 |
| 启动 | 完整参数/命令/GPU/SSL/Watcher | 基础 | 中 |
| 崩溃 | 45 类规则/堆栈/报告 | 基础规则 | 中 |
| 联机 | EasyTier 完整 | 占位 | **严重** |
| 主题/UI | HSL/多主题/背景/Logo/音乐/主页 | 浅色/深色/单背景 | 中 |
| 动画 | 自研引擎/多缓动/页面入场 | CrossFade/卡片错峰 | 中 |
| 更新 | 开源版无；官方未知 | 自实现 | 需说明 |
| i18n | 未发现 | 无 | 不算差距 |

---

## 14. 修复优先级建议

### P0（功能正确性）

1. **重做安装模型**：`McInstallRequest` 等价物，支持多组件合并一个实例；
2. **加载器冲突引擎**：按第 4 节矩阵，在 UI 选择时实时互斥/清除/过滤；
3. **Forge+OptiFine、Fabric+OptiFabric、Fabric API** 的组合安装；
4. **合并 JSON**：`inheritsFrom`/`libraries`/`arguments` 的合并顺序、ID、jar；
5. **安装清单**：记录 managed files/loader versions，供修改/导出。

### P1（可用性）

6. 全局任务管理器：任务树、总进度、速度、剩余文件/线程、取消；
7. 登录补 Nide/Authlib + 实例服务器登录；
8. 资源平台补 CurseForge + 数据包/地图/插件；
9. 整合包补 CurseForge/HMCL/MMC/MCBBS；
10. Watcher/崩溃分析对齐。

### P2（UI/体验）

11. 主题 HSL/背景列表/Logo/音乐/自定义主页；
12. 每页独立入场动画/侧栏宽度/标题栏子页动画；
13. 实例页总览/导出选项/脚本；
14. EasyTier 联机。

---

## 15. 一句话总结

PCL2 的“安装”不是“下原版，再装加载器”，而是**组件选择 → 兼容性判定 → 多组件下载 → JSON/库合并 → 单个实例 → 安装清单**；PCL2 的“下载”也不是页面进度条，而是**全局 Loader 任务树 + 任务栏 + 右下角进度球 + 任务管理页**。这两点是我们目前最大的结构性偏差，必须先改模型，而不是继续在页面上加卡片。

---

## 16. 下载源与网络细节（补充）

### 16.1 版本/库/资源

PCL2 的下载源分为“官方源”和“镜像源”，由设置中的“文件下载源/版本列表源”控制；`DlSourceOrder`、`DlVersionListOrder` 等在官方/BMCLAPI 地址之间排序。

| 内容 | 主要来源 |
|---|---|
| 版本清单 | Mojang piston-meta、BMCLAPI |
| 客户端 JSON/jar | Mojang、BMCLAPI |
| libraries/assets | Mojang resources、BMCLAPI |
| Forge 列表/安装器 | Forge 官方、BMCLAPI |
| NeoForge 列表/安装器 | NeoForge 官方、BMCLAPI |
| Fabric Loader | Fabric Meta、BMCLAPI |
| OptiFine | optifine.net 页面/签名下载、BMCLAPI |
| Java Runtime | Mojang、BMCLAPI |
| CurseForge | api.curseforge.com（需 API Key） |
| Modrinth | api.modrinth.com |
| authlib-injector | 官方 latest.json、BMCLAPI |
| 整合包资源 | CurseForge/Modrinth CDN + MCIM 镜像 |

`NetManager` 会对同一文件尝试多个源，失败换源；`NetFile`/`NetThread` 负责分块、断点、重试、校验；`LoaderDownload` 把文件组变成可显示的任务。

### 16.2 下载设置的实现

`PageSetupSystem`：

- 文件下载源 / 版本列表源；
- 最大线程数；
- 速度限制；
- 缓存目录；
- 更新通道/公告；
- 动画速度、调试、遥测；
- 设置导出/导入。

我们的重写版只有镜像枚举、并发和 45s 超时，缺少速度限制/源选择/任务统计/导入导出。

---

## 17. 页面/源码索引（审计依据）

| 功能 | 源码 |
|---|---|
| 主窗口/顶栏/页面栈/切换动画 | `FormMain.xaml`, `FormMain.xaml.vb` |
| 安装请求/合并/任务链 | `Pages/PageDownload/ModDownloadLib.vb` (`McInstallRequest`, `McInstallLoadersGet`, `MergeJson`, `McInstallStart`) |
| 安装页选择/冲突 | `Pages/PageDownload/PageDownloadInstall.xaml(.vb)` (`ReloadSelected`, `Load*GetError`) |
| 版本 JSON/规则/库/natives/assets | `Modules/Minecraft/ModMinecraft.vb` |
| 版本/Forge/Neo/Fabric/OptiFine 列表 | `Modules/Minecraft/ModDownload.vb` |
| 启动/登录/参数 | `Modules/Minecraft/ModLaunch.vb` |
| Java 发现/下载 | `Modules/Minecraft/ModJava.vb` |
| 任务框架/任务栏 | `Modules/Base/ModLoader.vb` |
| 网络/下载 | `Modules/Base/ModNet.vb` |
| 动画引擎 | `Modules/Base/ModAnimation.vb` |
| 整合包 | `Modules/Minecraft/ModModpack.vb` |
| 资源搜索/项目/版本/本地 Mod | `Modules/Resource/*.vb` |
| 实例页/设置/Mod/导出 | `Pages/PageInstance/*.xaml(.vb)` |
| 设置页 | `Pages/PageSetup/*.xaml(.vb)`, `Settings.vb` |
| 崩溃分析 | `Modules/Minecraft/ModCrash.vb` |
| 游戏 Watcher | `Modules/Minecraft/ModWatcher.vb` |
| 联机 EasyTier | `Pages/PageLink/PageLinkMain.xaml(.vb)` |
| 任务管理页 | `Pages/PageSpeedLeft/Right.xaml(.vb)` |
| 更新/遥测/密钥（开源版删减） | `Modules/ModSecret.vb` |
| 音乐 | `Modules/ModMusic.vb` |
| 帮助 | `Modules/ModMain.vb` HelpLoad/HelpTryExtract |
| 常量/资源类型/加载器枚举 | `PCLCS/Constants.cs` |

### 审计边界

- 已覆盖全部模块/页面清单、安装/启动/资源/任务/设置/UI/联机关键链路；
- 未逐行验证每个崩溃规则、每个下载源、每个隐藏主题/活动逻辑；
- 官方闭源版的更新、遥测、赞助、加密/鉴权逻辑无法从开源仓库还原；
- 未做 PCL2 实机运行对照（Windows 环境不在此机器），视觉/行为结论来自源码与页面结构。

---

## 18. 最终判断

1. **安装模型必须重做**：从“独立加载器实例”改成“组件选择 + 合并 JSON + 单实例 + 安装清单”。
2. **冲突规则必须前置**：加载器组合不是 UI 装饰，而是安装正确性的一部分。
3. **任务系统必须统一**：页面进度无法替代 Loader 任务树、后台任务栏、进度球和任务管理页。
4. **资源/整合包/登录/联机仍有大块功能缺口**：这些不是“UI 没对齐”，而是功能模型没实现。
5. **动画只是表层**：PCL2 的动画服务于页面层级与任务反馈，先把功能层级做对，再追求动画细节。

