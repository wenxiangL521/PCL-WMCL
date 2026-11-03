# PCL / CE / ME UI 对照

仅记录结构与已知差异，不是逐像素验收；参考源码只读审阅，仓库同步仅按用户明确要求安全快进，不作本地源码修改。

## 实例资源与新闻（2026-11-03）

- 管理页顶部操作区在滚动容器外，列表/下载浏览器在其下方滚动；只有一套工具条，不用复制底部按钮或仅限高冒充固定。Mod搜索/清除与选择同列，单项明细工具和批量操作区分。
- 管理页底部与下载入口使用同一ModResourceBrowser/InstanceResourceBrowser：筛选、图标结果卡、分页、详情、版本分组、依赖、收藏、保存及安装。实例模式目标只读锁定，返回详情恢复原结果与滚动，不重建业务模型。
- 每个子页检查更新后打开对应类型的复选确认窗口，不混合Mod/资源包/光影；当前→新版、全选/全不选、选择计数和逐项结果可见，关闭不安装。来源未知或存档目录不能可靠安全更新时明确说明，不编造更新项。
- 导出页按用途选择备份ZIP或分享整合包，路径、格式和导出按钮集中；维护工具折叠，实例备份通过原“导入整合包”入口恢复，不在单实例导出页再放全局导入。
- 首页新闻为占满可用列宽的紧凑横向图文；独立新闻窗口自动计算列数并均分宽度。图片16:9、标题换行加tooltip，不固定300/280卡宽造成右侧空白。布局与fake操作测试覆盖420~1200宽、往返resize，不等同原生全平台视觉验收。

## 主框架

| 区域 | PCL2 / CE | WMCL |
|---|---|---|
| 顶栏 | 启动 / 下载 / 联机 / 设置 / 更多 | 同 5 项，图标为 Lucide/自有矢量 |
| 下载侧栏 | 原版、Mod、整合包、数据包、资源包、光影、存档；CE 另有 Cleanroom/LabyMod | 原版、Cleanroom、LabyMod、Mod、整合包、数据包、资源包、光影、存档、资源收藏 |
| 设置侧栏 | 启动、Java、游戏管理、联机、日志、更新、个性化、其他 | 同 |
| 版本卡 | 卡头展开/收起，版本行选择，方块图标 | 同；方块图标来自 PCL2/CE |
| 实例侧栏 | PCL2 6 项 RadioBox（含双 Mod 条件项） | 十项直接展示，固定右侧宿主，无“更多内容”Flyout |
| 页面切换 | MyPageRight 动画 | `PclPageAnimation` / `CatalogExpansionAnimation` |

## 跨页面布局与窗口呈现（2026-11-03）

