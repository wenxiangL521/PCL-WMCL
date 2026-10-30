# PCL WMCL 项目交接文档

> 本文档由自动化审计代理生成，涵盖项目概况、技术栈、架构设计、功能清单、已知问题、修复记录及开发指南。
> 生成时间：2026-10-29 · 版本：v0.1.1 · 提交数：320

---

## 1. 项目概况

### 1.1 项目简介

**PCL WMCL**（Plain Craft Launcher WMCL）是原版 **PCL2**（龙腾猫跃/LTCat 开发的 Plain Craft Launcher 2）的第三方跨平台重写版本，由 wenxiangL 维护。该项目以 Linux 为优先平台，使用 .NET 10 + Avalonia 实现跨平台 UI。

> ⚠️ 本项目与 PCL 官方无隶属关系，是独立重写版本。

### 1.2 基本信息

| 项目 | 内容 |
|------|------|
| 仓库路径 | `/home/wenxiangl/Documents/deepseek-harness/default-workspace/wmcl` |
| 远程仓库 | `https://github.com/wenxiangL521/PCL-WMCL.git` |
| 分支 | `main` |
| 提交数 | 320 |
| 标签 | 无 |
| 当前版本 | v0.1.1 |
| 解决方案 | `WMCL.slnx` |

### 1.3 解决方案结构

```
WMCL.slnx
├── src/
│   ├── WMCL.Core/          — 核心工具：路径管理、代理、回收站、本地化、环境检测 (21 .cs)
│   ├── WMCL.Download/      — 下载管理器：HTTP 下载、进度回调、限速 (12 .cs)
│   ├── WMCL.Launch/        — 游戏启动：启动计划构建、JVM 参数、进程管理 (27 .cs)
│   ├── WMCL.Minecraft/     — Minecraft 核心：版本安装、库解析、规则评估、实例管理 (39 .cs)
│   ├── WMCL.Modding/       — 模组生态：Modrinth/CurseForge API、整合包、资源包、光影 (43 .cs)
│   ├── WMCL.UI/            — UI 资源：颜色主题、样式 (6 .cs, 2 .axaml)
│   ├── WMCL.App/           — 主应用：ViewModels、Views、Controls、Services (161 .cs, 45 .axaml)
│   └── WMCL.Cli/           — 命令行工具：完整 CLI (7 .cs, 1512 行)
└── tests/
    ├── WMCL.Core.Tests/    — Core 层测试 (15 .cs, 53 tests)
    ├── WMCL.Launch.Tests/  — Launch 层测试 (26 .cs, 139 tests)
    ├── WMCL.Minecraft.Tests/ — Minecraft 层测试 (72 .cs, 832 tests)
    └── WMCL.App.Tests/     — App 层测试 (88 .cs, 912 tests)
```

**总计**：8 个 src 项目 + 4 个 test 项目，316 个源文件，45 个 XAML 文件，201 个测试文件。

---

## 2. 技术栈

### 2.1 运行时与框架

| 组件 | 版本 |
|------|------|
| .NET SDK | 10.0.402 |
| 语言版本 | C# 14 |
| 目标框架 | net10.0（所有项目统一） |
| UI 框架 | Avalonia 11.3.10 |
| MVVM 框架 | CommunityToolkit.Mvvm 8.4.2 |
| 测试框架 | xUnit 2.9.3 + Avalonia.Headless.XUnit 11.3.10 |
| D-Bus 协议 | Tmds.DBus.Protocol 0.21.3（Wayland 剪贴板） |

### 2.2 关键依赖

- **Avalonia** — UI 框架（Desktop、Themes.Fluent、Fonts.Inter、Markup.Xaml.Loader）
- **CommunityToolkit.Mvvm** — 源生成器 MVVM（`[ObservableProperty]`、`[RelayCommand]`）
- **xUnit** — 测试框架（含 Avalonia.Headless 支持 UI 测试）
- **coverlet.collector** — 代码覆盖率

### 2.3 开发环境要求

- .NET 10 SDK
- Git
- Linux（Wayland/X11 均可，Wayland 有特殊剪贴板处理）

---

## 3. 构建与测试

### 3.1 构建

```bash
cd /home/wenxiangl/Documents/deepseek-harness/default-workspace/wmcl
dotnet build WMCL.slnx -c Release
```

