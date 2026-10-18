# PCL2 重写计划（.NET 10 LTS + Avalonia）

> 基线：`Hex-Dragon/PCL2` @ `ade6e04`（2.14.0.0，2026-10-17）
> 目标：**Linux 原生优先**，Windows / macOS 同源可构建；不做第三方项目的二次开发
> 文档版本：v0.1 · 2026-10-18

---

## 0. 结论

1. **这不是"迁移"，是"以 PCL2 为规格的跨平台重写"。** WPF 在 .NET 10 上仍然只有 Windows 实现，UI 层（63 个 XAML、26.5k 行 Pages+Controls）必须用 Avalonia 重做。
2. **逻辑层可以大量复用**：`Modules` 18.9k 行里约 **7–8k 行是平台无关的算法**（版本 JSON、规则、库、资源、下载、参数、Java 匹配、崩溃规则），按函数语义移植到 C#；约 4k 行是平台/UI 胶水，重写；其余是 UI 绑定代码，丢弃。
3. **推荐技术栈**：.NET 10 LTS + C# 14 + Avalonia 11.3 + CommunityToolkit.Mvvm + `Microsoft.Extensions.*` + SkiaSharp + xUnit。
4. **交付节奏**：先打通"命令行安装并启动原版 MC"（M1），再做 Avalonia 界面（M2），然后按功能横向扩展。**v0.1 不追求功能对齐**，只保证主链路可用。
5. **本机现状**：已装 .NET 10.0.13 **运行时**，但**没有 SDK**；有 Java 25、有 Wayland/X11 桌面、31 GB 内存、网络可达 Mojang/BMCLAPI/NuGet。M0 第一步是用户级安装 .NET 10 SDK（无 sudo）。

---

## 1. 可行性判断

### 1.1 为什么不能"改个 TargetFramework 就完事"

| 约束 | 事实 | 影响 |
|---|---|---|
| WPF | 只有 Windows 实现；.NET 10 的 WPF 仍为 `net10.0-windows` | UI 100% 重写 |
| .NET Framework 4.8 | 只随 Windows 提供；`System.Windows.Forms`、`System.Management`、`System.ServiceModel` 等不在跨平台运行时里 | 核心库必须迁 `net10.0` |
| VB.NET | .NET 10 仍能编译 VB 类库，但语言已冻结；WPF/WinForms 的现代工具链以 C# 为主 | 新代码用 C#，VB 仅作参考 |
| 平台假设 | 注册表、`cmd.exe`、`java.exe`、`;` 分隔 PATH、`windir`、`My.Computer`、Win32 拖拽 | 需要平台抽象层逐个替换 |

### 1.2 能复用什么

- **算法与协议**：版本清单/JSON、`inheritsFrom` 合并、`rules` 判定、库与 natives 解析、资源索引、下载多源回退、SHA1 校验、JVM/游戏参数拼装、Java 版本匹配、崩溃规则、整合包结构。
- **数据与常量**：官方/BMCLAPI 端点、版本映射、正则、默认参数、UI 文案。
- **设计 token**：PCL 的配色、圆角、字号、窗口尺寸（850×500，最小 810×470）。
- **不能复用**：WPF 控件/XAML、动画系统、`Loader` 任务框架、注册表配置、更新器、音乐/位图/拖拽等平台代码。

### 1.3 工作量判断

- 到 v0.1（原版安装 + 启动 + 基础 UI）：约 **25–35 个连续执行日**（含下载/调试；不含等待评审）。
- 到 v1.0（Fabric/Forge/Mod/整合包/资源包/崩溃分析/自动更新）：以**人月**计。
- 完整对齐 PCL2（含 CurseForge、OptiFine、皮肤、音乐、自定义主页、i18n、暗色等）：小型团队 **6–12 个月**。

> 结论：可行，但必须严格分阶段；任何"一次做完"的计划都会失败。

---

## 2. 目标与范围

### 2.1 v0.1（MVP，必须先完成）

