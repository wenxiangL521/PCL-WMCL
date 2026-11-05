# WMCL 项目结构审查与整改任务书

> 交接文档 · 审查日期 2026-11-05 · 审查对象 `main` @ `51c2a3b`
> 审查时工作区干净；本文档为新增的交接文件。

## 0. 给接手窗口的说明

- 这是一份**结构与依赖审查**，不是功能 bug 清单。
- 目标是**按优先级整改，不是重写**。
- 第 5 节是必须知道的事实校正；第 8 节是任务清单；第 9 节是禁止事项；第 10 节是验收标准。
- 审查方式：静态结构/依赖分析 + `dotnet build WMCL.slnx -c Release`；**未跑全量测试**，测试数量来自源码 attribute 统计，不代表当前全部通过。

### 一句话结论

结构总体合理（主观 7.5/10），分层和依赖方向正确。主要问题是：**App 工程过重（约占生产代码 63%）**、**Core / Infrastructure / UI / Link 边界未收尾**、**CLI 与 App 各自编排、缺少共享应用层**、**测试工程与生产工程不对齐**。

---

## 1. 项目概况

- 产品：Plain Craft Launcher (PCL) WMCL，PCL2 的第三方、非官方跨平台重写。
- 技术栈：.NET 10、C# 14、Avalonia 11.3.10；SDK 由 `global.json` 固定为 10.0.402。
- 解决方案：`WMCL.slnx`，10 个生产工程 + 4 个测试工程。
- 常用命令：

```bash
dotnet build WMCL.slnx -c Release
dotnet test  WMCL.slnx -c Release
dotnet format whitespace WMCL.slnx --verify-no-changes --no-restore
```

- 审查时 Release 构建结果：**0 warning / 0 error**。

生产工程与规模（约值）：

| 工程 | 文件 | 行数 | 定位 |
|---|---:|---:|---|
| WMCL.Core | 16 `.cs` | 1.6k | 路径 / HTTP / 日志 / 更新 |
| WMCL.Download | 6 `.cs` | 0.6k | 下载、镜像、断点 |
| WMCL.Minecraft | 36 `.cs` | 4.5k | 版本 / 规则 / 库 / 实例 / 安装 |
| WMCL.Launch | 24 `.cs` | 4.2k | Java / 认证 / 启动 / 崩溃 |
| WMCL.Modding | 39 `.cs` | 9.1k | 加载器 / 资源站 / 整合包 |
| WMCL.Link | 7 `.cs` | 1.9k | 联机 |
| WMCL.Infrastructure | 2 `.cs` | 0.2k | 回收站、音乐列表 |
| WMCL.UI | 1 `.cs` + 4 `.axaml` + 2 `.ttf` | 1.2k | 主题 / 字体 |
| **WMCL.App** | **223 `.cs` + 62 `.axaml`** | **42.1k** | 表现层 |
| WMCL.Cli | 6 `.cs` | 1.6k | 命令行 |

测试：4 个工程，323 个 `.cs`，约 48.2k 行；1,675 个测试方法（674 `[Fact]` + 322 `[Theory]` + 504 `[AvaloniaFact]` + 175 `[AvaloniaTheory]`），679 个 Avalonia Headless 用例，1,855 条 `[InlineData]`。

---

## 2. 审查方法与统计口径

- 源码计数均**排除** `bin/`、`obj/`。
- 测试数量按源码中的 attribute 统计，不是 `dotnet test` 的运行结果。
- 依赖关系来自 `ProjectReference`，并用脚本做过环检测。
- 未做运行时 UI 验证、真实 Minecraft 启动验证、打包产物验证。

---

## 3. 依赖拓扑（实测，无环）

