# 实施进度

## M0 — 工具链与脚手架 ✅

- 用户级安装 .NET SDK **10.0.402** 到 `~/.dotnet`，`~/.local/bin/dotnet` 软链。
- `global.json` 固定 SDK；`Directory.Build.props` 统一 `LangVersion 14`、`Nullable`。
- 解决方案：`PCL.slnx`，项目 `Core/Download/Minecraft/Launch/Cli` + 两个测试项目。
- `dotnet build -c Release` 通过；`dotnet test -c Release` 27 个测试通过。

## M1 — 核心库 + CLI 启动原版 ✅

### 已实现

| 能力 | 实现 |
|---|---|
| 版本清单 | `version_manifest_v2.json` 拉取 + 10 分钟缓存 + 离线回退 |
| 版本解析 | `inheritsFrom` 递归合并、`rules`（OS/arch/version/features） |
| 库解析 | Maven 坐标、classifier、natives（旧式 `natives` map 与新式独立条目均支持） |
| 下载 | 并发、官方/BMCLAPI 回退、`.part` 续传、SHA1 校验、聚合进度 |
| 资源 | asset index + objects（`assets/objects/xx/hash`） |
| 安装 | client jar、libraries、assets、log config，幂等 |
| Java | `JAVA_HOME`/`PATH`/`/usr/lib/jvm`/`~/.jdks`/SDKMAN 发现，版本匹配 |
| 参数 | JVM/game 参数、占位符替换、natives、classpath、logging |
| 账户 | 离线 UUID（Java 兼容）、accessToken=0 |
| 启动 | 进程启动、stdout/stderr 实时日志、取消时杀进程树 |

### 实测

```bash
dotnet run --project src/PCL.Cli -c Release -- install 1.21.1 --dir /home/wenxiangl/pcl-test-mc --threads 24
# 完成：1.21.1，3947 个文件，872.7 MB，耗时 00:02（二次运行）

dotnet run --project src/PCL.Cli -c Release -- launch 1.21.1 --offline Steve --dir /home/wenxiangl/pcl-test-mc
# 窗口标题：Minecraft 1.21.1
```

日志关键行：

- `Sound engine started`
- `Created: 1024x512x4 minecraft:textures/atlas/blocks.png-atlas`
- 无 `Exception` / `ERROR`

### 过程中修复的 bug

1. **原生库去重覆盖 base jar**
   - 现象：`NoClassDefFoundError: org/lwjgl/system/MemoryUtil`
   - 原因：1.21.1 的 natives 是独立 library（`org.lwjgl:lwjgl:3.3.3:natives-linux`），去重键未包含 Maven classifier，导致 base jar 被覆盖、安装时漏下载。
   - 修复：去重键改为 `group:artifact:mavenClassifier`；新增 `LibraryResolverTests` 回归。

2. **HTTP 自动解压 + Range 续传导致 416**
   - 现象：client.jar 反复“服务器拒绝续传范围”。
   - 原因：下载器启用了 `AutomaticDecompression.All`，写入 `.part` 的是解压后字节，续传偏移超过压缩文件大小。
   - 修复：下载专用 HttpClient 关闭自动解压；416 时若 `.part` 的 SHA1 已匹配则直接视为完成。

3. **Java 版本推断漏掉 1.20.5**
   - 修复：1.20.5+ 判定 Java 21；新增参数化测试。

### 待补

- 断网、取消、含空格/中文路径的集成测试。
- 安装前磁盘空间检查、下载失败重试上限可配置。
- 旧版本（`minecraftArguments`、legacy assets、`virtual`）尚未实测。

## M2 — Avalonia 壳 ✅

### 已实现

- `PCL.UI`：PCL 蓝色系主题（取自原版 `Application.xaml`）、卡片/按钮/输入框/列表/进度条样式。
- `PCL.App`：组合根、`MainWindowViewModel`、导航（启动/下载/实例/设置/关于）。
- 启动页：版本列表（已安装优先）、筛选、账户名、内存、安装/启动/取消、实时日志、进度与状态。
- 下载页：远程版本列表、筛选、安装、校验/修复、取消、进度。
- 实例页：已安装实例、版本目录/游戏目录入口。
- 设置页：游戏目录、内存、玩家名、Java 路径、下载源、保存与 Java 扫描。
- 关于页：版本、运行时、署名与许可提示。
- 日志清洗：把 log4j XML 控制台输出还原为可读消息（`LogSanitizer`）。
- 开发参数：`--game-dir`、`--auto-install`、`--auto-launch`，用于 GUI 端到端冒烟。

### 验证

- GUI 正常启动，窗口标题 `PCL Linux`，版本列表加载 120+ 个版本。
- 已安装版本显示“已安装”徽标，启动按钮可用。
- `--auto-launch 1.21.1` 成功从 GUI 拉起游戏（窗口 `Minecraft 1.21.1`，Java 进程存活）。
- `--auto-install 1.21.1` 显示“安装完成：3,947 个文件，872.7 MB”。
- 日志面板实时显示 `Sound engine started`、纹理图集创建等。