- 版本列表：正式版/快照，官方源 + BMCLAPI 回退。
- 原版安装：version JSON、client jar、libraries（含 natives）、assets、logging config；SHA1 校验；断点/重试。
- Java：扫描系统 Java（PATH、`JAVA_HOME`、`/usr/lib/jvm`、SDKMAN、asdf、`~/.jdks`），按 MC 版本选 Java，缺失时自动下载 Temurin JRE。
- 账户：离线账户；微软设备码登录（M5，见风险）。
- 启动：classpath、natives 解压、JVM/游戏参数、日志窗口、退出码。
- 实例：`versions/<id>` 隔离，兼容原版 `.minecraft` 目录结构。
- UI：主窗口 + 启动页 + 设置页 + 日志页（PCL 风格）。
- 平台：Linux x64 优先；Windows/macOS 同代码可构建（后续验证）。

### 2.2 后续版本

| 版本 | 内容 |
|---|---|
| v0.2 | Fabric/Quilt 安装；Modrinth 搜索/安装；Mod 启用/禁用 |
| v0.3 | Forge/NeoForge（headless installer）；整合包导入/导出 |
| v0.4 | 资源包/光影/存档管理；崩溃分析；日志上传 |
| v0.5 | 自动更新；配置迁移（从 PCL2 注册表/目录导入）；i18n；暗色 |
| v1.0 | 功能对齐 + 稳定性 + 打包（AppImage/deb/zip/dmg） |

### 2.3 非目标

- 手机版/平板版。
- 像素级 1:1 复刻（先对齐布局、交互、配色，细节后补）。
- 兼容 PCL2 的插件/主题包（原版没有插件系统）。
- 复用 PCL2 的 WPF 控件、图标、字体（许可与平台原因）。
- 首个版本支持所有 Mod Loader / 所有下载源。

---

## 3. 原版代码评估

### 3.1 体量

| 区域 | 文件数 | 行数 | 说明 |
|---|---:|---:|---|
| `Modules`（VB） | 25 | 18,901 | 逻辑核心 |
| `Pages`（XAML+VB） | 90 | 19,699 | 页面 |
| `Controls`（XAML+VB） | 51 | 6,796 | 自定义控件 |
| XAML 合计 | 63 | 5,858 | 全部 WPF |
| `PCLCS`（C#） | 9 | 920 | 官方已抽出的 Java/启动工具 |
| 总计 | 113 VB | 43,091 | — |

### 3.2 模块复用矩阵

| 原版模块 | 行数 | 职责 | 处理方式 | 可复用度 |
|---|---:|---|---|---:|
| `ModMinecraft.vb` | 2,153 | 版本 JSON、`inheritsFrom`、库、资源索引、实例扫描 | 语义移植模型与解析 | 80% |
| `ModLaunch.vb` | 2,221 | JVM/游戏参数、natives、登录、进程、日志 | 移植参数/规则/natives；重写进程与登录 | 65% |
| `ModDownload.vb` | 1,389 | 版本/资源/库下载、校验、多源 | 移植调度与校验 | 75% |
| `ModNet.vb` | 1,933 | HTTP、镜像、重试、测速、代理 | 用 `HttpClient`+Polly 重写骨架，移植源选择策略 | 50% |
| `ModJava.vb` | 584 | Java 发现、版本匹配、自动下载 | 移植匹配算法；重写发现（Linux 路径） | 55% |
| `ModModpack.vb` | 861 | 整合包解析、覆盖文件 | 移植 | 60% |
| `ModCrash.vb` | 1,122 | 崩溃日志规则匹配 | 移植规则表 | 80% |
| `ModValidate.vb` | 424 | 文件/版本完整性校验 | 移植 | 85% |
| `Resource*.vb` | 2,285 | 资源项目/搜索/版本（Mod/整合包） | 移植模型与 API 调用 | 55% |
| `ModWatcher.vb` | 394 | 进程树监控、游戏退出检测 | 用 `/proc` 重写 | 20% |
| `ModLoader.vb` | 791 | 任务/进度框架（与 UI 强耦合） | 重写为 `IJobService` | 10% |
| `ModBase.vb` | 1,216 | 路径、日志、进程、剪贴板、对话框 | 部分移植，平台 API 重写 | 40% |
| `ModMain.vb` | 1,083 | 设置、更新、单例、遥测 | 重写 | 20% |
| `ModAnimation.vb` | 1,066 | WPF 动画 | Avalonia 重写 | 0% |
| `ModMusic` / `MyBitmap` / `DragHelper` | 666 | NAudio / GDI+ / OLE 拖拽 | 重写（OpenAL/Skia/Avalonia DnD） | 0% |
| `ModSecret.vb` | 202 | 加密/混淆 | 重做或删除 | 10% |
| `PCLCS`（C#） | 920 | Java/启动/版本工具 | **直接迁 `net10.0`** | 85% |
| `Pages` + `Controls` | 26,495 | 全部 UI | Avalonia 重写 | 0–10%（仅设计 token） |

