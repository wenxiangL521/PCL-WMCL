# UI 向 PCL2 风格美化（二期）—— 执行完成记录

> 特性：`ui-pcl2-polish`　规格：`.codeartsdoer/specs/ui-pcl2-polish/{spec,design,tasks,pcl2-baseline}.md`
> 参考基线：**主参考 PCL2（`../pcl2-research/pcl2`），PCL-CE 辅助，忽略 PCL-ME**
> 回归基线：**2085** 项测试（App 1018 / Core 78 / Launch 155 / Minecraft 834）
> 状态：**C1~C12 全部完成并入库**（2026-10-30）；全量 2096 测试通过、0 warning、格式/空白通过；推送被 PAT workflow scope 阻断（待用户处理）

## 一、已完成并提交（C1~C6，工作区测试与构建均通过后才提交）

| 提交 | 内容 |
|---|---|
| `4aefdf4` C1 | 二期色板与三色标对称渐变（顶栏 48/54/48、页面背景亮度 91/色相±15、Color4 65+l、Gray6~8/Success/PageFlat、卡片阴影半径 3 + hover 档、ThemeService 同构三色标、TopbarHueDelta）+ 3 处一期断言同步（规格变更）+ PageScreenshotGenerator（可配置目录/滚动帧） |
| `507cda2` C2 | `WmclDimensions.axaml`（19 键：圆角七档/字号七档/间距五档）双处合并；全局样式 token 化；pageTitle/pageSubtitle/groupTitle 类；视图圆角迁 30 处、字号越档迁 26 处；DownloadActionButton 20.8→20 连带断言 |
| `94b82e9` C3 | 按钮五级（default 改 Blue1 描边 / primary Blue8+Blue3 / secondary 透明+Divider / tertiary / danger）+ disabled 置末 + card.clickable（阴影 120ms + hover Blue8）+ 启动按钮内联清理 + ConfirmWindow 破坏性文案→danger |
| `8b880b2` C4 | `WmclIcons.axaml`（20 StreamGeometry，Material Symbols Apache-2.0，来源登记 UI_REFERENCE）+ iconButton/size16/20/24 + 11 处字符图标替换清零 + 删除回退 TextBlock |
| `dd25ed2` C5 | `WmclSkeleton` / `WmclEmptyState` / `WmclStatusDot` + hint/divider/skeleton/dataDisplay 样式族 |
| `43f613b→amend` C6 | `MyHint`（TemplatedControl.Template 版）/ `MySearchBox`（TemplatedControl + App.axaml ControlTheme）/ rowAction 局部样式收敛 / `ControlContractTests` 3 项 |

**L4 断言状态（C6 后全绿）**：PolishStyleTests 8 项（三色标/背景亮度/Color4/灰阶/维度 token/圆角零字面量/字号零越档/字符图标清零）、ControlStyleTests 9 项、StyleTokenTests 3 项、ControlContractTests 3 项。

## 二、已完成但未验证/未提交（组 7 B0 代码改动已在工作区）

- `MainWindow.axaml`：侧栏 subTabs 选中块（圆角 WmclCornerListItem=6、Blue7 底 + Blue6 1px 描边 + BrushTransition、选中文字 Blue2 SemiBold、项字号 token）；RootFrame 底部新增 `WmclPageFlat` 纯色底（`IsVisible="{Binding !BackgroundColorOverlay}"`）
- `MainWindowViewModel.cs`：`PageMargin` 25,10,25,25 → **20,15,20,10**
- 未跟踪：`tests/WMCL.App.Tests/PolishStyleTests.cs`（组 0 红骨架，现已全绿，待组 12 定稿入库）、`ControlContractTests.cs`（已随 C6 入库）

## 三、后续已全部完成（组 8~12，提交 C8~C12）

- **组 8 启动页 B1**：标题区（pageTitle/pageSubtitle）、主 CTA 复核、头像圆形（WmclCornerCircle）、账户三段图标线性化、新闻三态（Skeleton/EmptyState/MyHint）、左栏底部平衡
- **组 9 下载页 B2**：刷新按钮层级、时间戳 muted、收缩起卡 hover/展开指示条、右卡按钮栈层级
- **组 10 联机页 B3**：双卡主次（card clickable）、标题 16/描述 13、MaxWidth 520、齿轮图标钮进标题区、状态点四态映射（LinkView.axaml.cs 读既有属性）、窄窗堆叠
- **组 11 设置页 B4**：卡内分组（groupTitle+divider+行高 36）、按钮层级、数值等宽与滑块间距、输入焦点/悬停与 13.5、下拉箭头 14px
- **组 12 收尾**：PolishStyleTests 定稿入库（清占位/白名单注释）、L1 截图复跑（44+22+2）、四件套、UI_REFERENCE 差异清单、rollback.md

## 四、恢复后的执行清单（bash 可用时按序）

1. `dotnet build WMCL.slnx -c Release`（验证组 7 改动；预期 0 warning）
2. `dotnet test WMCL.slnx -c Release`（预期 2085+3 ControlContractTests=2088 全绿）
3. 提交 C7：`git add src/WMCL.App/Views/MainWindow.axaml src/WMCL.App/ViewModels/MainWindowViewModel.cs`，提交信息 `feat(ui): 页面框架（侧栏选中块/边距/平面底）`
4. 按第三节继续组 8~11（每页一提交 C8~C11）
5. 组 12：PolishStyleTests 入库 + L1 复跑 + 四件套 + 文档

## 五、环境故障记录

- **bash 工具**：本次连续 5 次不同形式（含 `ls`、`git status`、`dotnet build`）均被拒绝，错误为权限拒绝/安全守卫组件不可用。恢复后按上表继续；若持续不可用，需人工执行第四节的命令。
- **页面截图**：`PageScreenshotGenerator` 已支持 `WMCL_SHOT_DIR` 与滚动帧，默认输出 `docs/verification/ui-pcl2-polish/screenshots/`。
- **识别到的注意点**：`MyHint` 必须用 `UserControl.Template`（否则 `Content` 会覆盖 XAML 根 Border）；模板内部件需 `OnApplyTemplate` + `NameScope.Find`（x:Name 不生成字段）。

## 六、关键设计裁决（已落地，勿回退）

1. `h1/h2` 保留弹窗语义，页面/分组标题用 `pageTitle/groupTitle`（D1，已在 UI_REFERENCE 待登记）。
2. C4 不新建控件类型，用 `Button.iconButton + size16/20/24` 样式类（D7）。
3. PCL2 按钮是"描边 + hover 变蓝"，**无实心蓝主按钮**；primary 用 Blue8 底 + Blue3 描边 + Blue2 文字。
4. 滚动条加宽只走 `Width` 过渡（PclPageAnimation 独占 `ScrollBar.RenderTransform`）。
5. Windows 蓝色锁定（210/85/0）结构不变，`TopbarHueDelta` 在锁定路径恒 0。