### 修复/调整

- 默认选中项：优先已安装版本，其次最新正式版。
- 日志面板不再显示原始 XML。
- 启动时不再自动保存设置，避免临时目录被写进配置。

### 待补

- 自动化 UI 测试（Avalonia.Headless）。
- 窗口图标、自定义标题栏、暗色主题。
- 日志滚动性能（当前按 200k 字符截断）。

## M3 — 下载/校验完善 ✅

- `InstallOptions.VerifyExisting` → `DownloadOptions.VerifyExisting`，已有文件逐个 SHA1 校验。
- 下载页新增“校验 / 修复”按钮；CLI `install --verify`。
- GUI `--auto-install 1.21.1` 冒烟：状态显示“安装完成：3,947 个文件，872.7 MB”。
- 修复“全部命中缓存”时的瞬时速度异常（初始化后重置传输速率基线）。

## M4 — Java 自动下载 ✅

- 新增 `JavaProvisioner`：Mojang `java-runtime` `all.json`（官方 + BMCLAPI 回退）→ 组件 manifest → 136 个文件 → SHA1 校验 → chmod → `-XshowSettings` 探测。
- 版本匹配：`java-runtime-delta` = 21.0.7；`jre-legacy` = 8u202；`gamma/beta` = 17；`epsilon` = 25。
- 实测 `pcl java install 21 --force`：105 MB，`bin/java -version` 输出 21.0.7；`java list` 能看到 PCL Java。
- UI/CLI 启动改为 `EnsureAsync`：系统已有 Java 直接用，没有才下载。

### 说明

- 先尝试 Adoptium/Temurin：GitHub Release 连接超时；清华 TUNA 对 .NET 客户端返回 403；华为镜像为 SPA 假 200。最终采用原版 PCL 的 Mojang Runtime 方案，官方与 BMCLAPI 均可用。

## M6 — Fabric ✅

### 对照原版

- PCL `McDownloadFabricLoader`：版本 ID 为 `fabric-loader-{loader}-{mc}`；profile JSON **BMCLAPI 优先、官方兜底**；安装后补全 libraries，并创建 `mods` 目录。
- 本项目按此实现：`FabricInstaller` 写入 `versions/<id>/<id>.json`，复用 `VersionInstaller` 补全依赖；`mods` 目录在安装完成后创建。

### 实测

- `pcl fabric install 1.21.1` → 67 个文件，默认稳定版 Loader 0.19.5。
- `pcl launch fabric-loader-0.19.5-1.21.1` → 日志 `Loading Minecraft 1.21.1 with Fabric Loader 0.19.5`，窗口 `Minecraft* 1.21.1`。

### 踩坑

- 官方 `meta.fabricmc.net` / `maven.fabricmc.net` 连接后不响应；元数据请求加 8 秒超时，并改为 BMCLAPI 优先。
- 依赖下载：ASM 走 Maven Central，Fabric 自身走 BMCLAPI；下载器补 User-Agent/Accept，并加 45 秒单请求超时。

### UI

- 新增“模组”页（`ModsViewModel` / `ModsView`）：选择游戏版本与 Loader、安装 Fabric、进度、打开 mods 目录。
- 安装完成会触发启动页刷新；支持 `--page mods` 开发参数。

## M6 — Modrinth ✅

### 对照原版

- 搜索：`/v2/search?index=relevance&facets=[[project_type:mod],[categories:fabric],[versions:<mc>]]`。
- 版本：`/v2/project/{id}/version?include_changelog=false`，按 `game_versions` / `loaders` 过滤。
- 文件：`files[].url`，`cdn.modrinth.com` 走 `mod.mcimirror.top` 镜像。
- 依赖：`dependencies` 中 `required` 的项目递归安装，最多 3 层。

### 实现

- `ModrinthClient`：搜索、版本选择、主文件下载、required 依赖递归安装。
- CLI：`pcl modrinth search/install`。
- UI：模组页右侧 Modrinth 搜索列表 + “安装所选 Mod”，显示下载进度与结果。

### 实测

- `pcl modrinth search sodium --game 1.21.1` → 47 条结果。
- `pcl modrinth install sodium` → `sodium-fabric-0.8.13+mc1.21.1.jar`（SHA1 匹配）。
- Fabric 启动日志出现 `Sodium`、`fabric-api-base` 等；窗口 `Minecraft* 1.21.1`。
- GUI `--modrinth-install sodium` 同样落盘成功，界面状态显示“已安装”。

### 已知问题

- CDN 在 .NET 下下载偏慢（约 1 分钟 / 1.5MB），后续可调请求策略或镜像优先级。
- 仅对可装 Mod 的版本自动隔离；原版实例仍共享根目录 mods。

## Forge / NeoForge（核心 + CLI）🟡

### 对照原版