**合计**：约 7.5k 行算法可移植（占 `Modules` 40% 左右）；约 4k 行需重写；UI 26.5k 行全部重写。

### 3.3 平台绑定清单与替换方案

| 绑定 | 原版位置 | Linux 替换 |
|---|---|---|
| WPF / XAML | 63 个 XAML、`Controls/*` | Avalonia 11.3 |
| .NET Framework 4.8 | `*.vbproj` | `net10.0` |
| 注册表设置 | `Settings.vb`（`HKCU\Software\PCL`）、`Application.xaml.vb`（.NET 版本检查） | JSON 配置 + 一次性迁移工具 |
| `cmd.exe /c` | `ModLaunch.vb` 两处自定义命令 | `/bin/sh -c` |
| `java.exe` / `javaw.exe` | `PCLCS/Java.cs`、`ModLaunch.vb` | `java`，重写 Java 发现 |
| PATH 用 `;` | `ModLaunch.vb` | `:` |
| `My.Computer`（28 处） | 内存/屏幕/键盘/剪贴板 | `IPlatformService` |
| `user32/shell32/ole32` | `DragHelper.vb` | Avalonia DragDrop |
| `System.Drawing` / `Imazen.WebP` / `NAudio` | 皮肤、图片、音乐 | SkiaSharp / OpenAL |
| 单实例 Mutex | `Application.xaml.vb` | 文件锁 / Unix socket |
| 自更新 exe | `ModMain.vb`、`--update` | AppImage/zip 更新器 |
| `Microsoft YaHei UI` | `Application.xaml` | 内置 Noto Sans SC（OFL） |
| 资源管理器 `\` 路径 | `ModBase.vb` `OpenExplorer` | `xdg-open` |

### 3.4 结论

原版代码的价值在**协议与流程**，不在实现。正确姿势是"**按函数移植算法，按页面重写 UI，按平台替换 API**"。

---

## 4. 技术选型

| 层 | 选择 | 理由 |
|---|---|---|
| 运行时 | **.NET 10 LTS**（`net10.0`） | 最新 LTS，支持到 2028-11；本机已有 10.0.13 运行时 |
| 语言 | **C# 14** | Avalonia 工具链、异步、源生成器、长期维护 |
| UI | **Avalonia 11.3.x** | XAML 语法接近 WPF，跨三平台，自定义控件/动画能力强 |
| MVVM | `CommunityToolkit.Mvvm` | 源生成器，少样板 |
| 基础设施 | `Microsoft.Extensions.DependencyInjection/Hosting/Logging/Options/Http` | 标准组合根 |
| JSON | `System.Text.Json` | 性能好；兼容性缺口处局部用 `Newtonsoft.Json` |
| 图像 | `SkiaSharp` | 皮肤/图标/截图，跨平台 |
| 压缩 | `SharpCompress` + `SevenZip`（按需） | zip/tar.gz/7z 整合包 |
| 重试 | `Polly` | 下载/HTTP 退避 |
| 日志 | `Microsoft.Extensions.Logging` + 文件 sink | 结构化、可上传 |
| 测试 | `xUnit` + `FluentAssertions` + `WireMock.Net` | 单测 + HTTP 仿真 |
| UI 测试 | `Avalonia.Headless` | 无显示环境跑 VM/控件测试 |
| 打包 | `dotnet publish` + AppImage / zip / dmg | 先做 tar.gz + AppImage |
| 更新 | GitHub Releases + `latest.json`（自研）或 Velopack | 先自研，依赖少 |
| CI | GitHub Actions（ubuntu/windows/macos） | 三平台构建 |

---

## 5. 目标架构

### 5.1 解决方案布局

```text
pcl-rewrite/
├─ PCL.slnx
├─ Directory.Build.props          # net10.0, C# 14, nullable, analyzers
├─ global.json                    # 固定 SDK 10.0.x
├─ src/
│  ├─ PCL.Core/                   # 平台无关：路径、配置、日志、HTTP、Job、事件
│  ├─ PCL.Download/               # 下载队列、分块、校验、镜像、限速
│  ├─ PCL.Minecraft/              # 版本清单、JSON 模型、规则、库、资源、natives、实例
│  ├─ PCL.Launch/                 # 参数构建、Java 选择、进程、日志解析
│  ├─ PCL.Accounts/               # 离线、微软 OAuth、authlib-injector
│  ├─ PCL.Modding/                # Fabric/Quilt/Forge/NeoForge、Modrinth、整合包
│  ├─ PCL.UI/                     # Avalonia 主题、控件、动画、转换器
│  ├─ PCL.App/                    # 组合根、页面、ViewModel、导航
│  ├─ PCL.Platform/               # IPlatformService + Linux/Windows/macOS 实现
│  └─ PCL.Cli/                    # 无 UI 的安装/启动入口（M1 验收用）
└─ tests/
   ├─ PCL.Core.Tests/
   ├─ PCL.Minecraft.Tests/
   ├─ PCL.Launch.Tests/
   └─ PCL.UI.Tests/