```text
WMCL.Core              -> (无)
WMCL.UI                -> Avalonia
WMCL.Download          -> Core
WMCL.Minecraft         -> Download, Core
WMCL.Launch            -> Minecraft, Download, Core
WMCL.Modding           -> Minecraft, Download, Core
WMCL.Link              -> Minecraft, Core
WMCL.Infrastructure    -> Core
WMCL.App               -> UI, Link, Infrastructure, Launch, Modding, Minecraft, Download, Core
WMCL.Cli               -> Launch, Modding, Minecraft, Download, Core
WMCL.App.Tests         -> App
WMCL.Core.Tests        -> Core
WMCL.Launch.Tests      -> Core, Minecraft, Launch
WMCL.Minecraft.Tests   -> Core, Minecraft, Launch, Modding
```

关键事实：

- Core 零依赖。
- UI 只依赖 Avalonia。
- 除 App/UI 外，其他程序集**没有任何 Avalonia 引用**。
- App/Services 不反向引用 ViewModels。
- 14 个工程无环。

---

## 4. 总体评价

### 做得好的地方

1. 分层方向正确，依赖图干净，没有“下层反向依赖上层”。
2. 领域模块划分合理：Modding 按加载器/平台分目录，Launch 按 `Auth/Java/Crash/Diagnostics` 分目录。
3. 组合根显式（`AppServices` 手工组装），ViewModel 有 internal 构造注入作为测试缝，没有全局 ServiceLocator。
4. 测试投入大，Headless UI 测试和 `TestEnvironment` 的 XDG 隔离做得比多数同类项目好。
5. 工程化基础好：SDK 锁定、`Directory.Build.props`、CI 门禁、打包脚本共享 `publish.sh`、依赖漏洞覆盖。

### 主要问题

- App 占 63%，内部按类型平铺，出现多个上帝类。
- Core 像杂物间，Infrastructure/UI 是“半个工程”，Link 混入桌面集成。
- 缺少共享应用层，CLI 与 App 各自编排安装/启动。
- 测试工程与生产工程不对齐，App.Tests 216 个文件平铺。
- 版本元数据、CI、文档和仓库资产有收尾问题。

---

## 5. 关键事实校正（先读，避免改错）

### 5.1 `WMCL.App` 的 `.cs` 是 223 个，不是 235 个

排除 `bin/obj` 后：**223 个 `.cs` + 62 个 `.axaml`**。235 是把 `obj` 下 12 个生成文件（AssemblyInfo、GlobalUsings 等）也算进去了。

### 5.2 `WMCL.Link -> WMCL.Minecraft` 不是冗余依赖

- `src/WMCL.Link/EasyTierService.cs:119` 调用了 `ServerPingService.SplitAddress(value)`。
- `ServerPingService` 位于 `src/WMCL.Minecraft/ServerPingService.cs`，命名空间 `WMCL.Minecraft`（第 6、25 行）。
- 因此 `using WMCL.Minecraft;` 是必需依赖；直接删除 `src/WMCL.Link/WMCL.Link.csproj:11` 的 ProjectReference **会编译失败**。

真正的问题：`SplitAddress` 是通用地址解析，放在 Minecraft 里导致 Link 依赖 Minecraft。正确做法是把它（连同测试）移到 Core，再解除引用。

### 5.3 `MainWindowViewModel` 构造里是 25 个 ViewModel，不是约 30 个

用 `new *ViewModel(` 统计为 25 个。它仍然是“在构造函数里一次性创建全部页面”的问题，只是数字要按 25 记录。

---

## 6. 问题清单

### 6.1 App 工程