### 3.2 运行测试

```bash
dotnet test WMCL.slnx -c Release --no-build
```

### 3.3 格式检查

```bash
dotnet format whitespace WMCL.slnx --verify-no-changes
```

### 3.4 Git 差异检查

```bash
git diff --check
```

### 3.5 运行应用

```bash
dotnet run --project src/WMCL.App -c Release
```

### 3.6 运行 CLI

```bash
dotnet run --project src/WMCL.Cli -c Release -- [command]
```

---

## 4. 架构设计

### 4.1 分层架构

```
┌─────────────────────────────────────────────────┐
│                  WMCL.App                       │
│  (ViewModels → Views → Controls → Services)     │
├───────────┬────────────┬────────────────────────┤
│  WMCL.UI  │  WMCL.Cli  │    WMCL.Launch        │
│ (主题样式) │  (CLI入口) │  (启动计划构建)       │
├───────────┴────────────┴────────────────────────┤
│                   WMCL.Minecraft                 │
│  (版本安装/库解析/实例管理/规则评估)             │
├─────────────────────┬───────────────────────────┤
│     WMCL.Modding    │         WMCL.Download     │
│  (Modrinth/CurseForge/整合包/资源包)            │
│                     │    (下载管理器/限速)       │
├─────────────────────┴───────────────────────────┤
│                    WMCL.Core                     │
│  (路径管理/代理/回收站/本地化/环境检测)          │
└─────────────────────────────────────────────────┘
```

### 4.2 关键设计模式

#### MVVM 模式（CommunityToolkit.Mvvm 源生成器）

- `[ObservableProperty]` 用于字段（**必须用在字段上，不能用于属性**）
- `[RelayCommand]` 用于命令方法
  - `Async` 后缀的方法生成的命令不带 `Async` 后缀（`AddServerAsync` → `AddServerCommand`）
  - `[RelayCommand(IncludeCancelCommand = true)]` 自动生成取消命令
- `partial void OnXxxChanged(T value)` 用于属性变更副作用

#### 服务注册

- `AppServices` 聚合所有服务实例
- `_services.Mirror` 返回 `MirrorMode` 枚举（Auto/Official/Bmclapi），**不是字符串**

#### 路径管理

- `WmclPaths.DataDirectory` 是 get-only 属性
- 测试通过 `XDG_DATA_HOME` 环境变量隔离数据目录
- `WmclPaths.EnsureCreated` 处理旧版配置迁移

### 4.3 本地化（i18n）

- 自研 YAML 本地化系统，无外部依赖
- 资源文件：`src/WMCL.App/Assets/Locales/en-US.yaml`、`zh-CN.yaml`
- 当前 26 个键值，中英完全对齐，无空值，无占位符不匹配
- 使用方式：`Localizer.T("welcome.title")`

### 4.4 Avalonia 资源 URI

- 前缀：`avares://WMCL.App/`
- ⚠️ **AssemblyName 必须保持 `WMCL.App`**，否则资源加载失败

---

## 5. 功能清单

### 5.1 核心功能