```

### 5.2 依赖规则

- `PCL.Core` 不依赖 UI、不依赖平台实现。
- `PCL.Minecraft` / `PCL.Download` / `PCL.Launch` 只依赖 `PCL.Core`。
- `PCL.App` 依赖 UI + 业务；`PCL.Platform.*` 通过 DI 注入。
- 禁止业务层引用 `Avalonia.*`；UI 通过 ViewModel/事件订阅。

### 5.3 关键接口

```csharp
public interface IGameLauncher
{
    Task<LaunchResult> LaunchAsync(LaunchRequest request,
        IProgress<JobProgress> progress, CancellationToken ct);
}

public interface IVersionInstaller
{
    Task InstallAsync(string versionId, InstallOptions options,
        IProgress<JobProgress> progress, CancellationToken ct);
}

public interface IDownloadManager
{
    Task DownloadAsync(IReadOnlyList<DownloadItem> items, DownloadOptions options,
        IProgress<DownloadProgress> progress, CancellationToken ct);
}

public interface IJavaLocator
{
    Task<IReadOnlyList<JavaRuntime>> DiscoverAsync(CancellationToken ct);
    Task<JavaRuntime> EnsureAsync(JavaRequirement requirement,
        IProgress<JobProgress> progress, CancellationToken ct);
}

public interface IPlatformService
{
    string ConfigDirectory { get; }
    string DataDirectory { get; }
    string CacheDirectory { get; }
    void OpenUrl(string url);
    void RevealInFileManager(string path);
    Task StoreSecretAsync(string key, string value);
    Task<string?> LoadSecretAsync(string key);
}
```

### 5.4 任务与进度模型

原版 `Loader`/`LoaderTask` 是 UI 驱动的树形任务，直接搬会再次耦合。替换为：

```csharp
public sealed record JobProgress(
    string Stage,
    double? Percent,
    long? BytesPerSecond,
    TimeSpan? Eta,
    string? Detail);