| ID | 级别 | 问题 | 证据 |
|---|---|---|---|
| A1 | 高 | App 体量过大 | 42,133 行，占生产代码 67,006 行的约 63%；223 `.cs` + 62 `.axaml` |
| A2 | 高 | 内部组织不一致 | Views 86、Controls 44、Services 52 个文件基本平铺；ViewModels 81 个文件只有 5 个子目录 |
| A3 | 高 | 7 个 ViewModel 超过 1000 行 | Launch 1838、Mods 1694、Instance 1561、InstanceContent 1409、Settings 1147、TaskManager 1098、MainWindow 1033 |
| A4 | 高 | `MainWindowViewModel` 职责过载 | 1033 行；构造中创建 25 个 ViewModel；导航 + 页面注册 + 跨 VM 事件中介；新增页面要在至少 3 处同步 |
| A5 | 中 | partial 拆分粒度/命名不统一 | Install 主文件 + 9 个 partial；Mods 3 个、Instance 1 个、MainWindow 2 个；后缀 `.Comparison/.Components/.RowActions` 与 `.Updates/.Dependencies` 混用 |
| A6 | 中 | 跨 VM 文件 + 重复逻辑 | `ViewModels/ResourceBrowserNavigation.cs` 一个文件定义 3 个 partial VM；`ModsViewModel` 与 `InstanceContentViewModel` 重复 `EnsureSearchGameVersionsAsync` / `EffectiveGameVersion` |
| A7 | 中 | Services 混杂 + 聚合对象过宽 | Services 52 个文件 / 5,030 行；21 个 VM 文件依赖 `AppServices`，实际使用约 30 个成员 |
| A8 | 中 | VM 直接依赖 Avalonia | 22 个 ViewModel 文件 `using Avalonia`（Bitmap / Dispatcher / ApplicationLifetime） |
| A9 | 低 | 资源组织 / 实例化小问题 | `App.axaml` 内联大段 `ControlTheme`；`MainWindowViewModel.cs:66` 为 Install 又 new 了一个 `ModsViewModel`，与共享的 `Mods` 并存 |

补充：

- A2 的子目录粒度也不统一：`Accounts` 1 个文件、`Launching` 2 个、`Instances` 5 个、`Content` 4 个、`Mods` 4 个。
- A4 的三处注册点：`App.axaml` 的 `DataTemplate`、`MainWindowViewModel` 的构造/导航列表、`NavItem` 定义。
- A7 的 `AppServices` 作为组合根本身合理；问题是消费端把它当成“什么都能拿到”的聚合对象，接近服务定位器。

### 6.2 工程边界

| ID | 级别 | 问题 | 证据 |
|---|---|---|---|
| B1 | 高 | Core 是杂物间 | 根目录 7 个文件；`Http/Jobs/Logging/Localization` 各 1 个文件，`Update` 2 个；`EasyTierNodeList.cs` 属 Link 领域；`Proxy*` / `Update/*` / `LauncherSecrets` 偏基础设施或应用层；`Music/` 是空目录 |
| B2 | 高 | Infrastructure 作为独立层不划算 | 仅 `MusicPlaylist.cs`、`TrashManager.cs` 两个静态类、178 行；仅 App 引用；测试放在 App.Tests 且命名空间为 `WMCL.Core.Tests` |
| B3 | 中 | UI 工程只做了一半 | 实际是主题/字体/资源程序集，仅 `ExpanderReveal.cs` 一个控件；App 仍有 44 个控件文件、4 个 style 文件和 `App.axaml` 内联主题 |
| B4 | 中 | Link 边界混入无关内容 | `Link/NiriWindowRuleService.cs` 是桌面合成器配置，与联机无关；对 Minecraft 的唯一用途是 `ServerPingService.SplitAddress` |
| B5 | 中 | 两套 Infrastructure 概念 | `WMCL.Infrastructure` 与 App 内 `Services/Infrastructure`（5 个文件，命名空间仍是 `WMCL.App.Services`）并存 |
| B6 | 低 | 拆分未收尾 | 工作区残留 `Services/Integration`、`Core/Music`、`App/Assets/Fonts` 空目录（未进 Git）；CHANGELOG 0.2.2 宣称的 Services 拆分未完全落地 |
| B7 | 中 | 结构文档与实际脱节 | `CONTRIBUTING.md` 只列 8 个工程，漏 Link/Infrastructure/UI；依赖方向描述过时；`GUIDE.md:128` 仍写“仓库不使用 GitHub Actions” |

### 6.3 架构缺口