- UI缩放保留FontScale兼容字段但语义是真实布局0.8~1.5，含文字、图标、间距与自有子窗口；缩放包装层与原RootFrame启动动画隔离，避免动画将比例写回1。弹层使用Avalonia原生InheritsTransform而非重新挂接打开的Popup；高缩放窄窗口顶栏切为图标并保留名称提示。鼠标拖缩放滑块时先更新数值，松开后应用布局，避免滑块随自身比例移动产生反馈；键盘和程序修改立即应用。
- WmclFormRow在有限窄栏把标签移到输入上方，宽栏保持110+12的横排；无限量测不依旧Bounds，避免布局回路。快捷键按有最小可用宽度的输入对换行，路径支持滚动与完整提示。Mod/整合包/内容搜索条件复用同样260宽标签输入对，默认两列/窄栏一列，不再硬挤到固定150旁的星号列。
- 版本行收藏/齿轮/更多各30px命中区、16px图标，间距6px并垂直居中，右列固定预留；悬停只改变透明度，不挤标题，不重新启用普通列表0.8缩放。菜单锚定更多按钮右下。
- Java管理运行时分层显示版本/架构/路径，安装按钮可换行，取消仅安装时出现；菜单8/11/17/21/25，手动安装必须精确主版本和当前架构。Adoptium元数据不可达可用同发行商GitHub发布信息，仍校验size+SHA256、受限解压和执行探测后才提交；不改变自动启动时的Java兼容选择策略。
- ComboBox弹层、禁用态输入背景与光标使用共享主题，联机高级、实例服务器/导出Expander复用已有平色卡片模板，不留Fluent灰板；无内容的公网/NAT/摘要/日志隐藏，避免空块。
- 窗口透明度与 PCL-ME 一致：滑块0~100%映射为窗口不透明度下限~100%，整窗含卡片与侧栏一起透出模糊桌面。下限按背景自适应：深色主题或有背景图（存在深色衬底）用PCL-ME的40%，浅色主题且无背景图用75%，避免浅色下深色文字被明亮桌面洗白。滑块范围恢复0~100，设置与持久化同步；不做逐控件alpha保护层，也不再对控件叠加单独透明度。桌面背景模糊仍由合成器负责，不对文字套BlurEffect，设置显示实际后端透明级别。
- “跟随系统”不能再在设置Default后立即读ActualThemeVariant（此时可能仍是旧变体），改为读PlatformSettings；并监听ActualThemeVariantChanged，在系统主题运行中变化时重绘强调色/页面/侧栏，避免浅色底配深色主题白字。
- 已核实当前Niri26.04(a8562a11)支持background-effect，WMCL在该桌面走XWayland、app-id/WM_CLASS为WMCL.App。Niri模式只有点击应用并确认后才备份、校验和写入WMCL专用规则；不会静默改全局blur或其他窗口，取消/验证失败/外部编辑冲突不覆盖原文件。符号链接配置提供复制规则回退。规则不写opacity以免与应用透明度重复相乘；合成器规则可覆盖原生blur请求。
- 原生Niri隔离窗口已核对前景不随底色变淡（独立XDG与游戏根，截图时底色参数为73%）；最低20%与40%由浅深主题真实Headless渲染/字形扫描验证，另有150%布局场景。此前75%整窗淡化截图仅为已撤销历史。未应用真实Niri规则，不把这些等同于所有平台/系统主题验收。

## 页面与分区过渡、百宝箱分组（2026-11-03）

- 同一页面实例内切换分区（如 启动设置 → 管理 Java）时，TransitioningContentControl 看不到内容变化，原先没有任何动画。现在导航会在同页分区变化时触发160ms淡入（可减少动效），跨页仍走CrossFade；PageHost自带的Opacity过渡会与手动淡入抢值，已在代码中移除，仅保留页面内容过渡。回归覆盖启动→Java、启动设置→Java、Java→启动设置三条路径，并补齐此前缺失的 `settings-launch` 路由（缺省会落到启动页）。
- 百宝箱按PCL2工具行重排：清理垃圾、启动计数、创建快捷方式、抽奖合并为“实用工具”卡片内的行（标题/说明/状态在左，按钮靠右居中），服务器查询、下载测速、镜像对比并入“网络诊断”，目录入口、皮肤预览、自定义下载各自成卡。全部原有命令与绑定保留，850宽下不再一功能占一整张大卡。

## 交互动效作用域

- `ListBox.subTabs` 导航侧栏保留192ms悬停进入缩放及颜色反馈；正在播放时鼠标移出/移回不取消、不复位或重启，同一条目继续原时间线，不同条目独立播放。完成后清除运行状态，下一次正常进入可再次播放；选择变化/任务区复位仍可取消，减少动态效果时跳过缩放。
- `ListBox.taskRows` 的整行悬停/按压缩放禁用；其他含内联操作的普通列表不强制从0.8倍开始，避免影响点击定位。不能为修复任务区抖动而禁用整个应用的侧栏反馈。

## 许可约束

- Windows 版固定蓝色主色相（`hue=210`、`saturation=85`、`lightAdjust=0`），隐藏 HSL/彩色预设；配置加载与应用层都有兜底，防止拷贝配置绕过。

## 已对齐的页面

