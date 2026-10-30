# PCL WMCL 未推送提交审计报告

> 审计范围：本地 main 领先 origin/main 的 20 个提交（`88b087f..ca5108e`），共约 5800 行新增。
> 远程 HEAD：`a1cbf69`（docs: update v0.1.1 artifact hash），标签 v0.1.0 / v0.1.1。
> 审计时间：2026-10-29 ~ 10-30 · 方法：人工逐行审查 + 并行分层审计 + 全量测试验证

---

## 0. 总体验证结果

| 检查项 | 结果 |
|--------|------|
| `dotnet build WMCL.slnx -c Release` | ✅ 0 Error / 0 Warning |
| `dotnet test` | ✅ 1936 通过，0 失败（Core 53 / Launch 139 / Minecraft 832 / App 912） |
| `dotnet format whitespace --verify-no-changes` | ✅ EXIT=0 |
| `git diff --check` | ✅ EXIT=0 |
| 敏感信息扫描（密钥/密码/令牌） | ✅ 无泄漏（仅测试夹具与设置字段名） |
| 调试代码残留（Console.Write / TODO / HACK） | ✅ 无 |
| 误提交产物（bin/obj/exe/pdb） | ✅ 无 |
| 硬编码微软 Client ID | ✅ 无（用户自配，文档明确指引） |
| 本地化键对齐（en-US vs zh-CN） | ✅ 26 键完全对齐，无空值 |

**总体结论：这 20 个提交质量较高，可推送。** 但发现 4 个值得修复的功能缺陷（详见第 2 节）与 1 个中危安全问题，建议修复后再推送或尽快跟进修复。

---

## 1. 提交构成

- 1 个大型 feat 提交 `88b087f`：79 文件，+5358/-109（回收站、UI 缩放、快捷键、配置导入导出、下载暂停/恢复、镜像测速、皮肤编辑器、故障反馈、工具箱、欢迎引导等）
- 19 个 fix 提交：均为审计驱动的针对性修复，改动小而聚焦（1~12 文件），提交信息规范（均声明"测试：1936 通过"）
- 资金线检查：无 force-push / rebase 痕迹，历史线性干净，提交信息与实际 diff 相符

## 2. 发现的问题（按严重度排序）

### 🔴 高：LocaleInstaller 资源名解析错误 → 本地化完全失效

- 位置：`src/WMCL.App/Services/LocaleInstaller.cs:24-26`
- 提交：`88b087f`
- 嵌入资源名实际为 `WMCL.App.Assets.Locales.zh-CN.yaml`（已在编译产物 DLL 中验证）。代码用 `Path.GetFileNameWithoutExtension(name)` 提取 locale 名，但该 API 只按目录分隔符取"文件名"——对点分清单资源名会返回 `WMCL.App.Assets.Locales.zh-CN` 而非 `zh-CN`。
- 后果链：locale 文件被写成 `WMCL.App.Assets.Locales.zh-CN.yaml` → `Localizer.LoadFromDirectory` 以文件名（不含扩展）作为 locale 键 → `SetLocale("zh-CN")` 匹配不到 → 所有 `Localizer.T()` 调用永远回退返回原始 key 字符串 → **欢迎引导窗口 7 处文本全部显示为 "welcome.title" 等 key**。
- 无测试覆盖：`LocalizerTests` 只测 `LoadFromDirectory`（该函数本身正确），未测 `LocaleInstaller.EnsureInstalled` 的资源名→文件名转换，所以 1936 个测试全绿也发现不了。
- 修复建议：`var fileName = name[(name.LastIndexOf("Locales.", StringComparison.Ordinal) + "Locales.".Length)..];` 或在 csproj 给 EmbeddedResource 加 `LogicalName="Assets.Locales.%(Filename)%(Extension)"`。

### 🔴 高：macOS 代理检测解析格式错误 → 永远返回 null