| ID | 级别 | 问题 | 证据 |
|---|---|---|---|
| C1 | 高 | 缺少共享应用层，CLI 与 App 各自编排 | `Cli/Commands/InstallCommands.cs:36`、`GameCommands.cs:56` 直接 new Repository/Installer/LaunchPreparation；App 在 `AppServices.cs:45` 做同样的事；启动前校验参数已不同 |
| C2 | 低 | CLI 能力/行为边界不明确 | CLI 不引用 Link/Infrastructure；`delete` 走 `InstanceManager.Delete` 永久删除（`src/WMCL.Minecraft/InstanceManager.cs:44`），而 App 走 `TrashManager` 回收站。若差异是刻意的，应在文档写清；否则是行为不一致 |
| C3 | 低 | 缺少架构文档 | 没有说明“代码放哪个工程、依赖允许朝哪个方向”的文档；0.2.2 新增 Link/Infrastructure 后文档没有同步 |

### 6.4 测试组织

| ID | 级别 | 问题 | 证据 |
|---|---|---|---|
| D1 | 高 | 测试工程与生产工程不对齐 | 10 个 src 工程只有 4 个测试工程；`WMCL.Minecraft.Tests` 71 个文件，实际覆盖 Minecraft + Modding + Download + Launch |
| D2 | 高 | 测试归属混乱 / CLI 零测试 | Link/Infrastructure/UI 测试在 App.Tests；CLI 零测试；`App.Tests/TrashManagerTests.cs` 命名空间是 `WMCL.Core.Tests` |
| D3 | 中 | App.Tests 平铺 | 216 个 `.cs` 全部在一个目录，与源码子目录不对应 |
| D4 | 中 | 分片与分类脆弱 | `scripts/test-fast.sh:14` 按类名首字母 A–Z 分片；1675 个测试方法只有 34 处 `Kind` Trait 标注 |
| D5 | 低 | 测试配置不一致 | `coverlet.collector` 只在 Core/Launch/Minecraft 三个工程；`xunit.runner.json` 四份重复 |
| D6 | 中 | 测试依赖声明不透明 | `WMCL.App.Tests` 只引用 App，通过传递引用来访问 Link/Modding 等类型 |

### 6.5 构建、版本、CI、仓库卫生

| ID | 级别 | 问题 | 证据 |
|---|---|---|---|
| E1 | 中 | 版本元数据不一致 | `WMCL.App.csproj:35` `<Version>0.2.2</Version>`，`:37-38` `AssemblyVersion/FileVersion` 仍为 `0.2.1.0` |
| E2 | 中 | 没有中央包管理 | 14 个 csproj 各自重复 TFM/Nullable/ImplicitUsings；Avalonia 版本在 7 处硬编码；无 `Directory.Packages.props` |
| E3 | 中 | CI 重复打包 | `ci.yml:70` 每次 push(main)/PR 构建全部发布包；`release.yml` 的 gate 复用 ci（又打一次包），package job 再打一次 |
| E4 | 中 | 文档漂移 | `GUIDE.md:128` 与 `.github/workflows/*` 矛盾；CHANGELOG 0.2.2 的结构调整只完成一部分 |
| E5 | 低 | 截图目录约定自相矛盾 | `screenshots/` 有 86 张 PNG + README（5.1 MB）；`PageScreenshotGenerator.cs:114` 注释说该目录被 `screenshots/.gitignore` 忽略、不提交（该文件实际不存在），但目录 README 说纳入版本管理且 PNG 已被跟踪；顶层 README/GUIDE 没有入口。`.git` 约 99 MB（历史含已删除的字体/截图） |
| E6 | 低 | 字体声明缺失 | 内置 HarmonyOS Sans 未出现在 `THIRD_PARTY_NOTICES.md`（声明只列 Inter/Lucide） |

---

## 7. 优先级汇总

| 优先级 | 条目 |
|---|---|
| **高** | A1、A2、A3、A4、B1、B2、C1、D1、D2 |
| **中** | A5、A6、A7、A8、B3、B4、B5、B7、D3、D4、D6、E1、E2、E3、E4 |
| **低** | A9、B6、C2、C3、D5、E5、E6 |

---

## 8. 整改路线与任务清单

建议按 Phase 0 → 4 执行；每个 Phase 单独提交，避免一次大 diff。

### Phase 0：基线