- 启动页：账户切换、版本选择、启动/取消、日志、崩溃提示、新闻卡。
- 下载：版本目录、加载/错误面板、组件卡、资源详情、安装目标。
- 社区资源图片（2026-11-02）：Mod、整合包及共用资源内容列表图标统一为72px，详情96px，尺寸集中在 WmclDimensions；使用 Uniform 保留完整比例，Auto 图标列与剩余文字列分开。19项相关测试通过，覆盖420/660宽度和详情切换，本次未重复全量测试。
- 实例：概览、设置、Mod、资源包/光影/存档/截图、导出。
- 账户：微软设备码、Authlib/Nide、皮肤与披风、CDKEY。
- 微软登录与皮肤入口（2026-11-02）：设备码生成后自动复制并打开固定官方验证页，每会话一次，手动复制/打开与重试保留；登录后左侧仅一个“皮肤与披风”，修改/刷新/保存全部放入已有 MsSkinWindow，删除左栏重复内联编辑器及事件处理。皮肤窗口模型选择与操作分行、按钮换行、右侧滚动，适配620px最小宽度。
- 设置：启动、Java、游戏管理、联机、日志、更新、个性化、其他。

## 皮肤与披风模型预览（2026-11-02）

- 左侧 `SkinModelPreview` 是完整纹理方块角色：头、躯干、双臂、双腿及透明外层，用CPU正交投影和逐面纹理映射绘制，不引入WebView/游戏引擎；classic/slim、64×64/64×32和等比例HD源纹理支持。左键拖动 yaw/pitch 全向转动，右/中键平移、滚轮缩放，方向键调整视角，Home/重置复原全部视图参数。模型静态直立，不模拟布料物理或走路骨骼动画。
- 俯仰与水平旋转都可连续360°翻转，没有±60°或±89°限位，也没有“极端俯仰”开关/临时警告。角度仅按等价整周归一，非有限值才回默认；正面/背面/重置仍可恢复常用姿态。
- 拖拽每次以当前姿态增量续接，跨过顶部、底部及整周边界后反向移动立即响应，不累计回拖死区。
- 披风朝外主图使用图谱(1,1,10,16)，贴背内衬使用(12,1,10,16)，独立于皮肤正背UV；采用非对称F图案及左右色条进行正背像素验证，避免把内外面接反或水平翻转。
- 所选披风有独立前后纹理，选中即下载预览（固定Minecraft纹理服务、无Bearer凭据、可取消且限制PNG大小/尺寸），只有“应用披风”才改账号；无披风清空，迟到纹理不得覆盖新选择。
- 窗口按钮统一轻描边/主题强调字，32px高、3px圆角，窄窗口自动换行。右侧滚动，预览区固定可操作；深色主题沿用任务强调色，禁用不填灰。
- “修改皮肤”显示本地PNG选择区，支持系统选择器和本地路径回退。选择/解码成功后仅本地模型预览，明确“上传并应用”才提交；取消、选择器失败和坏PNG均有可见反馈。上传并发受控，过期账号提示重新登录，换账号的迟到上传/刷新结果不污染当前预览。
- 像素编辑器改为独立 `SkinEditorWindow`（默认1060×800、最小760×560，可最大化），大画布占剩余空间，右侧实时角色预览；滚轮以鼠标为锚点缩放，右/中键拖动平移，放大/缩小/适应画布按钮可用。视图缩放平移与鼠标像素命中采用同一变换，纹理仍是原64×64/64×32，不改变PNG尺寸。编辑是独立本地副本，“应用到预览”返回主窗口，只有后续“上传并应用”才修改账号，取消不改变父预览。
- 渲染改为有界CPU光栅化：每像素插值深度、opaque深度缓冲、半透明逐像素远到近合成，外层及披风双面，透明像素不写深度；纹理采样限制在对应面的texel范围，消除采到相邻图谱的白边。底面方向及披风四个薄面的UV另有独立角标校验，不以调换整个skin正背掩盖错误。
- 编辑工具栏新增可视调色盘：HSV二维选色、色相条、透明度、24个常用色/肤色及8个会话最近色；当前色块、HEX输入和真实画笔同步。吸色按钮或Ctrl+左键按缩放/平移后的实际纹理像素取RGBA，不修改像素或增加撤销；颜色选择会退出橡皮擦。无需手输代码即可绘画，HEX仅为可选高级输入。
- BGRA→ARGB按正确通道读取，棋盘格展示透明像素。预乘alpha经Skia编码往返可能造成半透明RGB 1级舍入，测试严格限制该容差，不掩盖通道交换。
- 模型缺省为 `auto`：按现代皮肤两臂unused透明条识别slim，旧64×32回退classic；手动classic/slim明确覆盖推断。预览、编辑会话、本地结果和确认上传共用同一选择，模型下拉框对当前账号也立即生效；从细臂编辑器接受结果时记住实际slim，不能回落到classic默认值。UI显示识别类型，auto上传前解析成服务接受的classic/slim，不向服务提交auto。
- 公共皮肤下载改为账号+URL+内容哈希缓存，刷新会取新数据并保留UI仍引用的旧位图；服务选择ACTIVE皮肤，保留接口variant作为信息，不再用它与默认classic在预览/本地切换时覆盖用户模型选择。