- PCL 用 `McDownloadForgelikeLoader` 下载安装器，并通过自定义 `JavaWrapper` / `com.bangbang93.ForgeInstaller` 处理 processors。
- 本项目直接使用官方 installer 的 `--installClient`（headless），再复用 `VersionInstaller` 补全 assets/libraries。
- 版本列表：Forge 用 BMCLAPI `/forge/minecraft/<mc>`；NeoForge 用 BMCLAPI/official 的 maven details API（对照 PCL 的 `DlForgeVersionBmclapiMain` / `DlNeoForgeListBmclapiMain`）。

### 实现

- `ForgelikeInstaller`：版本列表解析、安装器下载、`launcher_profiles.json`、headless 安装、检测新版本目录、补全资源。
- NeoForge 特有：解析安装器内 `install_profile.json` + `version.json` 的库路径，先从 BMCLAPI/Central/Aliyun 预置到 `libraries/`，再 `--offline --installClient <gameDir>`，绕开不可达的官方 Maven。
- 安装器失败时附带最后 20 行输出。
- CLI：`pcl forgelike versions/install ... --loader forge|neoforge`。
- GUI：模组页新增 Forge/NeoForge 卡片（加载器/版本选择、安装、进度、取消）。
- 修复 Forge JVM 参数占位符：`${library_directory}`、`${classpath_separator}`。

### 实测

- `forgelike versions 1.20.1 --loader forge` → 47.4.28 等 137 个版本。
- `forgelike install 1.20.1 --loader forge --version 47.3.0` → `1.20.1-forge-47.3.0`。
- 启动：窗口 `Minecraft Forge* 1.20.1`，Java 17。
- GUI：1.21.1 下正常加载 69 个 Forge 版本并可选。
- NeoForge 1.21.1-21.1.80 自动预置库 + 离线安装成功；启动窗口 `Minecraft NeoForge* 1.21.1`。
- Forge 1.20.1 + JEI：Modrinth 按 Forge loader 安装 `jei` 与 `mezz_config` 到实例 mods，启动进入 Forge。
- 测试：42 个通过（新增 Forge/Neo 版本解析）。

### 待补

- NeoForge 全部镜像不可用时的重试/提示。
- 安装器内部下载仍走官方 maven，网络差时可能较慢。

## M5 — 微软登录（部分）🟡

### 对照原版

- PCL 的 6 步链路：`devicecode` → `user.auth.xboxlive.com` → `xsts.auth.xboxlive.com` → `login_with_xbox` → `entitlements/mcstore` → `minecraft/profile`。
- PCL 使用自有 OAuth Client ID 与内置登录弹窗；本项目改为标准设备码 + 可配置 Azure Client ID。

### 实现

- `MicrosoftAuthService`：设备码申请、轮询、XBL/XSTS、Minecraft 登录、正版授权检查、档案读取、refresh token 续期。
- `MicrosoftAccountStore`：账号缓存，Linux 下 0600 权限。
- CLI：`pcl login microsoft --client-id <ID>`、`login status`、`login logout`、`launch --microsoft`。
- UI：设置页 Client ID；启动页“微软登录”对话框（设备码、打开登录页、进度、失败提示）；登录成功后自动填入玩家名。
- 启动时如果 AccessToken 过期且配置了 Client ID，会用 refresh token 自动续期。

### 验证

- `PCL.Launch.Tests` 用 fake HTTP 覆盖完整登录链路与 refresh；
- GUI 登录对话框可打开，未配置 Client ID 时提示正确；
- 总计 40 个测试通过。
- 真实账号 E2E 未做：需要用户提供 Azure 公共 Client ID。

## 实例隔离 ✅


### 对照原版

- PCL `McInstance.PathIndie`：可装 Mod 的版本默认使用 `versions/<id>` 作为游戏目录；否则使用 `.minecraft` 根目录。
- 启动参数中 `--gameDir` 用实例目录，assets/libraries/natives 仍走根目录。

### 实现

- `MinecraftVersionUtil`：`IsModable` / `GetLoader` / `GetBaseGameVersion` / `GetGameDirectory`。
- `LaunchRequest` 拆分 `GameDirectory`（实例工作目录）与 `MinecraftDirectory`（根目录）。
- `LaunchPlanBuilder` 使用根目录拼 classpath/assets/natives，使用实例目录作为 `--gameDir`。
- Fabric 安装后创建 `versions/<id>/mods`；模组页“目标实例”按实例选择 mods 目录。
- CLI：`pcl modrinth install ... --instance <id>` 安装到实例 mods。

### 实测

- 原版 `pcl launch 1.21.1 --dry-run` → `--gameDir /home/wenxiangl/pcl-test-mc`。
- Fabric `--dry-run` → `--gameDir .../versions/fabric-loader-0.19.5-1.21.1`。
- Fabric 启动后实例目录出现 `mods/`、`saves/`、`logs/`、`options` 等；Sodium 从实例 mods 加载。
- 模组页目标实例显示 `fabric-loader-0.19.5-1.21.1 · Minecraft 1.21.1 · fabric`，mods 路径为实例目录。

## Modrinth 整合包（Fabric）🟡

### 对照原版

