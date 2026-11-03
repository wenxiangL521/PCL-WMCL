# 开发、测试与打包

## 环境

- .NET SDK 10.0.x（`global.json` 固定 10.0.402）
- Java 21（启动 1.21.1；启动器也会扫描/下载 Java）
- Linux 桌面（X11 或 Wayland/XWayland）；Windows/macOS 使用同一 Avalonia 代码

## 目录

```text
src/WMCL.Core/      路径、日志、HTTP、Job、文件工具
src/WMCL.Download/  并发下载、多源回退、断点、SHA1、进度
src/WMCL.Minecraft/ 版本 JSON、规则、库、资源、安装、加载器识别
src/WMCL.Launch/    Java、参数构建、账户、启动计划、微软/Authlib 认证
src/WMCL.Modding/   Fabric/Forge/NeoForge、Modrinth、CurseForge、整合包
src/WMCL.UI/        Avalonia 主题、颜色、样式
src/WMCL.App/       Avalonia 壳、页面、ViewModel、服务组合根
src/WMCL.Cli/       命令行入口
tests/              xUnit + Avalonia.Headless
```

## 职责边界

依赖方向为 `App/CLI → Launch/Modding/Minecraft → Download → Core`，UI 样式只由 App 引用。ViewModel 负责可观察状态、命令、对话框及过期请求保护；无需 UI 的准备、检索和存储逻辑放入服务，不通过拆分 partial 来隐藏业务耦合。

- `WMCL.Launch/LaunchPreparationService`：GUI/CLI 共用版本与安装清单读取、Java 选择/校验、实例目录解析和请求组装。显式入口参数覆盖实例设置，实例设置覆盖入口默认值；整合包最低内存仍生效。`LaunchPreferences` 表达入口默认值，`LaunchOverrides` 表达 CLI 等显式覆盖，`PreparedLaunch.CreateRequest` 在账号确定后组装启动请求，不负责界面、登录或进程启动。
- `WMCL.Launch/PreLaunchCommandRunner`：启动前命令占位符展开、进程输出及取消；桌面入口调用，CLI 保持原先不运行启动前命令的行为。CLI 的实例初始堆/GC 策略也保持原先未应用的行为，通过 `IncludeInstanceJvmTuning=false` 显式表达，避免重构隐式改变启动参数。
- `WMCL.App/Services/ModCatalogService`：跨平台 Mod 搜索、分页、版本目录和兼容性计算；仅依赖平台客户端，页面保留展示投影与请求序号校验。
- 配置分为 `AppSettings` 数据模型、`AppSettingsNormalizer` 规范化、`AppSettingsSerializer` 内存 JSON/脱敏、`IAppSettingsStore`/`FileAppSettingsStore` 显式路径磁盘存储。`SettingsService` 保留兼容门面，动态读取平台配置路径并执行原有迁移；独立存储测试不需要改真实用户配置。
- `WMCL.Minecraft/GameFileVerificationService`：GUI/CLI 真正启动前共同执行的客户端、平台库/native 归档、资源索引/对象与日志配置校验；已知 SHA1/size 始终验证，完好文件不联网，缺损才按镜像补全。仅使用本地 profile/继承链，不覆盖 profile、mods/config/saves；没有 hash/size 时只能保证存在，缺失且无来源必须停止启动。原版 `DlClientFix` 仅作为流程参考，不直接依赖 PCL 程序集。旧资源映射由可取消的 `LegacyAssetExtractor.EnsureAsync` 修复，native 解压仍由 `GameLauncher` 负责。
- `ClientJarPathResolver` 让校验和 `LaunchPlanBuilder` 使用同一客户端路径，支持 profile 中显式的 `downloads.client.path`。CLI 的 `--dry-run` 不执行文件修复；真实启动使用与该请求分辨率一致的 RuleContext。GUI 校验阶段独立记录终态，避免后续 Java/登录失败污染文件校验历史。
- 任务管理将“业务快照”和“可观察展示项”分开：`_previousTasks` 保留每轮快照，展示项按 TaskId/Title 原位通知更新，子集合保持身份。高频详情仅使用剩余列宽、单行省略及完整 tooltip，百分比与操作列固定宽度；不再靠每次替换列表项刷新进度，也不固定整个窗口高度。
- 页面 `IsBusy` 可能仅表示版本目录获取，不能直接作为后台安装身份。Minecraft 目录加载不得启用取消/暂停；Fabric 任务使用 `IsBusy && CanCancel`，Forge 任务使用独立 `IsInstallingForgelike`，`IsForgelikeBusy` 仍负责目录加载/页面禁用。任务列表、总体进度、速度统计和任务球必须使用同一判定，不得为目录加载创建安装终态历史。
- 实例页是固定二级壳：`MainWindowViewModel.CurrentPage`/返回栈只管理跨页面进入与离开；壳内 `InstanceViewModel.SelectedSectionIndex` 管理十个分区，`EmbeddedPage` 通过现有 Application.DataTemplates 复用同一 `Install`/`Mods`/`Content` 模型与页面，不复制安装/资源管理业务。概览、设置、导出、服务器保留现有卡片，截图复用 `ScreenshotView`；所有右侧分区使用 `WmclPagePadding` 和统一实例选择器。
- `MainWindowViewModel.Instances` 负责分区准备、root/id/section/request 校验及可等待任务；同类型资源分区保留同一宿主模型，准备时隐藏/禁用旧目标内容。隐藏实例页刷新不得更改独立下载页的共享模型模式；回到实例壳时重新准备管理上下文。壳内切分区不 PushPage，进入壳隐藏全局侧栏，返回时恢复原有全局导航。克隆主动选中新实例时，壳选择器与内嵌安装目标一起切换，原实例文件保留。
- `AppServices` 保持手工组合根，不引入大型 DI 容器。整合包更新、账号刷新、运行状态和崩溃界面仍由入口协调；后续按独立用例提取，不将 AppSettings 或 Avalonia 类型传入底层启动库。