## 已知差异（未完成）

- PCL2 `Setup.ini` 仅双向同步 DisplayType/描述/自定义 Logo；IsStar、内置 Logo、版本缓存未接。
- 新闻是启动页 3 条带图卡 + 独立瀑布流窗口，不是 CE 首页整页新闻流。
- Authlib 皮肤衣柜目前打开网页/独立窗口，不是 PCL2 整页路由；披风按 PCL2 不做站内管理。
- 实例截图是内嵌缩略图列表 + 预览，不是 PCL2 的完整截图瀑布流。
- 资源详情是级别按钮，不是 PCL2 每行保存/默认目录；安装仍要求实例。
- 实例导航按用户要求直接列出十项，不逐项照搬 PCL2 六个 RadioBox；修改/Mod/资源管理统一在壳内复用页面，不再混用主页面跳转。
- 所有下载/安装入口的 HTTP 取消与内部进度尚未统一覆盖（取消清理与统一格式已接入下载/资源入口，其余入口取消按钮已有幂等守卫）。
- CE/ME 扩展：LabyMod 实例、ME 蓝图/收藏、CurseForge 数字 ID 远程分享码未接。
- Jar-in-Jar 只读嵌套 Jar/冲突告警；Forge 版本范围/完整依赖图未接。
- Windows/macOS 自动更新、真实 OAuth/CurseForge、高 DPI、真实进世界/声音/输入/长时间运行未验收。

## 参考入口

- PCL2：`PageLaunchLeft`、`PageDownloadLeft`、`PageDownloadCompDetail`、`PageInstanceLeft`、`MyPageRight`、`MyListItem`、`MyCard`。
- PCL-CE：`PageDownloadLeft`、`PageDownloadCleanroom`、`PageDownloadLabyMod`。
- PCL-ME：`PCL.Frontend.Avalonia` 的启动页/下载页/设置页。

## 图标来源清单（二期 A6-2a）

| 键名 | 语义 | 来源 | 许可 |
|---|---|---|---|
| WmclIconGear | 设置/齿轮 | Material Symbols（settings） | Apache-2.0 |
| WmclIconFolderOpen | 打开目录 | Material Symbols（folder_open） | Apache-2.0 |
| WmclIconMore | 更多操作 | Material Symbols（more_horiz） | Apache-2.0 |
| WmclIconRotateCcw | 重置/还原 | Material Symbols（rotate_left） | Apache-2.0 |
| WmclIconRefresh | 刷新 | Material Symbols（refresh） | Apache-2.0 |
| WmclIconSearch | 搜索 | Material Symbols（search） | Apache-2.0 |
| WmclIconClose | 关闭/清除 | Material Symbols（close） | Apache-2.0 |
| WmclIconChevronDown | 展开箭头 | Material Symbols（expand_more） | Apache-2.0 |
| WmclIconPlus | 新增 | Material Symbols（add） | Apache-2.0 |
| WmclIconDownload | 下载 | Material Symbols（download） | Apache-2.0 |
| WmclIconPlay | 启动 | Material Symbols（play_arrow） | Apache-2.0 |
| WmclIconStar | 收藏 | Material Symbols（star） | Apache-2.0 |
| WmclIconTrash | 删除 | Material Symbols（delete） | Apache-2.0 |
| WmclIconCheck | 完成 | Material Symbols（check） | Apache-2.0 |
| WmclIconWarning | 警告 | Material Symbols（warning） | Apache-2.0 |
| WmclIconError | 错误 | Material Symbols（error） | Apache-2.0 |
| WmclIconInbox | 空状态 | Material Symbols（inbox） | Apache-2.0 |
| WmclIconNetwork | 联机 | Material Symbols（lan） | Apache-2.0 |
| WmclIconLink | 链接 | Material Symbols（link） | Apache-2.0 |
| WmclIconInfo | 信息 | Material Symbols（info） | Apache-2.0 |

