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
- 下载进度在全部命中缓存时速度显示异常（仅统计实际传输字节）。
- 安装前磁盘空间检查、下载失败重试上限可配置。
- 旧版本（`minecraftArguments`、legacy assets、`virtual`）尚未实测。

## M2 — Avalonia 壳 ✅

### 已实现

- `PCL.UI`：PCL 蓝色系主题（取自原版 `Application.xaml`）、卡片/按钮/输入框/列表/进度条样式。
- `PCL.App`：组合根、`MainWindowViewModel`、导航（启动/下载/实例/设置/关于）。
- 启动页：版本列表（已安装优先）、筛选、账户名、内存、安装/启动/取消、实时日志、进度与状态。
- 下载页：远程版本列表、筛选、安装、取消、进度。
- 实例页：已安装实例、版本目录/游戏目录入口。
- 设置页：游戏目录、内存、玩家名、Java 路径、下载源、保存与 Java 扫描。
- 关于页：版本、运行时、署名与许可提示。
- 日志清洗：把 log4j XML 控制台输出还原为可读消息（`LogSanitizer`）。
- 开发参数：`--game-dir <路径>`、`--auto-launch <版本>`，用于 GUI 端到端冒烟。

### 验证

- GUI 正常启动，窗口标题 `PCL Linux`，版本列表加载 120+ 个版本。
- 已安装版本显示“已安装”徽标，启动按钮可用。
- `--game-dir /home/wenxiangl/pcl-test-mc --auto-launch 1.21.1` 成功从 GUI 拉起游戏（窗口 `Minecraft 1.21.1`，Java 进程存活）。
- 日志面板实时显示 `Sound engine started`、纹理图集创建等；状态显示“已启动（Java 21），等待游戏退出…”。

### 修复/调整

- 默认选中项：优先已安装版本，其次最新正式版，避免一上来选中快照。
- 日志面板不再显示原始 XML。
- 启动时不再自动保存设置，避免 `--game-dir` 临时目录被写进配置。

### 待补

- 自动化 UI 测试（Avalonia.Headless）；GUI 安装按钮尚未端到端冒烟。
- 窗口图标、自定义标题栏、暗色主题。
- 日志滚动性能（当前按 200k 字符截断）。
- 下载页在“全部命中缓存”时的速度显示修正。

## 下一步 M3 / M4

1. **M3**：下载页完善——安装队列、校验修复、镜像切换、失败重试提示；GUI 安装 `1.21.1` 端到端冒烟。
2. **M4**：Java 缺失时自动下载 Temurin（按 MC 版本要求选 8/17/21），并校验版本/架构。
3. **M5**：微软设备码登录（需要 Azure 公共 client ID）。
4. **M6**：Fabric/Quilt 安装 + Modrinth 搜索/安装。
5. **M7**：AppImage/tar.gz 打包 + 自动更新。

## 环境备忘

- 本机：x86_64、31 GB、Wayland(niri)+XWayland、Java 8/11/17/21/25。
- 测试游戏目录：`/home/wenxiangl/pcl-test-mc`（约 873 MB）。
- 启动日志：`~/.local/share/PCL/logs/`。