- 位置：`src/WMCL.Core/ProxyManager.cs:168-184`（`ParseScutilProxy`）
- 提交：`126ff39`（该提交声称修复 macOS 支持，实际引入错误解析）
- 代码假设 scutil 输出行形如 `HTTPProxy : 127.0.0.1:8080`（host:port 合一），但真实 `scutil --proxy` 输出是**两个独立行**：`HTTPProxy : 127.0.0.1` 与 `HTTPPort : 8080`。
- 逐步验证：`trimmed["HTTPProxy".Length+1..]` 得到 `": 127.0.0.1"`，去掉冒号后 `split(':')` 得 `host=''`，触发 `IsNullOrWhiteSpace(host)` → 返回 null。
- 后果：macOS 系统代理自动检测**从未工作过**，永远静默回退。
- 无测试覆盖：`ProxyManagerTests` 12 个测试全部只测 `NormalizeAddress`/`Apply`/`ResolvedAddress`，无 scutil 解析测试。
- 修复建议：分别解析 `HTTPProxy`/`HTTPSPort`（及 `HTTPSProxy`/`HTTPSPort`）两行再拼 `host:port`；同时解析 `HTTPSEnable` 判断启用状态。

### 🟠 中：DataProtectionService macOS 密钥派生错误 → 所有 Mac 派生相同密钥

- 位置：`src/WMCL.Launch/Auth/DataProtectionService.cs`（`GetMachineId` macOS 分支）
- 提交：`88b087f`
- ioreg 输出行形如 `"IOPlatformUUID" = "3D82A4C5-…"`，`line.Split('"')[1]` 取到的是属性名 `"IOPlatformUUID"` 而非 UUID 值（应为 `[3]`）。
- 后果：所有 macOS 机器派生出**完全相同的密钥**，令牌加密形同虚设（任何拿到密文的人都能解密）。在 Linux/Windows 上密钥派生正确（machine-id / MachineGuid），仅 macOS 受影响。
- 加密实现本身正确（AES-256-GCM、每次随机 nonce、版本前缀、篡改检测测试齐全），但密钥派生的平台 bug 让 macOS 的保护等于零。
- 修复建议：`line.Split('"')[3]`，并加解析失败回退。

### 🟡 低：批量安装暂停后"继续"只恢复单个版本

- 位置：`src/WMCL.App/ViewModels/DownloadViewModel.cs:392-402`（`ResumeAsync`）
- 提交：`88b087f` 引入暂停功能，`adb3a19` 修复竞态时未覆盖此场景
- `InstallBatchAsync` 期间 `CanPause = IsBusy && !IsPaused` 为 true（暂停按钮可见），但 `ResumeAsync` 只调用 `InstallCoreAsync(SelectedVersion)` 重装单个版本，批量上下文丢失。
- 后果：批量安装中暂停→继续，会变成只安装当前选中版本，剩余版本静默丢弃，与用户预期不符。
- 修复建议：暂停时记住安装模式（单/批量），Resume 分派到对应方法；或批量安装期间禁用暂停按钮。

### 🟡 低：TrashManager 若干小问题

- 位置：`src/WMCL.Core/TrashManager.cs`
- 提交：`88b087f`
- a) `Restore` 使用 `trash.json` 中的 `versionId` 拼接目标路径，未校验其不含 `..` / 路径分隔符（元数据被篡改时可路径穿越）。实际风险低：trash.json 由本应用写入，但建议加 `Path.GetFileName(versionId)` 校验。
- b) `Restore` 恢复后 `trash.json` 元数据文件残留在实例目录中（`MoveToTrash` 时写入），会被 Minecraft/后续导出看到。
- c) `ListEntries` 中 `if (info.LastWriteTime < DateTime.MinValue) continue;` 是死代码（LastWriteTime 不可能小于 MinValue，未初始化时为 MinValue 本身——条件永假）。
- d) `AutoClean` 依据目录 LastWriteTime 而非 trash.json 的 `deletedAt`，删除进回收站时写入 trash.json 不会更新目录时间戳，天数计算可能偏差。

### ⚪ 信息：敏感设置明文落盘（设计取舍，推送前知悉即可）

- `AppSettings.ProxyPassword` / `CurseForgeApiKey` / `MicrosoftClientId` 经 `SettingsService.Save`/`ExportToJson` **明文**写入 `settings.json` 与导出 JSON。
- 账户令牌已用 DataProtectionService 加密（设计合理），但代理密码与 CF API Key 未加密；导出的配置文件若分享会直接泄漏代理凭据。
- 建议：导出时脱敏（Password/ApiKey 置空），本地落盘可保持现状（PCL2 原版同样明文）。