存量图标：`NavIcons.cs`（Lucide，ISC）与 `TopTabIcons.cs` 为存量遗留、宽松许可，本期不扩充。
所有新增图标均未复用 PCL2/PCL-CE 的 path data、位图或字体素材。

## 实例页固定导航壳（2026-11-01）

- 左侧可滚动菜单直接展示：概览、设置、修改、Mod 管理、导出、服务器、资源包、存档、光影、截图；删除“更多内容”按钮与 Flyout，保留设置重置/导出刷新内联图标动作。
- 右侧使用统一实例选择器与 `WmclPagePadding`。概览/设置/导出/服务器继续使用原卡片；修改、Mod、资源包/存档/光影通过既有 `Application.DataTemplates` 复用同一个 Install/Mods/Content 模型及视图，截图复用 ScreenshotView，不复制业务实现。
- `CurrentPage` 保持 Instance 壳；分区切换只改 `SelectedSectionIndex/EmbeddedPage`，不增加返回栈、不跳回概览。进入壳临时隐藏全局侧栏，离开或单次返回恢复原主页面，避免双侧栏。修改提交接受后在壳内回概览。
- 隐藏实例刷新不改变独立下载页的共享模型上下文；快速换分区/实例、根目录切换和迟到加载都有 request/root/id 检查。截图按实例实际隔离目录读取，切换后不继续显示旧目标。
- 已生成十个分区的 Headless 布局预览；普通按钮共享透明禁用模板，复用页面不再引入默认灰色实心禁用块。文件选择与确认仍保留必要弹窗，不把系统文件对话框强行改为导航页。

## 任务管理控件（2026-11-01）

对照本机 PCL2 `MyCard` 的平色卡片、13px 标题、小箭头、5px 圆角，以及 `MyButton` 的3px圆角轻描边处理；不采用 Avalonia 默认 Expander 的灰色内容板。实现集中于 [WmclTaskControls.axaml](../src/WMCL.UI/Styles/WmclTaskControls.axaml)，通过任务专用 class 匹配，不改变其他页面的默认折叠控件。

- 任务操作按钮与日志 ToggleButton 统一30px高、13px字、3px圆角、透明底/轻描边；选中与悬停为轻蓝反馈，禁用态保留淡文字与透明底，不再整块填灰。
- “任务详情 / 最近完成 / 完整堆栈”统一为整宽36px标题行、小箭头和展开分隔线，内容延续白色卡片/深色对应底色，日志用轻微主题底。历史任务以留白与细线分隔，长详情省略并保留完整 tooltip，操作在窄屏允许换行。
- 任务整行不再被 ListBox 选中/悬停底色染成大块蓝板，先前的整行缩放禁用保持不变；必要高亮集中在标题和操作按钮。
- `WmclTaskAccent` 随主题色相变化，暗色使用较高亮度；双主题测试验证默认主题强调文字与卡片底色至少4.5:1对比。
- 折叠状态通过双向 `IsExpanded` 绑定，保留鼠标/空格切换、日志开关、历史筛选/清空、独立重试/复制操作。模板行为对照 [Avalonia 11.3.10 Expander](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/11.3.10/src/Avalonia.Controls/Expander.cs)，不是直接复制原版 PCL 控件源码。
- 已生成浅/深主题折叠、历史展开、全部展开六帧 Headless 预览；只证明静态布局与自动化交互，不代表原生窗口或逐像素验收。