| 功能 | 状态 | 说明 |
|------|------|------|
| 多账户管理 | ✅ | 离线账户、Microsoft 账户、Authlib-Addon-Interactive 登录 |
| 版本管理 | ✅ | Minecraft 版本安装、校验、验证、批量安装 |
| 镜像加速 | ✅ | 官方/BMCLAPI 镜像，自动检测，镜像测速对比 |
| 模组管理 | ✅ | Modrinth/CurseForge 搜索安装、本地模组、模组更新 |
| 整合包 | ✅ | CurseForge/MCBBS/本地整合包安装与导出 |
| 实例管理 | ✅ | 创建、克隆、重命名、删除、导入、导出、迁移、修复 |
| 资源包/光影 | ✅ | 资源包启用/禁用、光影包管理、存档管理 |
| 下载管理 | ✅ | 暂停/继续/取消、批量安装、进度显示、限速 |
| 服务端管理 | ✅ | 准备文件、启动/停止、控制台命令、server.properties 编辑 |
| 联机功能 | ✅ | EasyTier P2P 联机、Terracotta 房间 |
| 皮肤编辑器 | ✅ | 像素级编辑、撤销/重做、MS Skin 上传 |
| 截图管理 | ✅ | 截图预览、缩略图、选择导出 |
| 回收站 | ✅ | 移动/恢复/永久删除/自动清理（默认关闭） |
| 任务中心增强 | ✅ | 逐任务日志（环形 500 条/脱敏）、失败堆栈展示与复制、失败任务按原参数重试、历史持久化 v2（原子写/退出终态化/v1 兼容）、刷新合并调度 ≤10Hz 与差量更新 |
| 取消清理 | ✅ | 取消路径 .part 临时文件清理（暂停路径保留续传）、进度展示统一格式（ProgressFormatter）、取消幂等守卫 |
| 自动更新 | ✅ | 检查/下载/安装并重启 |
| 配置导入导出 | ✅ | JSON 格式导出/导入 |
| 快捷键 | ✅ | 自定义快捷键（Ctrl+1~5、Ctrl+Tab、Ctrl+F） |
| UI 缩放 | ✅ | 界面缩放设置 |
| 帮助文档 | ✅ | 17 个主题、8 个分类、搜索过滤 |
| 日志查看 | ✅ | 列出/打开/删除/清理日志 |
| 故障反馈 | ✅ | 诊断信息生成、崩溃报告导出 |
| CLI 工具 | ✅ | 完整命令行界面 |
| 启动计划构建 | ✅ | 特性解析、库解析、占位符替换、JVM 参数生成 |

### 5.2 平台特性

| 特性 | 说明 |
|------|------|
| Wayland 剪贴板 | 通过 `wl-paste`/`wl-copy` 和 D-Bus 协议支持 |
| macOS 代理检测 | `scutil --proxy` 解析 |
| Linux 代理检测 | `gsettings` 解析 |
| SDL Wayland 修复 | 启动时设置 `SDL_VIDEO_DRIVER=wayland` |
| GPU 检测 | `lspci` 解析 GPU 信息 |
| DRI_PRIME | GPU 选择支持 |

---

## 6. 代码规范与约定

### 6.1 命名规范

- C# 标准 PascalCase 命名
- XAML 控件使用 `x:Name` 命名（如 `PageScroll`、`SectionList`）
- 样式类使用小写（如 `card`、`secondary`、`primary`）

### 6.2 异常处理

- 空 catch 块是**有意设计**，用于捕获预期异常（文件不存在、权限拒绝、损坏元数据、进程已退出等）
- 全局异常处理在 `Program.cs` 和 `App.axaml.cs` 中
- 文件操作使用 try-catch 保护，不阻塞 UI

### 6.3 CTS 管理

- ⚠️ `Dispose-then-null` 模式（`_cts?.Dispose(); _cts = null;`）在 ~20 个位置使用
- 在 Avalonia 中安全：RelayCommand 和 finally 块都在 UI 线程运行，无线程交错
- 对于需要竞态保护的场景，使用请求 ID 模式（如 `DownloadViewModel._installRequest`）

### 6.4 Avalonia API 注意事项

| API | 注意 |
|-----|------|
| `Color` | 无 `FromRgba` 静态方法，使用 `new Color(r,g,b,a)` |
| `DashStyle` | 无 `Solid`，使用默认值 |
| `IPointer` | 无 `IsPressed`，使用 `IsPointerOver` |
| `FillRect` | 不存在，使用 `FillRectangle(IBrush, Rect)` |
| `WriteableBitmap` | 构造函数参数 `(PixelSize, Vector, PixelFormat, AlphaFormat)` |
| `AesGcm` | 构造函数需要 tag size：`new AesGcm(key, 16)` |
| `RadioButton.IsChecked` | 类型为 `bool?` |
| `e.Pointer.Capture(this)` | 可用 |
| `e.Pointer.Release()` | **不存在** |
| `KeyModifiers.Meta` | 存在 |
| `KeyModifiers.Super` | **不存在** |
| `Color.Parse("#555555FF")` | 字节序为 `#RRGGBBAA` |
| `string.Trim(char)` | .NET 10 返回 `ReadOnlySpan<char>`，不是 `string` |
| `FilePickerFileType` | 在 `Avalonia.Platform.Storage`，不在 `Avalonia.Controls` |
| `TopLevel` | 在 `Avalonia.Controls`，不在 `Avalonia` |
| `Window` | 需要 `using Avalonia.Controls;` |
| XAML StringFormat | 需要转义花括号：`StringFormat='{}{0:F1}×'` |
| `Bitmap.Save(stream)` | 同步方法 |