```

- 业务层只发 `IProgress<JobProgress>`，不碰 UI。
- UI 层用 `Dispatcher.UIThread.Post` 合并刷新（例如 10 Hz），避免进度刷爆。
- 所有长任务支持 `CancellationToken`，取消后清理 `.part` 文件。

### 5.5 配置与目录（XDG）

| 类型 | Linux | Windows | macOS |
|---|---|---|---|
| 配置 | `~/.config/PCL/` | `%APPDATA%\PCL\` | `~/Library/Application Support/PCL/` |
| 数据（实例索引、账户） | `~/.local/share/PCL/` | `%LOCALAPPDATA%\PCL\` | 同配置 |
| 缓存（下载临时） | `~/.cache/PCL/` | `%TEMP%\PCL\` | `~/Library/Caches/PCL/` |
| 游戏目录 | `~/.minecraft`（可改） | `%APPDATA%\.minecraft` | `~/Library/Application Support/minecraft` |

配置文件示例：

```json
{
  "schemaVersion": 1,
  "game": { "defaultGameDir": "~/.minecraft", "memoryMb": 4096 },
  "download": { "provider": "auto", "threads": 16, "mirror": "bmclapi" },
  "java": { "autoDownload": true },
  "ui": { "theme": "light", "language": "zh-CN" }
}
```

迁移：Windows 端跑一个 `pcl-migrate` 小工具，读 `HKCU\Software\PCL` 与安装目录，导出上述 JSON；Linux 端导入。**首版不做**，v0.5 再做。

### 5.6 日志

- 结构化文件日志：`logs/pcl-YYYYMMDD.log`，滚动保留 7 天。
- 游戏日志独立文件 + UI 实时滚动（保留最后 N 行，避免大日志卡顿）。
- 崩溃报告附环境信息（OS、Arch、运行时、Java、镜像、版本）。

---

## 6. 迁移与重写策略

### 6.1 三层迁移

| 层 | 做法 | 产出 |
|---|---|---|
| 核心层 | VB → C# **语义移植**，保留算法/常量/流程，去掉 UI 调用 | `PCL.Core`/`PCL.Minecraft`/`PCL.Download`/`PCL.Launch` |
| 平台层 | 重写：路径、配置、进程、Java 发现、桌面集成、密钥存储 | `PCL.Platform.*` |
| UI 层 | 按页面重写：先布局与交互，再动画与细节 | `PCL.UI` + `PCL.App` |

### 6.2 VB → C# 移植规范

1. **以函数为单位移植**，保留原函数名/注释/常量，便于对照原版。
2. **剥离 UI**：`Loader` → `IProgress<JobProgress>`；`MsgBox` → 返回 `Result`/抛异常/日志。
3. **剥离平台**：注册表 → 配置服务；`cmd.exe` → `IPlatformService.RunShellCommand`；`java.exe` → `java`。
4. **异步化**：阻塞下载/进程等待改 `async/await` + `CancellationToken`；CPU 密集放 `Task.Run`。
5. **加测试再移植**：先用原版行为写测试用例（如规则判定、参数拼装），再实现。
6. **不做逐行直译**：VB 的 `On Error Resume Next`、`GoTo`、隐式转换在 C# 里要显式化。

### 6.3 平台适配

- `IPlatformService`：目录、`xdg-open`、剪贴板、密钥存储、单实例、进程树。
- `IProcessService`：启动/等待/杀进程树；Linux 用 `/proc/<pid>/task` 找子进程。
- `IJavaLocator`：Linux 路径 + `java -XshowSettings:properties -version` 解析。
- `ISecretStore`：Linux 先落 0600 文件，后续接 Secret Service；Windows 用 DPAPI。

### 6.4 复用优先级（先做什么）

1. **版本 JSON / 规则 / 库 / 资源**（`ModMinecraft`）：启动的地基。
2. **下载与校验**（`ModDownload` + `ModNet`）：决定安装成功率。
3. **参数 / natives / 进程**（`ModLaunch`）：决定能不能启动。
4. **Java 匹配**（`ModJava`）：决定启动器是否"开箱即用"。
5. 其余（Mod/整合包/崩溃）在 UI 可用后做。

---

## 7. UI 重写方案

### 7.1 设计 token（直接取自原版 `Application.xaml`）

| Token | 值 | 用途 |
|---|---|---|
| `ColorBrush1` | `#343d4a` | 主文字 |
| `ColorBrush2` | `#0b5bcb` | 深蓝（按下/强调） |
| `ColorBrush3` | `#1370f3` | 主色（按钮/选中） |
| `ColorBrush4` | `#4890f5` | 悬停 |
| `ColorBrush5` | `#96c0f9` | 浅蓝 |
| `ColorBrush6` | `#d5e6fd` | 边框/浅底 |
| `ColorBrush7` | `#e0eafd` | 卡片底 |
| `ColorBrush8` | `#eaf2fe` | 页面底 |
| 灰阶 | `#404040` / `#737373` / `#8c8c8c` / `#a6a6a6` / `#cccccc` | 次要文字/分隔线 |

窗口：默认 850×500，最小 810×470；自定义标题栏；圆角 8–12；卡片阴影轻量。