## 安全边界

- 外部配置只能通过 `SettingsService.ImportFromFile/Json` 导入，统一执行 `AppSettingsImportPolicy`，与可信本地 `FileAppSettingsStore.Load` 分离；页面应用时再次跳过执行类字段。异常数值在修改当前设置前拒绝，批量应用暂停自动保存，完成后明确保存。
- `FileAppSettingsStore` 采用同目录临时文件、刷新与原子替换，保留有效备份和损坏副本。`LoadWithStatus` 返回恢复诊断，由启动界面显示，不在读取失败时静默覆盖原文件。
- ZIP/mrpack/mcbbs 导出共用 `Core/IO/ExportSafety` 的逐段链接/reparse 校验、受检枚举和临时归档提交。托管校验不等于 OS 原子 no-follow，不能声称消除恶意并发路径替换的 TOCTOU。
- `InstanceImportMigration` 在隔离暂存根迁移 profile、受管路径、覆写根和变化文件哈希；仅将确有导出且符合哈希的原受管内容纳入本机修复归档。旧外部绝对归档不作为修复来源，用户修改及存档保护保留；未导出共享依赖不保证离线完整。
- `StagedAssetIndexResolver` 在新建/修改实例的暂存发布前处理同 ID 不同内容索引：仅当前 profile 直接引用且 SHA1/size/JSON 校验通过、非 virtual/map_to_resources 的索引可使用 `id-完整SHA1` 别名，重写当前暂存 assetIndex.id 并更新受管路径/配置哈希，保留 URL 与下载校验元数据；旧共享文件/其他实例不改。未验证或 legacy 映射冲突、别名碰撞和普通库冲突仍拒绝。取消回滚避免移除已被其他 profile 引用的索引别名，但不宣称提供跨进程共享缓存事务。
- `DownloadManager` 在提交前核验已知大小、206 范围与实际正文。无 SHA1 不续接旧 `.part`，有 SHA1 最终校验后才提交。长度问题可能提前由下载器拒绝，沿用 `DownloadException` 包装并以 `InvalidDataException` 为根因；上层回归不得只依赖原先较晚的大小检查异常。