### ⚪ 信息：诊断报告含游戏目录与玩家名（合理）

`FeedbackViewModel` 诊断信息含 GameDirectory/PlayerName/JavaPath 等，不含密码/令牌，属合理范围。

## 3. 19 个 fix 提交逐项审计结论

| 提交 | 声称修复 | 审计结论 |
|------|----------|----------|
| `0b4e6e7` | 设置导入导出/资源泄漏/UI重叠 | ✅ 属实。反射跳过游戏目录防误改、ApplyShortcuts、IDisposable 补齐、Grid 行重排均正确；配套 SettingsImportExportTests 4 测试质量高（含非法 JSON 兜底） |
| `126ff39` | 下载线程/回收站清理/macOS代理 | ⚠️ 部分属实。MaxConcurrency Math.Clamp、AutoClean 接入 Program.cs 正确；**但 macOS 代理解析本身是错的（见高2）** |
| `f7034aa` | 全量 CTS 泄漏 + IsBusy 检查 | ✅ 属实。12 文件 19 处 `_cts?.Dispose()` 插入点全部正确（均在创建新 CTS 前释放旧实例），InstallAllAsync 入口检查正确 |
| `74140bd` | MicrosoftLoginViewModel IDisposable | ✅ 属实。App.axaml.cs try/finally 释放正确 |
| `0684534` | ToggleResourcePack 补全 + 保留选中 | ✅ 属实。新方法与 DeleteResourcePackAsync 模式一致；Refresh 保留选中项用 Name 匹配合理；TaskManager 订阅改具名处理程序正确 |
| `2efa191` | ApiSelectionViewModel.Clear CTS | ✅ 属实。单行修复正确 |
| `68b3040` | LinkViewModel Terracotta CTS 泄漏 | ✅ 属实。补 Terracotta 停止与两个 CTS 释放正确 |
| `697db10` | MinecraftNewsWindow CTS 泄漏 | ✅ 属实。`Interlocked.CompareExchange` 置 null + 始终释放本地 CTS，修复了旧逻辑"Closed 已置 null 则跳过 Dispose"的漏洞，分析正确 |
| `99cd4cf` | 提示文字 + BackupWorld 刷新 | ✅ 属实。文案与实际行为相符（GameDirectory 变更会触发刷新） |
| `7a31fc2` | 切换实例未清空 server.properties | ✅ 属实。清空时机在 LoadInstanceSettings 后，正确 |
| `40534e4` | SaveServerProperties 双重验证 | ✅ 属实。实例 ID + 目录双重校验合理，防跨实例误写 |
| `4ab87d8` | 服务端按钮 IsEnabled | ✅ 属实。`!IsServerRunning`/`IsServerRunning` 绑定正确 |
| `7541403` | LoadInstanceSettings 崩溃防护 | ✅ 属实。try/catch 包裹 + Core 提取，Status 提示合理 |
| `1e67882` | LoadServersAsync 竞态 | ✅ 属实。请求 ID 模式与 HANDOVER 记载模式一致，catch 内也判断请求 ID |
| `49e00db` | AuthlibLoginViewModel IDisposable | ✅ 属实。ReleaseView 中登录后立即 Dispose 正确 |
| `e05a656` | RemoteImage 缓存上限 | ✅ 属实。200 条上限 + 淘汰最早键；`Cache[url]=bytes` 同时修复了 TryAdd 竞态。注意 HANDOVER 已声明非 LRU 是有意设计 |
| `11ab02e` | 粘贴兜底选区删除 | ✅ 属实。`SelectionStart`/`SelectionEnd` 语义正确（CaretIndex 可能在选区右端导致删错方向） |
| `ca5108e` | 移除重复 IsBusy 检查 | ✅ 属实。纯删重复行 |
| `adb3a19` | 暂停/继续 + 竞态 | ✅ 基本属实。请求 ID 防旧任务干扰新任务、finally 分离 CTS 释放与状态清理均正确；但遗留"批量暂停恢复只装单版本"场景（见低4） |

