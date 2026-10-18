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

## UI 对齐 PCL2：主壳与启动页 ✅

### 对照原版

- PCL2：850×500、无边框、48px 蓝色顶栏、顶部五 Tab（启动/下载/联机/设置/更多）。
- 启动页：左侧 300px 账户/版本/启动操作区，右侧“启动日志”卡片。
- 页面级子导航：下载/更多有 150px 左侧列表；设置/启动无子导航。

### 实现

- `MainWindow` 改为无边框、850×500、自定义标题栏（拖动/最小化/关闭/边缘缩放）。
- `MainWindowViewModel` 重写为 TopTabs + SubTabs + CurrentPage 路由；保留 `--page` 深层链接。
- `LaunchView` 重做：账户/内存、版本选择、安装/刷新、启动按钮、日志卡片、崩溃卡片。
- 新增 `LinkView`（PCL2 层级占位，明确标注未实现）。
- 卡片/按钮/输入框改为 PCL 风格圆角 5、白色半透明卡片、浅蓝页面背景。

### 验证

- 850×500 实机截图 `docs/screenshots/m25-pcl-shell.png`。
- 顶部五个 Tab 可切换；`--page mods` 进入下载 → Mod 子导航。
- 启动页可显示已安装版本、离线账户、日志区域。

### 第二批：页面层级拆分