### 7.2 页面结构

```text
主窗口
├─ 左侧导航：启动 / 下载 / 实例 / 设置 / 关于
├─ 顶部：标题、最小化/关闭、账户入口
├─ 内容区：页面容器（带切换动画）
└─ 底部：状态栏（任务、下载速度、版本）
```

| 页面 | v0.1 | 说明 |
|---|---|---|
| 启动 | ✅ | 左：版本列表/筛选；右：启动按钮、账户、日志摘要 |
| 下载 | 部分 | 版本列表 + 安装；后续加 Mod/整合包 |
| 实例 | 部分 | 版本卡片、打开目录、删除、设置 |
| 设置 | ✅ | 启动/Java/下载/界面/关于 |
| 关于 | ✅ | 许可、署名、反馈链接 |

### 7.3 控件清单（Avalonia 重写）

`MyButton`、`MyCard`、`MyMsg`（消息框）、`MyLoading`、`MyScrollViewer`、`MyTextBox`、`MyComboBox`、`MySlider`、`MyListItem`、`MyIconButton`、`MyRadioButton`、`MyHint`、`MySearchBox`。

原则：先做"够用"版本（默认模板 + 样式），不追求原版每个像素；交互一致优先。

### 7.4 动画与交互

- 页面切换：淡入 + 轻微位移（150–200 ms）。
- 按钮/卡片：悬停亮度/阴影过渡。
- 加载：旋转指示器 + 文案。
- 下载：进度条 + 速度/ETA，节流刷新。
- 暗色模式：v0.5（先用资源字典留好结构）。

### 7.5 字体与图标

- 字体：内置 **Noto Sans SC**（OFL）或使用系统 Noto CJK；**不使用微软雅黑**。
- 图标：FluentUI System Icons / Material Symbols（MIT/Apache），不复制 PCL 图标资源。
- 中文排版：行高 1.4–1.5，13 px 正文（对齐原版）。

### 7.6 验收方式

- 每个页面提供截图（本机 Wayland/X11）。
- 关键交互录制 GIF/短视频：选版本 → 安装 → 启动 → 看日志。
- 与原版截图并排对比（布局/配色/层级），记录差异清单。

---

## 8. 里程碑

> 工期为"连续执行日"估算，含编码、测试、调试；实际受下载/网络/评审影响。

### M0 — 工具链与脚手架（0.5–1 天）

- 用户级安装 .NET 10 SDK：`curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "$HOME/.dotnet"`，写入 `~/.bashrc` 的 PATH。
- `global.json` 固定 `10.0.x`；`Directory.Build.props` 统一 `net10.0`、`LangVersion 14`、`Nullable enable`。
- 创建解决方案与空项目；`.editorconfig`；CI 骨架。
- **验收**：`dotnet build`、`dotnet test` 全绿；`dotnet --version` 为 10.0.x。

### M1 — 核心库 + CLI 启动原版（3–5 天）

- `PCL.Core`：路径/配置/日志/HTTP/Job。
- `PCL.Minecraft`：版本清单、版本 JSON 模型、`inheritsFrom`、`rules`、libraries、assets、natives。
- `PCL.Download`：队列、并发、SHA1、`.part` 断点、官方/BMCLAPI 回退。
- `PCL.Launch`：Java 发现/选择、JVM/游戏参数、natives、进程、日志。
- `PCL.Cli`：`pcl install <id>`、`pcl launch <id> --offline <name>`、`pcl java list`。
- **验收**：
  - `pcl install 1.21.1` 生成 `versions/1.21.1/`、`libraries/`、`assets/objects/`；
  - `pcl launch 1.21.1 --offline Steve` 成功拉起游戏窗口；
  - 重复安装幂等；断网/坏源有可读错误；取消后无残留进程。

### M2 — Avalonia 壳 + 启动页（3–5 天）

- 组合根、DI、主题、主窗口、导航、页面容器。
- 启动页：版本列表（本地扫描 + 远程清单）、账户区、启动按钮、日志面板。
- 启动页接 M1 的安装/启动服务。
- **验收**：GUI 完成"选版本 → 启动 → 看日志"；截图/录屏。

### M3 — 下载/安装 UI（4–6 天）