- PCL 的 `InstallPackModrinth`：读取 `modrinth.index.json` → 依赖 `minecraft`/`fabric-loader` → 解压 `overrides`/`client-overrides` → 下载 `files`（SHA1）→ 安装加载器。
- 本项目复用 Fabric 安装器，并新增自定义实例 ID，避免同一加载器版本互相覆盖。

### 实现

- `ModpackInstaller`：
  - 解析 `modrinth.index.json`（name/versionId/minecraft/fabric-loader/forge/neoforge/files）；
  - Fabric 用自定义实例 ID；Forge/NeoForge 安装后复制 JSON/jar 到自定义实例 ID（避免复用加载器目录）；
  - Forge/NeoForge 通过 `JavaProvisioner` 按 MC 版本准备 Java（1.20.1→17，1.21.1→21）；
  - 解压 overrides/client-overrides，校验路径不越界；
  - 下载 files 到实例目录（跳过 unsupported、支持 SHA1）。
- CLI：`pcl modpack install <file.mrpack> --name`；`modpack install-modrinth <projectId>`；`modpack export <实例>`。
- `ModpackExporter`：把隔离实例导出为 `.mrpack`，加载器依赖 + `overrides/`（mods/config/resourcepacks/shaderpacks/options），实例页新增导出按钮。
- GUI“整合包”页：游戏版本/关键词搜索、结果列表、实例名、安装、进度、取消、本地 `.mrpack` 路径。

### 验证

- 生成本地测试包（`minecraft 1.21.1` + `fabric-loader 0.19.5`，无额外 Mod，含 overrides）。
- 安装 → `versions/PCLTestPack/PCLTestPack.json`、`.jar`、`config/pcl-test.txt`、`options.txt`。
- 启动 → `Minecraft* 1.21.1`，日志 `Loading Minecraft 1.21.1 with Fabric Loader 0.19.5`、`Sound engine started`。
- 解析单元测试 2 条；总测试 59 个通过。
- GUI：1.21.1 搜索 “fabulously” 得 18 个结果，Fabulously Optimized 选中可安装。
- Forge 整合包：本地 `ForgeTestPack.mrpack`（1.20.1 + 47.3.0）→ `ForgePackTest`，启动 `Minecraft Forge* 1.20.1`。
- NeoForge 整合包：本地 `NeoTestPack.mrpack`（1.21.1 + 21.1.80）→ `NeoPackTest`，启动 `Minecraft NeoForge* 1.21.1`。

### 待补

- 真实 Modrinth 大包下载测试。

## CurseForge（核心 + CLI）🟡

### 对照原版

- PCL 的 CurseForge Key 来自 `PCL_CURSEFORGE_API_KEY` 环境变量（`ModSecret.vb`），请求头 `x-api-key`，API 镜像 `mod.mcimirror.top/curseforge`。
- 搜索 `classId=6` + `gameVersion` + `modLoaderType`；安装递归 `RequiredDependency`。

### 实现

- `CurseForgeClient`：搜索、文件列表、最新文件选择、SHA1、依赖递归下载；官方 API 失败时尝试镜像。
- Loader 映射：Forge 1 / Fabric 4 / Quilt 5 / NeoForge 6。
- 设置页新增 CurseForge Key；环境变量 `PCL_CURSEFORGE_API_KEY` 兜底。
- CLI：`pcl curseforge search/install ...`。
- GUI：模组页来源切换 Modrinth/CurseForge；无 Key 时明确提示；安装走同一依赖递归逻辑。
- 修复模组页把 `PCLTestPack` 等整合包实例当成原版版本的问题（改用版本 JSON 的 `IsModable` 判定）。

### 验证

- 单元测试：搜索解析、最新文件选择（下载 URL 缺失时回退 edge.forgecdn.net）、依赖列表、缺 Key 报错。
- 缺 Key：CLI 与 GUI 均明确提示“未配置 CurseForge API Key”。
- GUI：来源选 CurseForge、搜索 jei 时提示填写 Key；游戏版本列表恢复为 1.21.1/1.20.1。
- 真实 API E2E 未做：需要有效 Key（镜像会转发 dummy key 并返回 403/500）。
- 测试：57 个通过。

## OptiFine ✅

### 对照原版

- PCL 从 optifine.net 页面取签名下载地址；在临时 `user.home/.minecraft` 中准备原版 JSON/jar，运行 `optifine.Installer`，再把临时目录合并回游戏目录。
- 本项目按同样流程实现，并修复本地库（无下载 URL）被 `LibraryResolver` 丢弃的问题。

### 实现

- `OptiFineService`：
  - BMCLAPI `optifine/versionList` 列表；
  - 官方 `adloadx` 页面解析 `downloadx` 签名地址；
  - 临时 home 安装器运行（`-Duser.home` + `-cp jar optifine.Installer`）；
  - 合并 versions/libraries、检测新版本、`VersionInstaller` 补全；
  - `LibraryResolver` 允许无 URL 的本地库进入 classpath。