- [ ] `git status` 确认除本文档外工作区干净；从 `main` 拉整改分支。
- [ ] `dotnet build WMCL.slnx -c Release`，记录基线 0 warning / 0 error。
- [ ] 跑一次目标测试（至少 Core/Launch/Minecraft；App 可按 `scripts/test-fast.sh` 分片），记录基线失败用例（如有）。
- [ ] 确认本文档已在 `docs/STRUCTURE-REVIEW.md`，后续改动引用条目 ID。

### Phase 1：低风险清理（建议一个 PR）

- [ ] **E1** 修正版本元数据：把 `AssemblyVersion/FileVersion` 改成 `0.2.2.0`，或删除这两项让 SDK 从 `<Version>` 推导。
- [ ] **B5** `Services/Infrastructure` 的命名空间与目录对齐（改为 `WMCL.App.Services.Infrastructure`），或把这些文件移到合适位置。
- [ ] **B6** 删除空目录 `Services/Integration`、`Core/Music`、`App/Assets/Fonts`；更新 CHANGELOG 0.2.2 的“结构分层”描述，标明哪些已完成、哪些延后。
- [ ] **B7/E4** 更新 `CONTRIBUTING.md` 的项目结构与依赖方向（10 个工程 + 4 个测试工程）；更新 `GUIDE.md` 的 CI/发布章节，使其与 `.github/workflows` 一致。
- [ ] **E2** 新增 `Directory.Packages.props`（CPM），把 Avalonia/CommunityToolkit/xunit 等版本集中；把 TFM/Nullable/ImplicitUsings 收进 `Directory.Build.props`。
- [ ] **E6** 在 `THIRD_PARTY_NOTICES.md` 补 HarmonyOS Sans 的来源与许可说明（先核对许可文本）。

验收：`dotnet build` 0/0；`dotnet format whitespace ... --verify-no-changes` 通过；文档与 `WMCL.slnx` 一致。

### Phase 2：边界收紧（建议 1–2 个 PR）

- [ ] **B4** 把 `ServerPingService.SplitAddress`（连同 `ServerPingServiceTests` 中的相关用例）迁到 Core 的通用网络/地址工具；然后删除 `WMCL.Link` 对 `WMCL.Minecraft` 的 ProjectReference。
  - 注意：**先迁移、后删引用**，不能直接删引用。
- [ ] **B1** 把 `EasyTierNodeList` 迁到 `WMCL.Link`；把 `EasyTierNodeListTests` 从 Core.Tests 迁到 Link 对应测试工程。
  - 这依赖 D1 的测试工程方案；如果暂不拆测试工程，可先保留在 Core，并在文档标注“待迁移”。
- [ ] **B4** 把 `NiriWindowRuleService` 移出 Link：放到 App 的平台服务目录，或新建 `WMCL.Platform`。
- [ ] **B2** 给 `WMCL.Infrastructure` 做决定：
  - 方案 A（推荐，成本低）：并入 `WMCL.Core`（`TrashManager` → `Core/IO`，`MusicPlaylist` → `Core/Music`），删除工程。
  - 方案 B：保留工程但改名为能描述内容的名字，并补接口/依赖注入。
- [ ] **B3** 给 `WMCL.UI` 做决定：
  - 方案 A：改名为 `WMCL.Theme` / `WMCL.Design`，只保留样式/字体/资源，把 App 的 44 个控件留在 App，并在文档写清边界。
  - 方案 B：把 App 的通用控件全部迁入，做成真正的设计系统。
  - 注意：改工程名会改 `avares://WMCL.UI/...` 资源 URI、csproj 引用和打包脚本，需全局替换并重新打包验证。
- [ ] **C1** 决定 CLI 的定位：如果 CLI 是一等交付物，抽 `WMCL.Application`（安装/启动用例）；如果只是附属工具，在文档写清“CLI 不共享 App 编排”的边界。同时确认 CLI `delete` 的永久删除行为是否有意（App 走回收站）。

验收：依赖图仍无环；`WMCL.Link` 不再引用 `WMCL.Minecraft`；构建/测试通过；文档更新。

### Phase 3：App 重构（收益最大，建议独立 PR）