## 共用消息弹窗（2026-11-01）

`ConfirmWindow` 统一启动确认、下载/安装失败和删除确认：无系统标题栏，透明窗口内使用平色消息卡、5px 圆角、1px 主题描边、标题分隔线和描边按钮，不再叠加整窗渐变与内层卡片。正文可选择复制，长错误滚动显示，浅/深主题复用现有颜色 token。

- 启动正版提示明确使用“继续启动 / 取消”，不再借用“确定删除”。离线/第三方账号首次点击启动时提示，确认后立即持久化 `MinecraftPurchaseNoticeAcknowledged`，后续启动、重建页面或重启启动器不再提示；取消不视为确认。配置导入保留本机确认状态，不采用外部配置的确认值。
- 失败提示使用红色标题与普通“确定”按钮；只有真正的删除/清理等破坏性确认使用危险按钮，并默认聚焦取消。
- Esc 取消，普通确认支持默认按钮；关闭动画有重复触发守卫，保留既有 `ModalMotion` 运动体系。
- 此项为 WMCL 的 PCL2 风格收敛，不宣称与原版 MyMsg 逐像素一致；原生阴影、跨平台焦点和高 DPI 仍需实机验证。输入弹窗与账号登录窗口不在本轮范围。

## 二期美容差异清单（ui-pcl2-polish，2026-10-30）

已对齐：顶栏三色标对称渐变（48/54/48）、页面背景亮度 91（色相±15）、Color4 `65+l`、灰阶 Gray6~8、圆角七档/字号四档 token、按钮五级（描边+hover 变蓝，无实心蓝）、卡片圆角 5/阴影半径 3/可点击抬升、图标全矢量（20 键）、三态组件（骨架/空态/状态点）、MyHint/MySearchBox、侧栏选中块（6px+Blue6 描边）、页面外边距 20,15,20,10、启动页标题区/圆头像/线性分段图标/新闻三态、下载页按钮层级/时间戳可读/分组卡 hover 与展开态、联机页标题区/状态点/齿轮入口/主次卡、设置页按钮层级/数值等宽/输入 hover/下拉箭头 14px。

保留/顺延（含理由）：
1. h1/h2 保留弹窗标题语义（20/16），页面/分组标题由 pageTitle/groupTitle 承接（spec A3-1a 字面 h1=16/h2=13 与类语义归位的裁决，待评审）。
2. 侧栏项高 36 vs PCL2 42（存量布局，未改）。
3. WmclPage 背景不叠加个性化 lightAdjust（design D2 裁决：页面底保持亮度 91）。
4. 崩溃块保留 WmclCrash* 独立语义色（局部覆盖 hint.error，登记偏差）。
5. MySearchBox 仅在组件层定型，六页搜索框迁移随 B6（P2）。
6. 联机失败态判定依赖既有状态文案关键词（IsIdle/IsBusy/RoomStatus），无独立失败布尔。
7. LaunchView 日志框直角 0（有意）。
8. Slider Thumb 圆角 8 为圆形语义（直径/2），扫描白名单登记。
9. 存量 NavIcons（Lucide/ISC）与 TopTabIcons 不扩充。
10. B5 实例页重排、C3 MyMsg 结构收敛、B6 搜索类六页、设置页卡内二级分组（需拆分 12 行大 Grid）均按 P2 顺延。
11. 联机主卡底色由 spec Blue8 微调为 Blue7 + Blue5 描边（L1 截图裁决：Blue8 与卡片底差异过弱）。
12. 头像圆形：64px 元素 × WmclCornerCircle 32（半径=直径/2 语义）。

二期 L1 截图归档：`docs/verification/ui-pcl2-polish/screenshots/`（{light|dark}-{page}.png 与 -scrolled 帧）。