- CLI：`pcl optifine versions/install <mc>`。
- 模组页新增 OptiFine 卡片：游戏版本、版本列表、安装、进度、取消。
- `MinecraftVersionUtil` 将 launchwrapper/optifine 判定为 modable（实例隔离）。

### 验证

- `pcl optifine install 1.20.1 --patch I6` → `1.20.1-OptiFine_HD_U_I6`。
- 启动窗口 `Minecraft* 1.20.1`，日志 `[OptiFine] *** Reloading custom textures ***` 等。
- GUI：1.21.1 下显示 8 个 OptiFine 版本；原版版本下拉不再混入 OptiFine。
- 测试：69 个通过（新增列表解析）。

## Mod 更新检查 ✅

### 实现

- `ModrinthClient.GetVersionByHashAsync`：按本地 jar 的 SHA1 反查 Modrinth 版本。
- `ModUpdateService`：逐个启用 Mod 计算 SHA1 → 反查项目 → 找同游戏版本/加载器的最新版本 → 返回更新。
- `InstallFileAsync`：下载指定版本文件，已存在则跳过。
- 模组页本地列表显示“可更新 → 版本”，新增“检查更新 / 更新选中 / 更新全部”；更新后删除旧文件并刷新。
- `UpdateAllModsAsync` / CLI `--apply`：遍历更新列表，下载主文件、删除旧 jar。
- CLI：`pcl modrinth updates --instance <id>`。
- 启动参数：`--mod-updates`、`--mod-update`、`--mod-instance` 用于验证。

### 验证

- 临时放入旧 JEI `15.0.0.12`，检查输出：
  `jei-...-15.0.0.12.jar  15.0.0.12 → 15.71.0.224`
- GUI：Forge 实例显示“发现 1 个可更新 Mod”，列表标记可更新；“更新全部”按钮。
- CLI `--apply`：旧 JEI 被替换，只保留最新版。
- 执行更新后 mods 目录仅保留 `jei-...-15.71.0.224.jar` 与 `mezz_config`。
- 测试：69 个通过（当前总计）。

## 资源包启用/禁用 ✅

### 实现

- `InstanceContentItem` 增加 `IsEnabled/StateText`。
- `InstanceContentService`：
  - 读取 `options.txt` 的 `resourcePacks` JSON 数组；
  - 启用/禁用时增删 `file/<资源包文件名>`；
  - 无 `options.txt` / 空列表时默认禁用；
  - 保留其它 options 行。
- 资源页显示“已启用/已禁用”，按钮动态“启用/禁用”。

### 验证

- 单元测试：`resourcePacks:["vanilla"]` → 列表禁用 → 启用写入 `file/pack.zip` → 列表启用 → 禁用移除。
- GUI：资源包列表显示状态，按钮随选中项切换。
- 测试：67 个通过。

## 资源/光影/存档导入 ✅

### 实现

- `InstanceContentService.Import`：
  - 资源包/光影：zip 直接复制，目录递归复制；
  - 存档：目录递归复制；zip 解压到 `saves/<name>`，若 zip 只有一个顶层目录则自动去掉该层；
  - 路径/重名校验。
- 资源页新增导入控件：类型（资源包/光影/存档）、路径、导入。
- CLI：`pcl content import <resourcepack|shader|world> <path> --instance <id> [--name]`。

### 验证

- CLI 导入 `WorldImportCli.zip` → `saves/CliWorld/level.dat`。
- CLI 导入 `TestImportPack.zip` → `resourcepacks/TestImportPack.zip`。
- GUI：资源页显示导入控件；列表刷新为 3 资源包/2 光影/2 存档。
- 单元测试：资源包 zip、存档 zip（含顶层目录）；总测试 64 个通过。

## 更新清单工具链 ✅

### 实现

- `scripts/make-latest.sh`：读取安装包，生成含 `version/url/sha256/notes/size` 的 `latest.json`。
- `scripts/serve-updates.sh`：用 `python3 -m http.server` 在本地提供更新源。
- 更新器已验证可消费该格式。

### 验证

- `make-latest.sh 0.2.0 artifacts/PCL-linux-x64-0.1.0.tar.gz http://127.0.0.1:8765/...` 生成合法 JSON，sha256/size 正确。
- 托管时只需把 `latest.json` 与安装包放到同一静态目录，或使用 GitHub Releases 的直链。

## Java 管理 ✅

### 实现

- 设置页 Java 行改为：已发现 Java 下拉、扫描、自动、自定义路径。
- `SettingsViewModel.LoadAsync` 在首次进入设置页时调用 `JavaLocator.DiscoverAsync(forceRefresh: true)`。
- 选择运行时写入全局 `JavaPath`；保存持久化；输入自定义路径仍可用。
- “自动”清空路径，由启动流程按版本发现/下载 Mojang Runtime。

### 验证

- 设置页扫描结果：Java 25 / 21 / 21 / 17 / 11 / 8。
- 下拉显示 `Java <major> (<arch>) <home>`，自动/扫描按钮正常。
- 测试：63 个通过（无新增单测）。

## 实例重命名/删除 ✅