- 下载：原版游戏 / 加载器 / Mod / 整合包 / 资源包 / 光影包 / 存档 / 实例管理。
- 加载器页与 Mod 搜索页分开；Mod 搜索与本地 Mod 合并为单列。
- 设置：启动 / 个性化 / 其他，三组独立卡片。
- 资源：资源包 / 光影包 / 存档三个子页，每次只显示一类列表。
- 原版下载、整合包、实例、关于页统一为 PCL 卡片标题与宽度。
- 截图：`m26-pcl-loaders.png`、`m27-pcl-settings.png`、`m28-pcl-content.png`、`m29-pcl-instance.png`、`m30-pcl-about.png`。
- 视觉精度：PCL 矢量 Logo、窗口控制图标、账户头像首字母、PCL2 的 8px 透明边距 + 圆角 + 阴影；启动页压缩后无按钮重叠。
- 顶栏五个 Tab 改用原版 FormMain 的矢量 Logo（Path），启动按钮改为全宽，窗口阴影减弱。
- 页面内容使用 `TransitioningContentControl` + `CrossFade` 切换；页面底色改为浅蓝渐变。
- “更多”补成 PCL2 的五个子页：帮助 / 关于与鸣谢 / 百宝箱 / 反馈 / 新功能投票；百宝箱按原版开源说明保留“不包含百宝箱功能”的文案。
- 设置补成四项：启动 / 联机 / 个性化 / 其他；联机设置页保留 PCL2 层级并注明尚未实现。
- 截图：`m33-pcl-more.png`、`m34-pcl-settings-4.png`。
- 启动页账户区改为 PCL2 的“正版 / 离线”单选；正版项打开微软设备码登录，离线项保留自定义玩家名。
- 启动页新增“版本设置”，跳转到对应实例；实例管理从“下载”子导航移到“更多”，下载分类更接近 PCL2。
- 帮助页从静态占位改为可搜索帮助：8 个主题、关键词过滤、左侧列表 + 右侧详情。
- “下载”新增数据包分类：选择目标存档后，可搜索 Modrinth 数据包并安装到 `saves/<world>/datapacks`。
- “原版游戏”改为 PCL2 单列安装页：Minecraft / Fabric / Forge+NeoForge / OptiFine 四张卡片；移除独立“加载器”子导航，`--page loaders` 兼容到安装页。
- 安装页版本下拉补 `Id/已安装` 模板，避免显示类型名。
- 个性化设置新增背景图：文件选择、路径持久化、主窗体背景层；修改后重启生效。
- 背景图支持不透明度 0–100% 与模糊 0–30px，实时预览在重启后生效。
- 动画：页面 `CrossFade` 160ms；左侧栏宽度 0↔150 180ms；卡片错峰淡入/上浮（32ms 间隔，约 130ms）；按钮悬停 1.02/按下 0.97；窗口入场 0.985→1 缩放 220ms；控件颜色过渡 120ms。
- 修复顶栏切换时 `_navigating` 抑制子页处理、导致“页面切换但内容不刷新”的问题。
- 用临时 `PCL_UI_CYCLE` 轮播五个 Tab 验证内容刷新和动画不崩溃，验证后移除测试钩子。
- 安装页新增固定底栏“同时安装加载器 → 开始下载”，一次完成原版 + Fabric/Forge/NeoForge/OptiFine 的组合安装。
- 主窗右下角新增全局任务球（百分比），任一安装/下载时显示；点击跳转对应任务页。
- 实测组合安装流程：Minecraft 已装时进入 Fabric 安装，任务球显示 100%，但 Fabric 0.20.0 下载在测试网络下长时间未完成，未能端到端确认最终实例。
- 任务管理：`TaskManagerViewModel` 聚合安装/下载/整合包/资源/导入/组合安装任务，任务球点击打开独立任务窗口，支持逐项取消与取消全部。
- 兼容规则：新增 `LoaderCompatibility` 与 5 个单元测试，共 76 个测试。
- 合并实例：`MergedInstanceBuilder` 把加载器版本展平为独立 JSON、复制客户端 jar；组合安装不再只是重命名基础实例。
- 安装清单：`PCL/install.json` 记录 minecraft/loader/optifine 等组件与 OptiFine 文件 SHA1，对应 PCL2 `McInstallManifest` 的最小可用子集。
- 第三方登录：新增 Authlib/Yggdrasil authenticate/refresh/validate、账号持久化、authlib-injector 下载/缓存/`-javaagent` 注入；登录页新增“第三方”入口。
- 资源平台：资源页新增 Modrinth/CurseForge 来源选择；CurseForge 支持资源包/光影/数据包搜索与下载，数据包安装到存档 `datapacks`。
- CurseForge 整合包：解析 `manifest.json`、安装 Fabric/Forge/NeoForge、展平实例、解压 overrides、按 project/file id 下载文件；整合包页支持平台切换和本地 CF zip 识别。
- 实例认证：`InstanceSettings.AuthServer/AuthServerName`，启动时校验实例认证服务器与第三方账号一致，并使用该服务器注入 authlib-injector。
- 多账号/皮肤：Authlib 账号库改为多账号列表，实例认证服务器可自动选择匹配账号；sessionserver 拉取皮肤纹理并缓存 PNG，启动页头像显示第三方皮肤。
- 本地整合包：新增 MMC/HMCL/MCBBS 识别、组件解析、加载器安装、游戏目录覆写解压；本地 zip 安装支持这三种格式。
- 主题：新增 HSL 色相/饱和度/亮度设置，运行时重写 PclBlue 色系、页面渐变、顶栏渐变和 Fluent SystemAccent；已用橙色色相实机验证。
- 背景：支持单图或背景文件夹随机图，主题色叠加；有背景图时根容器透明、卡片/侧栏半透明，背景可透出。
- 任务统计：`DownloadMonitor` 上报全局速度/剩余文件/最大线程，任务管理窗口实时显示。
- Logo/音乐：自定义 Logo 显示在顶栏；背景音乐支持文件夹扫描、随机顺序、音量、启动播放，使用 ffplay，窗口关闭停止。
- 账号切换：启动页多 Authlib 账号下拉，直接切换第三方账号；账号区压缩为单行，保持 850×500 布局。
- 微软皮肤：Minecraft Services profile 拉取 skins/capes，缓存 PNG 并显示在启动页头像。
- 音乐联动：`IsGameRunning` 事件驱动，游戏运行时停止音乐、退出后恢复（重新随机播放）。
- Mod 版本选择：选中项目后加载 Modrinth/CurseForge 兼容版本列表，按所选版本安装；CurseForge 支持 fileID + 必需依赖递归。
- Modrinth 依赖：指定版本按 required dependency 的 versionId 精确解析，缺失时回退 projectId 兼容版本。
- 资源版本：资源包/光影/数据包选中项目后加载 Modrinth/CurseForge 兼容版本，按所选版本下载到 shaderpacks/resourcepacks/存档 datapacks。
- 实例导出选项：可选择 Mod/配置/资源包/光影/存档/PCL 配置/options.txt，导出器按选项过滤。
- 启动前命令：全局 + 实例设置支持命令、等待/不等待；支持 `{java}/{minecraft}/{verpath}/{name}` 占位符，取消时结束子进程。
- 启动进程：全局进程优先级（普通/高于普通/高/低于普通/空闲）与高性能显卡环境变量（DRI_PRIME/NVIDIA）。
- 启动器可见性：`HideLauncherOnGame` 开启时，游戏运行隐藏主窗口，退出后恢复。
- 整合包版本：Modrinth/CurseForge 选中项目后加载兼容版本，按所选版本下载 zip，再走统一整合包安装器。
- 本地包设置：MMC 迁移 JVM 参数/启动前命令/自动进服；MCBBS 迁移 launchInfo 的 Java/游戏参数。
- 实例管理：支持收藏与描述，收藏实例优先排序，列表显示 ★ 与描述。
- 主题预设：龙猫蓝/甜柠青/小草绿/菠萝黄/橡木棕/玄素黑/铁杆粉/神秘紫/秋仪金/活跃橙/跳票红/极客蓝，选中后同步 HSL。
- 自定义主页：本地 `.axaml` 通过 AvaloniaRuntimeXamlLoader 加载，DataContext 指向 LaunchViewModel；启动页右侧“主页/启动日志”标签。
- 任务树：组合安装维护原版/加载器/OptiFine/OptiFabric/Fabric API/合并/清单阶段，任务窗口显示子任务状态。
- 页面动画：PageHost 增加 160ms 淡入 + 8px 上浮过渡，与卡片错峰/侧栏宽度叠加；自动轮播冒烟无卡死。
- CLI 服务器：`pcl server list/ping/add/edit/remove`；支持 `--isolation` 与 `--json`，实测增改删/JSON 输出通过。
- Mod 分页：Modrinth offset / CurseForge index 接入 Mod 搜索，“加载更多”追加 20 条。
- 资源/整合包分页：资源搜索与 Modrinth/CurseForge 整合包搜索也支持“加载更多”。
- 版本隔离：全局自动/隔离/不隔离 + 实例覆盖；Launch/Mods/资源/OptiFine/整合包/导出路径统一走 IsolationPolicy；CLI 新增 `--isolation`。
- 任务阶段：组合安装子任务会显示当前阶段的真实进度/阶段名，而不是仅 0/100。
- 手动联机：下载 EasyTier core，房间码派生网络名/密钥，公共中继启动；支持多个中继，默认文档节点。联机页显示虚拟 IP/公网 IP/NAT/节点名/peer 列表；30 秒无虚拟 IP 给出超时提示。非 PCL2 官方房间服务。
- 帮助中心：扩展到 16 个主题，新增安装冲突、隔离、服务器列表、第三方登录、EasyTier、启动命令、导出、主题/主页等；支持类别筛选。
- 中继测试：联机页可逐个 TCP 测试中继延迟/错误，辅助判断 DNS/端口/防火墙问题；新增“自动选择中继”，并行测试后选延迟最低的 TCP 中继。
- 窗口图标：新增 `Assets/pcl-icon.png`（PCL 风格），通过 `WindowIcon`/AssetLoader 设置窗口与任务栏图标。
- Linux 桌面项：`scripts/install-desktop.sh` 安装 `.desktop` 和 256×256 图标到 XDG 应用菜单，临时 HOME 实测通过。
- Nide：第三方登录窗口增加服务器 ID → `auth.mc-user.com:233/<id>`；NormalizeServer 兼容以 `/authserver` 结尾的地址。
- 资源项目页：资源/光影/数据包、Mod、整合包搜索均可打开 Modrinth/CurseForge 项目网页。
- 任务历史：任务管理窗口记录最近 20 个结束的任务，可清空；总进度改为任务权重（下载/组合/整合包权重 3，Forge 2，其余 1）。
- 服务器全部 Ping：实例页可并发（最多 8 个）Ping 所有服务器，每条显示人数/延迟。
- 服务器列表导入/导出：实例页可用文件选择器导入/导出任意 `.dat`，导入按地址去重。
- 旧版资源：安装后按 `AssetIndex.Virtual/MapToResources` 把 objects 映射到 `assets/virtual/<id>` 或 `resources`，带路径穿越保护。
- 安装清单：`PCL/install.json` 记录 components + 实例 JSON/jar/natives 的 SHA1 受管文件；实例页可校验；CurseForge/本地整合包也写入受管文件。
- Java UI：设置页可选 Java 8/17/21，显示阶段/百分比、支持取消，并把结果写入 Java 路径；新增“清理”删除 mojang-* 下载运行时。
- 崩溃分析：规则扩到 15 条，新增 Mixin、登录失效、端口占用、JVM 参数/文件缺失、存档损坏等；崩溃卡片可保存报告、诊断包（zip 含报告/latest.log/系统信息/Mod 清单）、打开日志/目录。
- 下载限速：全局令牌桶 `DownloadThrottle`，设置页可填 MB/s（0=不限），下载循环按全局桶等待。
- 实例图标/窗口：实例 LogoPath 显示在列表；主窗口启动时读取设置宽高（最小 810×470）。
- 服务器列表：`servers.dat` 最小 NBT 读写，实例页可添加/删除/保存；保留 icon 字段。
- 实例分类：InstanceSettings.Category，实例页可分类并按分类筛选，收藏仍优先排序；支持输入自定义分类并加入筛选列表。
- 导出路径：实例页可另存 zip/.mrpack 到指定路径；留空使用默认 exports 目录。
- Server List Ping：实现 1.7+ 状态协议，显示版本/在线人数/延迟/MOTD/favicon/玩家样本；支持 `_minecraft._tcp` SRV（多 nameserver、IPv4/IPv6、TTL 缓存+过期清理、TCP fallback），实测 play.hypixel.net；MOTD 彩色/删除线；favicon 写回/缩略图。
- CLI 隔离：新增 `--isolation auto|isolated|global`。
- 尝试 `PageSlide` 时 Avalonia 预编译 XAML 失效，已回退 `CrossFade`。
- 最终截图：`m32-pcl-launch-final.png`。