### 6.5 测试规范

- 使用 `Bitmap`/`WriteableBitmap` 的测试必须使用 `[AvaloniaFact]`，不能用 `[Fact]`
- 测试通过 `XDG_DATA_HOME` 环境变量隔离数据目录
- `ImplicitUsings` 已启用，但 `System.Collections.ObjectModel` 需要显式引用

---

## 7. 修复记录

### 7.1 本轮审计修复（第 6-8 轮，6 次提交）

| # | 提交 | 文件 | 问题 | 修复 |
|---|------|------|------|------|
| 1 | `e05a656` | `RemoteImage.cs` | 远程图片缓存无大小限制 | 添加 200 条目上限，超出时淘汰最早条目 |
| 2 | `11ab02e` | `MainWindow.axaml.cs` | 粘贴兜底选区删除范围错误 | 改用 `SelectionStart` 而非 `CaretIndex` |
| 3 | `adb3a19` | `DownloadViewModel.cs` | 暂停/继续功能严重 Bug | 请求 ID 防竞态、分离 CTS 释放与状态清理、统一 UpdateCommands |
| 4 | `ca5108e` | `UpdateViewModel.cs` | DownloadAsync 重复 IsBusy 检查 | 移除重复检查 |

### 7.2 此前修复（第 1-5 轮，12 次提交）

| # | 提交 | 修复内容 |
|---|------|----------|
| 5 | `49e00db` | AuthlibLoginViewModel 实现 IDisposable |
| 6 | `1e67882` | LoadServersAsync 添加竞态保护 |
| 7 | `7541403` | LoadInstanceSettings 添加异常处理防崩溃 |
| 8 | `4ab87d8` | 服务端启动/停止按钮 IsEnabled 绑定 |
| 9 | `40534e4` | SaveServerPropertiesAsync 实例 ID + 目录双重验证 |
| 10 | `7a31fc2` | 切换实例时未清空 server.properties |
| 11 | `99cd4cf` | SettingsView 误导性提示 + BackupWorld 未刷新列表 |
| 12 | `697db10` | MinecraftNewsWindow CTS 泄漏 |
| 13 | `68b3040` | LinkViewModel.Dispose 泄漏 Terracotta CTS + 未停进程 |
| 14 | `2efa191` | ApiSelectionViewModel.Clear 中 CTS 未释放 |
| 15 | `0684534` | ToggleResourcePack 缺失 + Refresh 保留选中 + TaskManager 订阅泄漏 |
| 16 | `74140bd` | MicrosoftLoginViewModel IDisposable + 登录后释放 |
| 17 | `f7034aa` | 全量修复 ViewModel CTS 泄漏 + InstallAllAsync IsBusy 检查 |
| 18 | `126ff39` | 下载线程硬编码 + 回收站清理缺失 + macOS 代理检测 |
| 19 | `0b4e6e7` | 设置导入导出 + 资源泄漏 + UI 布局重叠 |

### 7.3 修复的问题类型分布

1. **CTS 泄漏**（~20 处）— 暂停/取消/关闭窗口后 CTS 未释放
2. **竞态条件** — LoadServersAsync、DownloadViewModel 暂停/继续
3. **资源管理** — RemoteImage 缓存无上限、文件流未释放
4. **UI 状态不一致** — 按钮 IsEnabled 缺失、误导性提示文字
5. **数据完整性** — server.properties 切换未清空、实例 ID 验证缺失
6. **异常处理** — 损坏设置文件导致崩溃、空 catch 块（已确认全部合理）
7. **逻辑错误** — 粘贴兜底选区删除范围错误、暂停后继续无效

---

## 8. 已知限制与注意事项

### 8.1 有意保留的设计

- **Dispose-then-null CTS 模式**：在 Avalonia UI 线程上下文中安全，不修复
- **空 catch 块**（45+ 处）：全部为预期异常，合理设计
- **InstallViewModel 匿名订阅**：子 VM PropertyChanged 订阅无法取消，生命周期 = 应用
- **RemoteImage 缓存淘汰**：使用 `TryRemove(Cache.Keys.First())` 简单策略，非 LRU