### 对照原版

- PCL `McInstance.Rename`：移动版本目录 → 重命名 jar/natives → 更新 `PCL/Setup.ini` 路径 → 改写 JSON `id`。
- 删除：`DirectoryUtils.Delete(PathVersion)`（Windows 默认回收站）。

### 实现

- `InstanceManager.Rename`：移动目录、改写 JSON id、重命名 `.jar`；非法名/重名校验。
- `InstanceManager.Clone`：递归复制版本目录与游戏数据，改写 JSON id、重命名 jar；目标重名自动加后缀。
- `InstanceManager.Delete`：删除版本目录（共享根目录的游戏数据不删）。
- CLI：`pcl clone <id> [--name]`、`pcl rename <old> <new>`、`pcl delete <id> --yes`。
- 实例页：重命名输入框、删除确认框、导出/导入入口。
- 实例变更后刷新 Launch/Mods/Content/Modpack 列表。

### 验证

- `pcl rename ImportedPack ImportedRenamed` → `ImportedRenamed.json` 的 id 正确，目录/jar 同步。
- `pcl delete ImportedRenamed --yes` → 目录移除。
- 单元测试：克隆/重命名/删除/重名拒绝；总测试 65 个通过。
- 克隆 E2E：`PCLTestPackCopy` 含 config/options/jar，启动 `Minecraft* 1.21.1` + Fabric 0.19.5。
- GUI：实例页显示导出、导入、重命名、删除、实例设置。

## 实例导出 ✅

### 实现

- `InstanceExporter`：仅对按实例隔离的版本导出，避免把共享根目录打包。
- zip 内容：
  - `pcl-export.json`（id/minecraft/loader/exportedAt）；
  - `instance/<id>.json`、`instance/<id>.jar`；
  - `mods/config/defaultconfigs/resourcepacks/shaderpacks/saves/PCL/kubejs`；
  - `options.txt`、`servers.dat` 等。
- 排除 `logs/natives/downloads/.fabric` 等运行时数据。
- CLI：`pcl export <实例> [--output]`；实例页新增“导出实例（备份 zip）”。
- `InstanceImporter`：读取 `pcl-export.json`、改写版本 id、解压 `instance/`、`VersionInstaller` 补全库/资源；CLI `pcl import`；实例页导入路径输入。

### 验证

- `pcl export PCLTestPack --output /tmp/PCLTestPack-export.zip`：
  - 4 个文件，25.6MB；
  - zip 含 `pcl-export.json`、`instance/PCLTestPack.json`、`.jar`、`config/pcl-test.txt`、`options.txt`。
- 单元测试：导出条目/排除 logs；总计 60 个测试通过。
- 原版共享目录实例会明确提示“不支持导出”。
- 导入闭环：`pcl import /tmp/PCLTestPack-export.zip --name ImportedPack` → id 改写、3888 文件补全，启动 `Minecraft* 1.21.1` + Fabric 0.19.5；总计 61 个测试通过。

## AppImage 打包 ✅

### 实现

- `scripts/package-appimage.sh`：self-contained 发布 → AppDir（PCL/AppRun/desktop/icon）→ appimagetool 构建。
- appimagetool 从 GitHub release 下载；`APPIMAGE_EXTRACT_AND_RUN=1` 兼容无 FUSE 环境。
- AppRun 转发参数到内置 `PCL`。

### 验证

- 产物：`artifacts/PCL-linux-x64-0.1.0.AppImage`（37MB）。
- `--appimage-version` 正常；`APPIMAGE_EXTRACT_AND_RUN=1 ... --page about` 启动窗口 `PCL Linux`。
- 本机无 FUSE，直接双击运行仍受限；需 `APPIMAGE_EXTRACT_AND_RUN=1` 或安装 FUSE。

## 资源包 / 光影下载 ✅

### 实现

- `ModrinthClient` 支持 `project_type:resourcepack/shader` 搜索；非 Mod 不要求 loader facet。
- `InstallContentAsync`：按游戏版本选版本，下载到实例 `resourcepacks/` 或 `shaderpacks/`，不解析 Mod 依赖。
- CLI：`modrinth search/install --type resourcepack|shader`，自动从 `--instance` 推导隔离目录。
- 资源页新增“从 Modrinth 下载”卡片：类型、关键词、结果、安装、进度；安装后刷新本地列表。
- `--content-search` 启动参数便于验证。

### 验证

- 搜索：faithful（资源包）99 个结果；bsl（光影）7 个结果。
- 安装：`Faithful 32x - 1.21.1.zip`（11,030,261 B）、`BSL_v10.1.8.zip` 进入实例目录。
- 断点续传：超时残留的 `.part` SHA1 与 Modrinth 一致，重跑后完成 rename。
- GUI：资源页显示 2 个资源包、2 个光影，搜索结果可选。

## 实例级设置 ✅

### 对照原版