- [ ] **A2** 建 Feature 目录，先只搬文件、不改代码：
  - `Features/Launch`、`Features/Instances`、`Features/Mods`、`Features/Link`、`Features/Settings`、`Features/Downloads`。
  - 每个 feature 下放 View/ViewModel/局部服务；先搬 Launch 和 Mods 两个最痛的。
- [ ] **A3** 拆 `LaunchViewModel`（账号/皮肤/版本/启动/整合包更新）与 `ModsViewModel`（组件目录/本地 Mod/搜索/更新/依赖）；沿用 partial，但一个 partial 只放一个职责。
- [ ] **A4** 把 `MainWindowViewModel` 的页面注册/导航抽成 `PageRegistry` / `NavigationService`；页面改为按需创建（至少非首屏页面），减少 25 个 VM 的启动开销。
- [ ] **A6** 把 `ResourceBrowserNavigation` 里的共享逻辑提成 `ResourceBrowserController`（或 base class），消除 `ModsViewModel` / `InstanceContentViewModel` 的重复实现。
- [ ] **A7** 收敛 `AppServices` 的消费面：为 VM 引入窄接口/门面（如 `IInstallFacade`、`IAccountFacade`），不要每个 VM 都拿 30 个成员。
- [ ] **A8** 评估 VM 对 Avalonia 的依赖：至少把图片加载、UI 线程调度抽到服务层；如果暂时不改，在文档中记录为已知权衡。

验收：行为不变；测试通过；`LaunchViewModel` / `ModsViewModel` 主文件显著下降；新增页面只需改一处注册。

### Phase 4：测试与 CI（建议 1–2 个 PR）

- [ ] **D1/D2** 先修归属，不急着拆工程：
  - 把 Link/Infrastructure/UI 测试移到对应命名空间/目录（或至少移到 App.Tests 下按 feature 分的子目录）。
  - 把 `TrashManagerTests` / `MusicPlaylistTests` 的命名空间改为与所在工程一致。
  - 给 CLI 加最小 smoke test（`help`、参数解析、dry-run）。
- [ ] **D3** App.Tests 按 feature 建子目录（`Launch/`、`Instances/`、`Mods/`……），文件名保持，降低查找成本。
- [ ] **D4** 用 Trait 或目录过滤替代首字母分片；给现有测试补 `Kind`（Unit / Headless / UiInteraction / Motion / Integration）。
- [ ] **D6** 给测试工程显式声明它实际使用的 ProjectReference（至少不要让 App.Tests 完全依赖传递引用）。
- [ ] **E3** CI 拆分：`push/PR` 只 restore + format + build + test；发布包只在 tag 或手动触发；release 复用 ci 的构建产物，不重复打包。

验收：测试归属清晰；CI 时间下降；全量测试通过。

### Phase 5：资产与历史清理（可选）

- [ ] **E5** 统一 `screenshots/` 的约定：当前 `PageScreenshotGenerator.cs:114` 注释与 `screenshots/README.md` 冲突。二选一：保留并纳入版本管理（在顶层 README 加入口、更新注释），或改为 CI artifact/Release 资产（更新 README、删除跟踪的 PNG）。如需瘦身历史，使用 `git filter-repo`（会改写历史，需团队确认）。
- [ ] **A9** 把 `App.axaml` 内联的 `ControlTheme` 移到 `WMCL.UI` / 主题资源；确认 `InstallViewModel` 里第二个 `ModsViewModel` 是否必要。
- [ ] **C3** 新增 `docs/architecture.md`，至少包括：依赖图、各工程职责、允许的依赖方向、测试工程与生产工程的对应关系。

---

## 9. 约束与禁止事项