### 8.2 功能缺口（相对 PCL2 原版）

以下功能在 PCL2 原版中存在但 WMCL 尚未实现或不够完善：

- 部分高级整合包格式支持
- HMCL 联机大厅
- 部分第三方 API 组件
- 详细的模组依赖解析可视化

### 8.3 测试覆盖

- 总测试数：**1936**（Core 53 + Launch 139 + Minecraft 832 + App 912）
- 测试框架：xUnit 2.9.3 + Avalonia.Headless.XUnit
- 代码覆盖率：使用 coverlet.collector 收集

---

## 9. 开发指南

### 9.1 新增功能的标准流程

1. **确定层级**：根据功能性质选择项目层（Core/Minecraft/Modding/App）
2. **编写 ViewModel**：在 `src/WMCL.App/ViewModels/` 中创建或修改 ViewModel
   - 使用 `[ObservableProperty]` 声明可观察属性（字段）
   - 使用 `[RelayCommand]` 声明命令方法
   - 实现 `IDisposable`（如果需要管理 CTS/资源）
3. **编写 View**：在 `src/WMCL.App/Views/` 中创建或修改 XAML 视图
   - 在 `App.axaml` 的 `DataTemplates` 中注册 ViewModel→View 映射
4. **编写测试**：在 `tests/WMCL.App.Tests/` 中添加测试
   - UI 相关测试使用 `[AvaloniaFact]`
5. **验证**：构建 + 测试 + 格式检查 + git diff 检查

### 9.2 新增服务的注意事项

- 在 `AppServices` 中注册服务
- 如果服务需要管理资源（文件句柄、进程、CTS），实现 `IDisposable`
- 在 `MainWindowViewModel.Dispose` 中释放服务引用

### 9.3 修改 XAML 的注意事项

- 修改 XAML 后需要**完全重新构建**（`dotnet build`），增量构建可能不生效
- 样式类使用小写命名（如 `card`、`secondary`）
- 资源引用使用 `{DynamicResource WmclXxx}` 格式
- 颜色值使用 `#RRGGBBAA` 格式（如 `#55FFFFFF`）

### 9.4 提交规范

```
类型: 简短描述

详细说明（可选）

测试：N 通过，0 失败
```

类型包括：`feat:`、`fix:`、`refactor:`、`docs:`、`test:`、`chore:`

---

## 10. 快速参考

### 10.1 常用文件路径

| 文件 | 路径 |
|------|------|
| 解决方案 | `WMCL.slnx` |
| 主应用入口 | `src/WMCL.App/Program.cs` |
| App 初始化 | `src/WMCL.App/App.axaml.cs` |
| 主窗口 VM | `src/WMCL.App/ViewModels/MainWindowViewModel.cs` |
| 主窗口 View | `src/WMCL.App/Views/MainWindow.axaml` + `.axaml.cs` |
| 服务注册 | `src/WMCL.App/Services/AppServices.cs` |
| 路径管理 | `src/WMCL.Core/WmclPaths.cs` |
| 代理管理 | `src/WMCL.Core/ProxyManager.cs` |
| 回收站 | `src/WMCL.Core/TrashManager.cs` |
| 本地化 | `src/WMCL.App/Assets/Locales/*.yaml` |
| 颜色主题 | `src/WMCL.UI/Styles/WmclColors.axaml` |
| 应用主题 | `src/WMCL.UI/Styles/WmclTheme.axaml` |

### 10.2 关键 ViewModel 构造函数签名

```csharp
// DownloadViewModel
public DownloadViewModel(AppServices services, Func<bool, Task<IReadOnlyList<VersionItem>>> loadVersions)

// InstanceViewModel
public InstanceViewModel(AppServices services)

// InstanceContentViewModel
public InstanceContentViewModel(AppServices services, string gameDirectory)

// TaskManagerViewModel
public TaskManagerViewModel(DownloadViewModel, ModsViewModel, ModpackViewModel,
    InstanceContentViewModel, InstanceViewModel, InstallViewModel,
    ResourceFavoriteBatchViewModel?, LaunchViewModel?, string? historyFilePath)

// InstallViewModel
public InstallViewModel(AppServices services, InstanceViewModel instance)
```

### 10.3 关键记录类型