- PCL 用 `Sources.Instance` 保存每实例独立的 `VersionRamType/VersionRamCustom`、`VersionArgumentJavaSelect`、`VersionAdvanceJvm/Game` 等，未设置时回落到全局。
- 本项目用 `versions/<id>/PCL/instance.json` 存同样语义的字段。

### 实现

- `InstanceSettings` / `InstanceSettingsStore`：MemoryMb、JavaPath、ExtraJvmArgs、ExtraGameArgs；空值回落全局。
- 实例页右侧新增“实例设置”：使用全局内存/Java、内存 MB、Java 路径、额外 JVM/游戏参数、窗口大小、自动加入服务器、保存/重置。
- 全局设置新增窗口宽高；启动与 CLI 读取实例/全局窗口大小，并把服务器地址转换为 `--server/--port`。
- 启动与 CLI 均读取实例设置；`CommandLineSplitter` 支持引号参数。
- `LaunchRequest.ExtraJvmArguments/ExtraGameArguments` 接到实际启动参数。

### 验证

- 单元测试：JSON 往返（含窗口/服务器）、重置、参数拆分（单/双引号、多空格）。
- CLI dry-run：`-Xmx2048M`、`-Dpcl.instance=1`、`--fullscreen` 均出现。
- GUI：Fabric 实例显示 2048 MB、两个额外参数，全局 Java 勾选时路径输入禁用。
- 测试：69 个通过（当前总计）。

## 删除确认 ✅

### 实现

- `ConfirmWindow`：标题、说明、取消/确定删除；`ShowDialog<bool>` 返回结果。
- `DialogService.ConfirmAsync` 从主窗口弹出。
- 本地 Mod、资源包、光影、存档删除前均确认；文案提示不可恢复，存档额外提示先备份。

### 验证

- 深色主题下弹出“删除存档”确认窗，标题/说明/按钮正常；取消返回 false，确定返回 true。
- 测试仍为 51 个通过（UI 弹窗无单元测试）。

## 深浅主题 ✅

### 实现

- `PclColors.axaml` 使用 Avalonia `ThemeDictionaries`（Light/Dark），统一提供 PclBlue1-8、文字、边框、卡片、页面、侧栏、输入框、日志背景、崩溃卡片颜色。
- `PclTheme.axaml` 移除写死颜色，控件全部改用 `DynamicResource`。
- `ThemeService.Apply` 设置 `Application.RequestedThemeVariant`（light/dark/system）。
- 设置页新增“主题”下拉；保存时立即应用并持久化。
- `--theme dark` 启动参数便于验证。

### 验证

- 深色启动截图：设置页、侧栏、输入框、ComboBox、卡片均为深色，文字可读。
- 原浅色资源保持默认；无硬编码 `Background="White"` 残留（已 grep）。

## 崩溃分析 ✅

### 对照原版

- PCL 的 `ModWatcher` 从日志识别 `Crash report saved to`、`Unable to launch`、`Exception in thread "main"` 等标志，再构造 `CrashAnalyzer` 收集 `latest.log` / `crash-reports` 做规则分析。
- 本项目实现精简规则版，覆盖常见原因。

### 实现

- `CrashAnalyzer`：读取实例 `logs/latest.log` 尾部 + 本次启动输出；匹配：
  - Java 版本过低（UnsupportedClassVersionError）
  - 内存不足（OutOfMemoryError）
  - Mod 依赖/兼容/重复
  - GLFW/OpenGL
  - 主类/ClassNotFound
  - NoSuchMethod/字段
  - 权限、损坏 zip
- 启动页新增红色崩溃卡片：标题、详情、建议。
- 正常退出（exit code 0 且无崩溃标志）不显示。

### 验证

- 单元测试 4 条（OOM、Java/Mod 依赖、正常退出、GLFW），总计 51 个测试通过。
- E2E：伪造 `mainClass=pcl.test.NoSuchMain` 的版本，Java 输出 `ClassNotFoundException`，启动页显示“主类/依赖缺失”。

## deb 打包 ✅

### 实现

- `scripts/package-deb.sh`：self-contained 发布 → `opt/pcl/PCL`、`usr/bin/pcl` 符号链接、desktop、hicolor 图标、文档、control/md5sums → `ar` 组装 deb。
- 依赖字段：libc6/libstdc++6/X11/fontconfig/GL/glib。
- 本机无 `dpkg-deb`，用 `ar`/`tar` 手工验证 deb 结构，并直接解包运行。

### 验证

- 产物：`artifacts/PCL-linux-0.1.0-amd64.deb`（约 40 MB）。
- `ar t` → `debian-binary`、`control.tar.gz`、`data.tar.gz`。
- control：Package/Version/Architecture/Installed-Size/Depends/Description 正常。
- 解包后 `/opt/pcl/PCL --page about` 启动窗口 `PCL Linux`。

## 资源 / 光影 / 存档 ✅

### 实现

- `InstanceContentService`：按实例目录枚举 `resourcepacks`（zip/目录）、`shaderpacks`（zip/目录）、`saves`（目录）。
- 删除资源包/光影/存档；存档备份为 zip 到 `~/.local/share/PCL/backups/worlds`。
- 新增“资源”导航页：实例选择、路径、数量、三栏列表、删除/打开目录/备份。
- 默认选中 Fabric 实例，便于查看按实例隔离的内容。

