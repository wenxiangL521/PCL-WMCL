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