- 版本列表（正式/快照/筛选）、安装对话框、进度/速度/ETA、取消/重试、修复。
- 镜像设置（自动/官方/BMCLAPI）。
- **验收**：全新目录下 GUI 安装并启动 1.21.1；中断后可续传。

### M4 — Java 管理（2–3 天）

- Java 列表、手动添加、自动下载（Temurin）、按版本匹配、警告。
- **验收**：隐藏系统 Java 后，一键下载 Java 21 并启动 1.21.1。

### M5 — 账户（3–5 天）

- 离线账户（UUID 生成、皮肤占位）。
- 微软设备码登录：OAuth → Xbox Live → XSTS → Minecraft → profile；刷新令牌加密存储。
- **前置条件**：需要 Azure 应用注册的公共 client ID（见 §11 风险）。
- **验收**：正版账号登录、启动、进入服务器；令牌过期自动刷新。

### M6 — 实例 + Fabric + Modrinth（5–7 天）

- 实例列表/创建/删除/复制；实例设置（内存、Java、游戏目录）。
- Fabric/Quilt 安装（meta API）。
- Modrinth 搜索/项目页/版本选择/安装/启用禁用/删除。
- **验收**：新建 Fabric 1.21.1 实例，安装 Sodium，启动成功。

### M7 — 打包与更新（2–3 天）

- `linux-x64` self-contained 发布；AppImage + tar.gz；Windows zip；macOS `.app`。
- `latest.json` + 下载/校验/替换；AppImage 自替换。
- **验收**：干净 Ubuntu 24.04 上解压即用；升级到新版本成功。

### M8 — 功能补齐（持续）

Forge/NeoForge、OptiFine、整合包（CurseForge/Modrinth）、资源包/光影/存档、崩溃分析、authlib-injector、i18n、暗色、自定义主页、皮肤、音乐、代理、PCL2 配置导入。

---

## 9. 测试策略

| 层级 | 内容 | 工具 |
|---|---|---|
| 单元 | 版本 JSON 合并、`rules` 判定、参数替换、路径、SHA1、Java 版本匹配 | xUnit |
| 契约 | 用固定 JSON 样本（Mojang/Fabric/Modrinth）反序列化 | xUnit + 快照 |
| 集成 | 本地 HTTP fixture 模拟官方/镜像；失败、重试、断点、校验失败 | WireMock.Net |
| E2E | 真实安装 1.21.1 + 启动（CI 缓存 assets，手动/定时跑） | 脚本 + 退出码 |
| UI | ViewModel 逻辑、导航、状态机 | Avalonia.Headless |
| 手工 | 三平台冒烟清单（启动/下载/登录/实例/Mod） | 清单 |

关键测试用例（先写）：

- `inheritsFrom` 多层合并：父版本库/参数/资源索引的覆盖顺序。
- `rules`：`os.name=linux`、`arch=x86_64`、`features.has_custom_resolution`。
- natives：classifier 选择、解压排除 META-INF。
- 参数：含空格/引号/中文路径的转义。
- 下载：404/超时/半包/哈希不符/镜像回退。

---

## 10. CI/CD 与发布

```yaml
# .github/workflows/ci.yml（示意）
strategy:
  matrix:
    os: [ubuntu-latest, windows-latest, macos-latest]
steps:
  - uses: actions/checkout@v4
  - uses: actions/setup-dotnet@v4
    with: { dotnet-version: '10.0.x' }
  - run: dotnet test -c Release
  - run: dotnet publish src/PCL.App -c Release -r ${{ matrix.rid }} --self-contained
```

- 缓存 NuGet；PR 只跑 build+test；tag 触发三平台打包。
- 发布物：`PCL-linux-x64.tar.gz`、`PCL-x86_64.AppImage`、`PCL-win-x64.zip`、`PCL-macos-arm64.dmg`。
- 版本号：`0.x.y`；`latest.json` 含版本、URL、SHA256、release notes。

---

## 11. 风险登记