### 验证

- 单元测试：列表、备份 zip 内容、删除（47 个测试通过）。
- GUI：Fabric 实例显示 `TestPack.zip`、`TestShader.zip`、`TestWorld`，数量与路径正确。

## M7 自动更新替换 ✅

### 实现

- `UpdateService.PrepareAsync`：用 `System.Formats.Tar` + GZip 解包，拒绝绝对路径/`..`，定位 `PCL`，设置可执行权限。
- `UpdateService.ApplyAndRestart`：生成 `/bin/bash` 脚本，等待当前 PID 退出 → 原子 `cp` 到 `.new` + `mv` → 启动新二进制 → 自删脚本。
- `CanApplyInPlace` 仅对打包后的单文件 `PCL` 为真。
- 关于页新增“安装更新并重启”；下载后自动准备。
- `--auto-update <latest.json>` 启动参数用于自动化验证。

### 实测

- 打包 `PCL-linux-x64-0.1.0.tar.gz`，本地 HTTP 提供 0.2.0 清单与包。
- 启动 `./PCL --auto-update http://127.0.0.1:8766/latest.json`。
- 结果：初始 PID 54314 → 新 PID 54365；目标 `PCL` 的 SHA256 与更新包内 `PCL` 完全一致；替换脚本已删除；窗口重新出现。
- 测试：45 个通过（新增更新包解包/路径穿越测试）。

### 限制

- 仅 Linux 打包态；开发态与 Windows/macOS 未实现自动替换。

## 本地 Mod 管理 ✅

### 对照原版

- PCL 的 `PageInstanceMod` 用 `.disabled` / `.old` 后缀切换启用状态：禁用时把 `foo.jar` 改为 `foo.jar.disabled`（若已存在则 `.old`），启用时改回。
- 删除直接删文件；PCL 会弹确认框。

### 实现

- `LocalModService`：扫描 `mods/` 下 `.jar`、`.jar.disabled`、`.jar.old`；列出启用状态、大小；启用/禁用/删除。
- 模组页新增“已安装 Mod”卡片：实例 mods 路径、列表、启用/禁用、删除、打开目录、刷新。
- Modrinth 安装后自动刷新本地列表。

### 验证

- 单元测试覆盖 list/toggle/delete（43 个测试通过）。
- GUI：Fabric 实例显示 `sodium-fabric-0.8.13+mc1.21.1.jar` 为已启用，按钮与路径正确。
- 删除暂无二次确认。

## M7 — 打包 / 更新（部分）🟡

### 对照原版

- 原版 PCL 开源仓库中的 `UpdateCheckByButton` / `UpdateReplace` 是空实现（注释为“该版本中不包含更新功能”），无法参考。
- 因此自定义 `latest.json`：`version` / `url` / `sha256` / `notes`。

### 实现

- `UpdateService`：清单解析、版本比较、下载、SHA256 校验；`CheckAsync` 只返回比当前新的版本。
- CLI：`pcl update check/download <url>`。
- UI：设置页“更新源”、关于页“检查更新 / 下载更新包 / 进度 / 状态”。
- `scripts/package-linux.sh`：`dotnet publish` 自包含单文件 → 复制 desktop/icon/README/LICENSE → tar.gz + sha256。
- `packaging/`：`.desktop`、SVG 图标；`LICENSE.md`（MIT + 原版许可提示）。
- 测试：`PCL.Core.Tests` 覆盖版本比较与清单解析；总计 43 个测试。

### 实测

- `bash scripts/package-linux.sh 0.1.0` → 39 MB tar.gz，解压后 92 MB 单文件二进制，直接启动成功。
- 本地 HTTP 服务返回 `latest.json`（v0.2.0）→ `pcl update check` 识别新版本，`pcl update download` 下载 39 MB 并 SHA256 校验通过。

### 待补

- 自动替换/重启（Linux 运行中二进制替换需要辅助脚本或 AppImage 方案）。
- AppImage/deb、发布清单托管、下载源镜像。

## 下一步 M5 / M7 剩余

1. **Forge/NeoForge**：GUI 卡片 + NeoForge E2E。
2. **M5 剩余**：真实 Azure Client ID 的端到端登录。
3. **M7 剩余**：自动替换/重启、AppImage、发布清单托管。
3. **隔离/管理**：按实例隔离 mods、Mod 启用/禁用、CurseForge。
4. M3 剩余：安装队列/失败重试提示、镜像切换 UI 反馈。

## 环境备忘

- 本机：x86_64、31 GB、Wayland(niri)+XWayland、Java 8/11/17/21/25。
- 测试游戏目录：`/home/wenxiangl/pcl-test-mc`（约 873 MB）。
- PCL 下载的 Java：`~/.local/share/PCL/java/mojang-21`（约 105 MB）。
- 启动日志：`~/.local/share/PCL/logs/`。