用户可见行为与恢复步骤见 [SETUP.md](SETUP.md)。

## 构建与测试

```bash
dotnet build WMCL.slnx -c Release
dotnet test  WMCL.slnx -c Release
dotnet run   --project src/WMCL.App -c Release
```

- 四个测试项目通过共享 `tests/TestEnvironment.cs` 的模块初始化器，在任何用例运行前将 XDG 配置/数据/缓存路径重定向至各进程独立临时目录，兼容门面与旧 PCL 路径迁移也必须留在该目录内。不要让自动保存、账号存储或截图测试写入真实用户配置；涉及 `ProxyManager.Apply` 的测试应恢复原代理状态。截图归档另用 `WMCL_SHOT_DIR` 指向临时输出，不能覆盖仓库历史截图。
- App 测试会启动 Avalonia Headless，部分用例要求串行执行。
- 目标测试可用 `--filter FullyQualifiedName~...`。
- 格式检查：

```bash
dotnet format whitespace WMCL.slnx --verify-no-changes --no-restore
git diff --check
```

## 打包

### Windows 单文件

```bash
dotnet publish src/WMCL.App/WMCL.App.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false \
  -o dist/win-x64
# 产物名为 WMCL.App.exe；发布为 WMCL.exe：
mv dist/win-x64/WMCL.App.exe dist/win-x64/WMCL.exe
```

产物为 `WMCL.exe`。保留 `WinExe`，否则 Windows 会弹控制台。不要开启裁剪，避免 Avalonia/XAML/反射被裁坏。程序集名保持 `WMCL.App`（Avalonia 资源前缀 `avares://WMCL.App/` 依赖它），只重命名发布产物。

### Linux

`scripts/package-linux.sh`、`package-appimage.sh`、`package-deb.sh` 生成 tar.gz/AppImage/deb；`packaging/wmcl-linux.desktop` 与 `wmcl-linux.svg` 是桌面入口。

### tag 自动发布

推送 `v*` 标签触发 `.github/workflows/release.yml`：先通过 `workflow_call` 复用 `ci.yml` 的三平台测试与 Linux 格式/发布检查，验证成功后三平台独立打包（Linux 走 `package-linux.sh`；Windows/macOS 单文件 publish，`DebugType=None` 统一去符号）。产物附 `.sha256`，三个打包任务全部成功后汇总上传草稿 Release。工作流的真实执行结果以 GitHub Actions 为准，本地构建不能替代三平台运行验证。

## 验证分类

不同证据不能互相替代：

1. **Headless**：ViewModel、绑定、动画中间状态、取消/失败回归。
2. **静态截图**：只证明某一帧布局。
3. **原生交互**：真实窗口、鼠标/键盘、焦点、DPI。
4. **动画录像**：逐帧/方向/快速反转。
5. **公共服务烟测**：Mojang/Modrinth/CurseForge 等真实接口。
6. **真实 Minecraft E2E**：实际启动、进入世界、声音/输入/长时间运行。

提交前至少跑全量自动测试；涉及 UI 的改动应说明证据属于哪一类。当前摘要见 [VERIFICATION.md](VERIFICATION.md)。

## 文档政策

- 不为每次修复新增独立 `*_VERIFICATION.md`；更新 `VERIFICATION.md` 的摘要。
- 历史验证证据放在仓库外，不提交 TRX、日志、临时截图和安装目录。
- 新增文档保持聚焦：开发、配置、验证、UI 对照、发布各一篇即可。

## 参考仓库

`PCL2`、`PCL-CE`、`PCL-ME` 仅作为行为/UI 对照，保持只读，不复制其构建产物或密钥。