```csharp
// TaskEntry（TaskManagerViewModel.cs 第 22 行）
sealed record TaskEntry(...)

// InstanceContentItem
sealed record InstanceContentItem(...) { IsEnabled { get; init; } = true; }

// ServerEntry
sealed partial class ServerEntry : ObservableObject { Name, Address, Icon, IconImage, PingText, HasIcon }

// JobProgress
record JobProgress(Stage, Percent, BytesPerSecond, Detail)

// MicrosoftAccount
record MicrosoftAccount(Name, Uuid, AccessToken, RefreshToken, ExpiresAt)
```

### 10.4 构建验证清单

每次提交前必须验证：

```bash
# 1. 构建
dotnet build WMCL.slnx -c Release    # 预期：0 Error(s)

# 2. 测试
dotnet test WMCL.slnx -c Release --no-build    # 预期：1936 Passed, 0 Failed

# 3. 格式
dotnet format whitespace WMCL.slnx --verify-no-changes    # 预期：EXIT=0

# 4. Git diff
git diff --check    # 预期：EXIT=0
```

---

## 11. 对比参考

### 11.1 与 PCL2 原版对比

PCL WMCL 是 PCL2 的独立重写，主要差异：

| 方面 | PCL2 | PCL WMCL |
|------|------|----------|
| 平台 | Windows 优先 | Linux 优先，跨平台 |
| UI | WinForms/WPF | Avalonia (跨平台) |
| 语言 | C# | C# 14 (.NET 10) |
| 架构 | 单体 | 分层 (Core/Minecraft/Modding/Launch/Download) |
| 联机 | HMCL 大厅 | EasyTier P2P + Terracotta |
| 皮肤 | 基础 | 像素编辑器 + MS Skin |

### 11.2 与 PCL-CE 对比

PCL-CE 是 PCL2 的另一个社区分支，PCL WMCL 与它的对比：

| 方面 | PCL-CE | PCL WMCL |
|------|--------|----------|
| 平台 | Linux 优先 | Linux 优先 |
| UI | Avalonia | Avalonia |
| 功能覆盖 | 较全 | 较全（部分功能不完善） |
| 架构 | 分层 | 分层 |

---

## 12. 附录：关键文件索引

### 12.1 ViewModel 索引

| ViewModel | 文件 | 职责 |
|-----------|------|------|
| MainWindowViewModel | `MainWindowViewModel.cs` | 主窗口导航、页面管理、音乐控制 |
| LaunchViewModel | `LaunchViewModel.cs` | 启动页：账户、版本选择、启动 |
| DownloadViewModel | `DownloadViewModel.cs` | 下载页：版本列表、安装/校验/暂停/继续 |
| VersionSelectViewModel | `VersionSelectViewModel.cs` | 版本选择 |
| InstanceViewModel | `InstanceViewModel.cs` | 实例管理：创建/克隆/迁移/删除/服务端 |
| InstanceContentViewModel | `InstanceContentViewModel.cs` | 实例内容：世界/资源包/光影/截图/下载 |
| ModsViewModel | `ModsViewModel.cs` + `.Updates.cs` + `.Dependencies.cs` | 模组管理 |
| ModpackViewModel | `ModpackViewModel.cs` | 整合包安装/导入 |
| SettingsViewModel | `SettingsViewModel.cs` | 设置：游戏目录/Java/代理/快捷键/缩放 |
| ToolboxViewModel | `ToolboxViewModel.cs` | 工具箱：目录/镜像测速/垃圾清理 |
| LinkViewModel | `LinkViewModel.cs` | 联机：EasyTier/Terracotta |
| AboutViewModel | `AboutViewModel.cs` | 关于/更新检查 |
| UpdateViewModel | `UpdateViewModel.cs` | 更新管理 |
| HelpViewModel | `HelpViewModel.cs` | 帮助文档 |
| LogsViewModel | `LogsViewModel.cs` | 日志查看 |
| FeedbackViewModel | `FeedbackViewModel.cs` | 故障反馈 |
| SkinEditorViewModel | `SkinEditorViewModel.cs` | 皮肤编辑器 |
| GameManageViewModel | `GameManageViewModel.cs` | 游戏管理 |
| InstallViewModel | `InstallViewModel.cs` + partials | 安装流程 |
| ResourceFavoritesViewModel | `ResourceFavoritesViewModel.cs` | 资源收藏 |
| ResourceFavoriteBatchViewModel | `ResourceFavoriteBatchViewModel.cs` | 批量资源安装 |