## HTTP 代理 ✅

### 实现

- 设置页新增“HTTP 代理”（`http://host:port`）。
- `ProxyEnvironment.Apply` 在创建任何 HttpClient 前注入 `HTTP_PROXY/HTTPS_PROXY/ALL_PROXY`，并保留已有 `NO_PROXY`。
- `Program.Main` 读取配置并应用；修改后需重启启动器。
- 所有使用默认代理的 HttpClient（下载、Modrinth、CurseForge、Java、更新等）均走代理。

### 验证

- 本地 Python CONNECT 代理监听 `127.0.0.1:8899`。
- 配置 `ProxyUrl` 后启动 GUI，代理日志记录：
  `CONNECT bmclapi2.bangbang93.com:443 HTTP/1.1`。
- 测试：69 个通过。

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
- 全局设置新增 `ExtraJvmArgs/ExtraGameArgs`，与实例参数按“全局 → 实例”顺序叠加。
- 启动与 CLI 均读取实例设置；`CommandLineSplitter` 支持引号参数。
- `LaunchRequest.ExtraJvmArguments/ExtraGameArguments` 接到实际启动参数。

### 验证

- 单元测试：JSON 往返（含窗口/服务器）、重置、参数拆分（单/双引号、多空格）。
- CLI dry-run：`-Xmx2048M`、`-Dpcl.instance=1`、`--fullscreen` 均出现。
- GUI：Fabric 实例显示 2048 MB、两个额外参数，全局 Java 勾选时路径输入禁用。
- 全局参数 E2E：配置 `-Dpcl.global=1`/`--demo`，GUI 启动 `PCLTestPack` 的 Java 命令行包含两项。
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