**fix 提交与提交信息的一致性：19/19 声称的修复均能在 diff 中落实**（其中 1 个提交引入了新的解析 bug）。

## 4. 大提交 `88b087f` 分层审计结论

- **Core 层**：TrashManager（见低5 项）、ProxyManager Linux gsettings 路径正常、ILocalizer 本身正确
- **Launch 层**：DataProtectionService 加密结构正确（macOS 密钥派生 bug 见中3）、EnsureNatives 标记文件方案合理、LaunchPlanBuilder/JavaRequirement 改动无异常
- **App Services**：GpuDetector lspci 正则合理、WaylandClipboard 双超时保护良好、SharedAssetComparer JSON 语义比较合理、LocaleInstaller（见高1）
- **App ViewModels**：镜像测速逻辑健全（8s 超时/3s 采样/失败入列）、FeedbackViewModel 无敏感泄漏、TaskManagerViewModel 历史过滤修复正确
- **Views/控件**：快捷键解析有默认兜底、SkinPixelEditor 与 VM 接口契约验证一致（PaintPixel/PixelsVersion 均存在）、粘贴兜底逻辑正确
- **测试**：新增 15 个测试文件质量良好（篡改检测、nonce 唯一性、非法输入兜底、快捷键匹配均有效断言）；盲区在 LocaleInstaller 与 scutil 解析（正是两个高问题漏网之处）
- **CI**：新增 format 检查合理
- **csproj**：AssemblyName 保持 WMCL.App ✅；`<EmbeddedResource>` 与 `<AvaloniaResource>` 双重包含 Assets/**（会双重打包，体积增大，建议改 `Assets/Locales/*.yaml` 仅 EmbeddedResource——注意这正是高1 的关联场景）

## 5. 推送建议

1. **建议修复后推送**（按优先级）：
   - 高1 LocaleInstaller 资源名解析（3 行改动）
   - 高2 ParseScutilProxy 格式（重写该函数，~20 行）
   - 中3 macOS 密钥派生 `[1]→[3]`（1 行改动）
   - 低4 批量暂停恢复（中等改动，可推后）
2. 若先推送再修，务必尽快跟进——高1 影响所有首次运行用户的欢迎引导体验，高2/中3 影响 macOS 用户。
3. HANDOVER.md 仍是未跟踪状态，若要入库需先 `git add`。

---

*本报告基于本地仓库 320 提交（HEAD `ca5108e`）审计生成。*

---

## 13. 第二轮修复记录（2026-10-30）

审计完成后对所有发现问题做了统一修复（含三个并行分层审计 subagent 的补充发现），并与原 20 个提交重整为 3 个逻辑提交（feat / fix / docs）：

### 新增修复（第二轮审计补充发现）

| # | 严重度 | 问题 | 修复 |
|---|--------|------|------|
| 20 | 🔴 高 | ProxyMode 用 `Enum.TryParse` 解析 UI 值 none/system/custom（枚举名 NoProxy/SystemProxy/CustomProxy）永远失败，**代理功能整体静默失效**（Program.cs + SettingsViewModel 双处） | 新增 `ProxyManager.ParseMode` 显式映射 |
| 21 | 🔴 高 | 系统代理模式从未接线：`DetectSystemProxyAsync` 结果只写 UI 状态文本，未回存 `SystemProxyAddress` → Apply 得 null → 静默直连 | 启动与设置页双处回存后再 Apply |
| 22 | 🔴 高 | ZGC 参数拼成 `-XX:+UseZGCGC`（本机 JVM 实测 Unrecognized VM option），**选 ZGC 即无法启动游戏** | 重写 GC 策略映射表（UseZGC 等正确选项名） |
| 23 | 🔴 高 | mcbpack 导出使用虚构 schema（无 addons 字段、文件在根目录、加载器版本硬编码 "1.0.0"），**自家导入器 100% 抛"缺少 addons"** | 重写为标准 MCBBS 格式：addons 数组 + overrides/ 前缀 + FindLoaderVersion |
| 24 | 🟠 中 | 测速 URL 两个 404：截断哈希 `4b5c7d4b/client.jar`、拼写错误 `version_manivel_v2.json` | 官方镜像改为从版本清单动态解析 client.jar 直链，失败回退清单对比 |
| 25 | 🟠 中 | CrashAnalyzer `Has()` OR 语义误用：日志单独出现 `libjawt.so` 即误诊"需 Windows/Wine 运行" | 新增 `HasAll`，两特征须同时出现；删除重叠的 UnsupportedClassVersionError 规则 |
| 26 | 🟠 中 | SyncLaunchLog 每次把全文重灌日志面板（一次启动 N 行 → O(N²) 追加、内容重复滚动） | 按行数增量同步 + 重置检测 |
| 27 | 🟠 中 | `--title`/`--renderer` 是幻影参数（Minecraft 不支持，joptsimple 静默忽略） | 移除参数生成、UI 设置项、存储字段整条链路 |
| 28 | 🟠 中 | "-Xms 可大于 -Xmx"（JVM 拒绝启动）、GpuIndex=0（集显）仍叠加强制 NVIDIA offload、清理垃圾整删 downloads（与自定义下载互踩）、gsettings 端口 0 拼 host:0、EnsureNatives 在 UI 线程同步解压、GpuDetector 同步 ReadToEnd 可能卡 UI、账号文件非原子写入 | 全部修复（钳制/条件跳过/7 天缓存语义/Task.Run/异步读/临时文件+Move） |

### 最终验证

- 构建：0 Error / 0 Warning
- 测试：**1976 通过，0 失败**（原 1936 + 新增 40 个回归测试：ProxyManagerParseTests 8、TrashManagerHardeningTests 7、DataProtectionUuidTests 7、SettingsExportRedactionTests 3、LaunchPlanBuilder GC/堆/幻影参数 15）
- `dotnet format whitespace --verify-no-changes` ✅
- `git diff --check` ✅

---

## 14. 第三轮修复记录（2026-10-30 下午，Views/控件/测试层审计补充）

第三个并行审计代理（Views/XAML/控件/测试/文档层）报告的发现，对照当前 HEAD 核实后修复：

| # | 严重度 | 问题 | 状态 |
|---|--------|------|------|
| 29 | 🔴 高 | **Ctrl+1~5 快捷键默认全灭**：数字走 `Enum.TryParse<Key>` 按枚举数值解析（"1"→Key.Cancel=1），用户按主键盘数字产生 Key.D1(=35) 永远不匹配；"Ctrl+3" 还与 "Ctrl+Tab" 解析冲突（子代理用独立 .NET 实验证实） | ✅ 提取 `ShortcutParser` 服务：数字映射 `Key.D0`~`Key.D9`，22 个回归测试 |
| 30 | 🟠 中 | 修饰键严格相等比较：Ctrl+Shift+Tab（`Control\|Shift ≠ Control`）永远不命中，注释宣称的反向循环不可达 | ✅ 改"必需齐全"子集语义，Shift 区分方向 |
| 31 | 🔴 高 | **欢迎引导窗按钮区缺 Grid.Row/Grid.Column**：三个按钮全叠在 Column 0 互相覆盖，仅最顶层（跳过）可点，且与内容区重叠 | ✅ 修复布局（Row=1，Back/Next/Skip 各归其位） |
| 32 | 🟡 低 | 皮肤编辑器选画笔字号后橡皮擦仍激活（同组单选按钮只切 UI 状态不切 VM） | ✅ OnBrushSize 时置 `IsEraser=false` |
| 33 | 🟠 中 | SkinPixelEditor.Render 每帧每像素 new SolidColorBrush（64×64 → 每帧最多 4096 个未冻结画刷），长笔画 UI 开销显著 | ✅ 冻结画刷按颜色缓存 |
| 34 | 🟡 低 | 指针捕获丢失（窗口失活）无兜底，`_isPainting` 滞留 true → 之后鼠标掠过无按键连续绘制 | ✅ 补 `OnPointerCaptureLost` |
| 35 | 🟡 低 | ViewModel 附加后才被替换时不重订阅，新 VM 的 PixelsVersion 变更不触发重绘 | ✅ OnPropertyChanged 重订阅（含旧 VM 退订） |
| 36 | 🟡 低 | 撤销栈无上限（快照约 16KB/笔无界累积） | ✅ 200 层上限 |
| 37 | 🟡 低 | 快捷键表每次 KeyDown 反序列化 JSON | ✅ 缓存 + 失效检测 |
| 38 | 🟠 中 | ProxyManagerTests 改写 HTTP_PROXY 等环境变量不恢复、无串行 Collection → 跨类污染同程序集网络测试 | ✅ ctor/Dispose 保存恢复 + Collection |
| 39 | 🟡 低 | KeyboardShortcutTests 无 XDG 隔离（构造链读真实 ~/.local/share/WMCL）；"环绕"测试从未断言环绕 | ✅ 隔离 + 双向环绕断言 |
| 40 | 🟡 低 | WelcomeViewModelTests 断言恒真（缺 locale 时 T() 返回键名本身，"非空"必过） | ✅ 具体值断言 + locale 加载链路测试 |

报告中被后续提交已修复的项（粘贴兜底 CaretIndex→11ab02e、暂停恢复→adb3a19、SettingsView 行重叠→0b4e6e7、快捷键回显→0b4e6e7 的 ApplyShortcuts）确认已落实；VERIFICATION.md 测试计数陈旧与 TrashManagerTests 的 fixture 语义混乱属信息级，暂不处理。

### 最终验证（三轮修复后）

- 构建：0 Error / 0 Warning
- 测试：**2003 通过，0 失败**（Core 74 / Launch 155 / Minecraft 832 / App 942）
- `dotnet format whitespace --verify-no-changes` ✅ · `git diff --check` ✅

---

## 15. 第四轮修复记录（2026-10-30 下午，三个审计代理遗留的中低优先级项）

| # | 严重度 | 问题 | 状态 |
|---|--------|------|------|
| 41 | 🟠 中 | 运行时改代理对已建全局 HttpClient 无效（环境变量只在 handler 构造时读取，UI 却提示"立即生效"） | ✅ Http.ResetClient + ProxyManager.Apply 联动重置；ToolboxViewModel 弃用自建实例改用全局客户端 |
| 42 | 🟠 中 | modinfo 注释与协议不符（声称 Fabric/NeoForge，实际仅旧版 FML ≤1.12 使用；NeoForge 走 forgeData.mods） | ✅ 容错解析：forgeData.mods（modId/modmarker）+ modinfo（大小写字段名变体），新增 2 个协议测试 |
| 43 | 🟡 低 | MoveToTrash 同秒同名目标冲突（Directory.Move 到已存在路径抛 IOException） | ✅ 冲突时追加序号 + 测试 |
| 44 | 🟡 低 | ExportConfigAsync 无 await 的 async、导出无文件选择器与导入不对称 | ✅ 改 SaveFilePickerAsync + 提取共享 GetStorageProvider |
| 45 | 🟡 低 | 启动路径 natives 解压异常直接中断启动（与"缺文件跳过"策略不一致） | ✅ IOException/InvalidDataException 隔离并警告继续 |
| 46 | 🟡 低 | WelcomeViewModel 定义在视图 code-behind（架构不一致） | ✅ 移入 ViewModels/；StepProgress 走 Localizer（新增 welcome.progress 键） |
| 47 | 🟡 低 | Localizer.T 无占位符格式化支持 | ✅ 合并签名 T(key, fallback, args)，Format 异常兜底 + 4 个测试 |
| 48 | 🟡 低 | DataProtectionService 子进程 stdout/stderr 顺序读（注释称防死锁实则不能） | ✅ 并发读取 |
| 49 | ⚪ 信息 | VERIFICATION.md 测试计数陈旧（1858 vs 实际） | ✅ 更新为实际值 |

### 最终验证（四轮修复后）

- 构建：0 Error / 0 Warning；`dotnet format whitespace --verify-no-changes` ✅；`git diff --check` ✅
- 测试：**2009 通过，0 失败**（Core 78 / Launch 155 / Minecraft 834 / App 942）