### 12.2 View 索引

| View | 文件 | 对应 ViewModel |
|------|------|----------------|
| MainWindow | `MainWindow.axaml` | MainWindowViewModel |
| LaunchView | `LaunchView.axaml` | LaunchViewModel |
| DownloadView | `DownloadView.axaml` | DownloadViewModel |
| InstanceView | `InstanceView.axaml` | InstanceViewModel |
| InstanceContentView | `InstanceContentView.axaml` | InstanceContentViewModel |
| ModsView | `ModsView.axaml` | ModsViewModel |
| ModpackView | `ModpackView.axaml` | ModpackViewModel |
| SettingsView | `SettingsView.axaml` | SettingsViewModel |
| ToolboxView | `ToolboxView.axaml` | ToolboxViewModel |
| LinkView | `LinkView.axaml` | LinkViewModel |
| AboutView | `AboutView.axaml` | AboutViewModel |
| UpdateView | `UpdateView.axaml` | UpdateViewModel |
| HelpView | `HelpView.axaml` | HelpViewModel |
| LogsView | `LogsView.axaml` | LogsViewModel |
| FeedbackView | `FeedbackView.axaml` | FeedbackViewModel |
| ScreenshotView | `ScreenshotView.axaml` | (ScreenshotWindow) |
| GameManageView | `GameManageView.axaml` | GameManageViewModel |
| InstallView | `InstallView.axaml` | InstallViewModel |
| ResourceFavoritesView | `ResourceFavoritesView.axaml` | ResourceFavoritesViewModel |
| ResourceFavoriteBatchView | `ResourceFavoriteBatchView.axaml` | ResourceFavoriteBatchViewModel |
| CleanroomView | `CleanroomView.axaml` | CleanroomViewModel |
| LabyModView | `LabyModView.axaml` | LabyModViewModel |
| LinkSettingsView | `LinkSettingsView.axaml` | LinkSettingsViewModel |
| MoreInfoView | `MoreInfoView.axaml` | MorePageViewModel |

### 12.3 关键 Services 索引

| Service | 文件 | 职责 |
|---------|------|------|
| AppServices | `AppServices.cs` | 服务聚合容器 |
| GameOperationCoordinator | `GameOperationCoordinator.cs` | 游戏操作互斥锁 |
| MusicService | `MusicService.cs` | 背景音乐播放 |
| EasyTierService | `EasyTierService.cs` | EasyTier 进程管理 |
| TerracottaService | `TerracottaService.cs` | Terracotta 进程管理 |
| ExternalLauncher | `ExternalLauncher.cs` | 外部程序启动 |
| GpuDetector | `GpuDetector.cs` | GPU 检测 |
| WaylandClipboard | `WaylandClipboard.cs` | Wayland 剪贴板 |
| CombinedInstallService | `CombinedInstallService.cs` | 组合安装 |
| ResourceBatchInstallService | `ResourceBatchInstallService.cs` | 批量资源安装 |
| TaskRunStore | `TaskRunStore.cs` | 逐任务日志环形缓冲（500 条/线程安全/失败信息） |
| TaskFailureInfo | `TaskFailureInfo.cs` | 失败堆栈载荷（InnerException 链拼接/32KB 截断/脱敏副本） |
| TaskLogEntry | `TaskLogEntry.cs` | 结构化日志条目四元组（时间/级别/阶段/消息） |
| SensitiveTextRedactor | `SensitiveTextRedactor.cs` | 共享脱敏过滤器（日志/堆栈/配置导出共用模式集，幂等） |
| TaskRetryPayload | `TaskRetryPayload.cs` | 失败任务重试快照（可 JSON 序列化，属性名冻结） |
| ITaskRetrySource | `ITaskRetrySource.cs` | 失败上报与重试行为契约（六源 VM 实现） |
| PartFileCleaner | `PartFileCleaner.cs` | 取消路径 .part 清理（后缀白名单/占用跳过） |
| ProgressFormatter | `ProgressFormatter.cs` | 任务进度统一展示格式（阶段/百分比/速度/详情） |

---

*本文档基于 commit `ca5108e` 生成。如需更新，请重新运行审计并更新相关章节。*