| 风险 | 概率 | 影响 | 对策 |
|---|---|---|---|
| 微软登录需要 Azure client ID | 高 | M5 阻塞 | 先做离线；同时准备公共客户端注册；或复用开源启动器的公开 client ID（需确认条款） |
| Forge/NeoForge 安装器复杂 | 高 | v0.3 延期 | 先 Fabric；Forge 用 headless installer，失败给手动指引 |
| CurseForge 需要 API key | 中 | 资源覆盖不全 | 首版只接 Modrinth |
| 下载源不稳定/限速 | 中 | 安装失败 | 多源 + 重试 + 断点 + 校验 |
| Avalonia 在 Wayland/显卡驱动下渲染异常 | 中 | UI 不可用 | X11 后端回退；`AVALONIA_X11`；测试 XWayland |
| 中文字体缺失/排版差异 | 中 | 观感差 | 内置 Noto Sans SC；固定字号/行高 |
| PCL 许可约束（非 OSI） | 高 | 发布/命名 | 见 §12；开源、署名、PCL 前缀、保留正版提示 |
| 原版源码非全部公开 | 中 | 行为差异 | 以黑盒行为补；不承诺 100% 一致 |
| 工作量失控 | 高 | 烂尾 | 严格 MVP；每里程碑可演示；不做"顺手加功能" |
| 无 sudo / 环境差异 | 低 | 构建失败 | SDK 用户级安装；CI 兜底；文档写清依赖 |

---

## 12. 许可与命名

- PCL2 使用《PCL 分发有限许可》，**不是 OSI 开源许可**。基于其代码/功能的重度使用要求：
  1. 明确标注第三方二次创作；
  2. 名称以 `Plain Craft Launcher (PCL)` 开头并加第三方后缀；
  3. 关于页首位署名"龙腾猫跃"及赞助链接；
  4. 公开源代码，并沿用该指南；
  5. 保留 Minecraft 正版购买劝导（对应原版 `McLaunchPrecheck`）。
- 建议工作名：**PCL Linux**（符合前缀要求）；README 与关于页注明"非官方、社区重写"。
- 不复用 PCL 的图标、字体、音乐、帮助文档图片；文案可参考但需自行组织。
- 第三方依赖逐一审核许可证（Avalonia MIT、SkiaSharp MIT、SharpCompress MIT、Polly BSD-3 等）。

---

## 13. 批准后立即执行的第一批任务

1. **M0**：安装 .NET 10 SDK（用户级），固定 `global.json`，创建解决方案骨架与 CI。
2. **M1-1**：实现 `version_manifest_v2.json` 拉取/缓存 + 版本 JSON 模型 + `inheritsFrom` 合并 + `rules` 判定（带单测）。
3. **M1-2**：实现 libraries/assets 下载（官方 + BMCLAPI 回退、SHA1、`.part`），用 1.21.1 实测。
4. **M1-3**：实现 Java 发现/选择 + 参数拼装 + 进程启动，CLI 拉起游戏。
5. 汇报：构建日志、安装目录树、启动截图、耗时与问题清单。

---

## 14. 待确认决策

| # | 决策 | 推荐 |
|---|---|---|
| 1 | 语言策略：核心全部 C# 重写，还是保留 VB 类库 + C# UI？ | **全 C#**（长期维护、工具链统一；VB 仅作参考） |
| 2 | 首版范围：先只做原版启动（M1–M2），还是 v0.1 就含 Fabric/Mod？ | **先原版启动**，Fabric/Mod 放 v0.2 |
| 3 | 微软登录是否首版必须？ | **否**，M5 再做；需要你提供 Azure client ID 或接受仅离线 |
| 4 | 项目名/仓库名 | **PCL Linux**（工作名），可改 |
| 5 | 平台优先级：Linux 优先，Windows/macOS 同源后验？ | **是** |

---

## 附：本机环境核查（2026-10-18）

| 项 | 结果 |
|---|---|
| .NET | 运行时 10.0.13；**无 SDK**（需 M0 安装） |
| Java | OpenJDK 25.0.4 LTS（Zulu） |
| 桌面 | Wayland（niri）+ XWayland `:0` |
| 架构/内存/磁盘 | x86_64 / 31 GB / 403 GB 可用 |
| 网络 | Mojang 200、BMCLAPI 302、NuGet 302 |
| 字体 | 系统有 Noto Sans CJK |
| 权限 | 无 passwordless sudo → SDK 用户级安装 |