1. **不要直接删除 `WMCL.Link -> WMCL.Minecraft` 引用**，先迁移 `SplitAddress` 并验证（见 5.2）。
2. **不要一次性大搬迁 App**；先 feature 目录搬文件，再拆类，保持每个 PR 可回滚。
3. **不要引入大型 DI 容器**；项目约定是手工组合根（见 `CONTRIBUTING.md`）。
4. **不要改程序集名 `WMCL.App`**；打包脚本、单文件发布和更新检查依赖它。
5. **不要开启程序集裁剪**；`GUIDE.md` 明确要求。
6. **不要修改 PCL2/PCL-CE 的素材与许可声明**；新增/移动素材前先确认来源与署名。
7. **不要提交** `bin/`、`obj/`、`artifacts/`、`dist/`、日志、测试结果、`secrets.local.props`。
8. **不要在测试中操作真实实例**（删除/更新/导出/导入）；使用 `tests/TestEnvironment.cs` 的隔离路径。
9. 不要为了让测试通过而放宽断言或跳过用例；先记录基线失败，再逐个处理。

---

## 10. 验收清单

- [ ] `dotnet build WMCL.slnx -c Release`：0 warning / 0 error。
- [ ] 目标测试通过；全量测试（或 `scripts/test-fast.sh` + 串行组）通过。
- [ ] `dotnet format whitespace WMCL.slnx --verify-no-changes --no-restore` 通过。
- [ ] `git diff --check` 通过。
- [ ] 依赖图无环；没有新增“下层引用上层”。
- [ ] `WMCL.Link` 若已去掉 Minecraft 引用，Link 相关测试仍通过。
- [ ] `CONTRIBUTING.md` / `GUIDE.md` / `docs/architecture.md` 与实际结构一致。
- [ ] 版本号在 UI、程序集元数据、Git tag、Release 标题四处一致。

---

## 11. 附录

### 11.1 可复现统计命令

```bash
# 生产代码文件数（排除 bin/obj）
find src -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | wc -l
find src -name '*.axaml' -not -path '*/obj/*' -not -path '*/bin/*' | wc -l

# App 各目录文件数
for d in Views ViewModels Services Controls Styles Animation; do
  printf '%s ' "$d"
  find "src/WMCL.App/$d" -type f \( -name '*.cs' -o -name '*.axaml' \) | wc -l
done

# 最大源文件
find src tests -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 \
  | xargs -0 wc -l | sort -rn | head -20

# 测试方法数
grep -rhoE '\[(Avalonia)?(Fact|Theory)\]' tests --include='*.cs' | wc -l

# 项目依赖
grep -rn 'ProjectReference' src tests --include='*.csproj'
```

### 11.2 关键文件索引

| 用途 | 文件 |
|---|---|
| 解决方案 | `WMCL.slnx` |
| 组合根 | `src/WMCL.App/Services/AppServices.cs` |
| 主窗口 VM | `src/WMCL.App/ViewModels/MainWindowViewModel.cs` |
| 最大 VM | `src/WMCL.App/ViewModels/LaunchViewModel.cs` |
| 资源浏览器共享逻辑 | `src/WMCL.App/ViewModels/ResourceBrowserNavigation.cs` |
| 主题入口 | `src/WMCL.App/App.axaml` |
| UI 资源工程 | `src/WMCL.UI/WMCL.UI.csproj` |
| 空壳层 | `src/WMCL.Infrastructure/WMCL.Infrastructure.csproj` |
| Link 的 Niri 服务 | `src/WMCL.Link/NiriWindowRuleService.cs` |
| Link 的地址解析依赖 | `src/WMCL.Link/EasyTierService.cs:119` |
| 测试隔离 | `tests/TestEnvironment.cs` |
| 测试分片 | `scripts/test-fast.sh` |
| CI | `.github/workflows/ci.yml`、`.github/workflows/release.yml` |
| 文档 | `CONTRIBUTING.md`、`GUIDE.md`、`THIRD_PARTY_NOTICES.md` |

### 11.3 给下一个窗口的交接提示（可直接粘贴）

> 请阅读 `docs/STRUCTURE-REVIEW.md`，按第 8 节 Phase 0 → 4 执行。先做 Phase 0 基线和 Phase 1 低风险清理；不要直接删除 `WMCL.Link` 对 `WMCL.Minecraft` 的引用（见第 5.2 节），也不要一次性大搬迁 App。每个 Phase 单独提交，按第 10 节验收。