## 最终状态（Round 88）

### 验证

- `dotnet build -c Release`：0 warning / 0 error。
- `dotnet test -c Release`：127 个测试通过（Core 10 / Minecraft 77 / Launch 40）。
- 工作区干净。

### 已补全的可行差距

- 安装：原版/加载器组合、冲突规则、OptiFine/OptiFabric/Fabric API、legacy virtual assets、安装清单/受管文件/校验/修复。
- 账户：微软/离线/Authlib/Nide、实例认证服务器、微软皮肤上传与尺寸校验。
- 资源：Mod/整合包/资源包/光影/数据包/世界/插件，分页、版本、依赖、项目页、可选文件。
- 实例：隔离、分类、服务器列表（NBT/Ping/SRV/IPv6/导入导出）、导出路径、图标、修复。
- 任务：任务球/窗口/阶段树/权重/最近完成/下载限速。
- 联机：EasyTier 手动模式、多中继、自动选择、诊断（非官方房间）；EasyTier 失败自动/手动切换到陶瓦（HMCL Terracotta），支持房主创建/客人加入；已有陶瓦实例时复用端口，停止不误杀外部进程；陶瓦启动/等待房间可取消。
- 陶瓦 E2E：双 Terracotta 实例实测 `host-ok → guest-ok`，客人转发端口可连；记录在 `docs/TERRACOTTA_E2E.md`。
- 陶瓦会话：进入房间后每 3 秒轮询状态/玩家列表，异常会显示，不只是一次性“已就绪”。
- 其他：崩溃规则/报告/诊断包、Java 下载/取消/清理、桌面项、CLI 服务器命令。

### 仍属外部不可得 / 架构近似（不声称等价）

- PCL2 官方 EasyTier 房间服务：开源代码 `InitConfig` 明确抛异常，无法复刻；已提供陶瓦（Terracotta）替代方案（需下载外部二进制）。
- PCL2 官方更新/遥测/赞助/秘钥：开源版为桩或移除。
- `McInstallRequest` 多组件请求模型：当前为基础版本+单加载器合并近似。
- 完整 45 类崩溃规则、完整 Loader 任务权重、皮肤库/披风、服务端实例：未逐项对齐。
- Windows/macOS 打包与真实 Azure Client ID E2E：未验证。

结论：按“可行差距”完成；与原版 PCL2 仍有已知结构性/外部差异，不能声称逐行等价。
