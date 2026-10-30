# PCL / CE / ME UI 对照

仅记录结构与已知差异，不是逐像素验收；参考仓库始终保持只读。

## 主框架

| 区域 | PCL2 / CE | WMCL |
|---|---|---|
| 顶栏 | 启动 / 下载 / 联机 / 设置 / 更多 | 同 5 项，图标为 Lucide/自有矢量 |
| 下载侧栏 | 原版、Mod、整合包、数据包、资源包、光影、存档；CE 另有 Cleanroom/LabyMod | 原版、Cleanroom、LabyMod、Mod、整合包、数据包、资源包、光影、存档、资源收藏 |
| 设置侧栏 | 启动、Java、游戏管理、联机、日志、更新、个性化、其他 | 同 |
| 版本卡 | 卡头展开/收起，版本行选择，方块图标 | 同；方块图标来自 PCL2/CE |
| 实例侧栏 | PCL2 6 项 RadioBox（含双 Mod 条件项） | 5 主项 + “更多内容”Flyout |
| 页面切换 | MyPageRight 动画 | `PclPageAnimation` / `CatalogExpansionAnimation` |

## 许可约束

- Windows 版固定蓝色主色相（`hue=210`、`saturation=85`、`lightAdjust=0`），隐藏 HSL/彩色预设；配置加载与应用层都有兜底，防止拷贝配置绕过。

## 已对齐的页面

- 启动页：账户切换、版本选择、启动/取消、日志、崩溃提示、新闻卡。
- 下载：版本目录、加载/错误面板、组件卡、资源详情、安装目标。
- 实例：概览、设置、Mod、资源包/光影/存档/截图、导出。
- 账户：微软设备码、Authlib/Nide、皮肤与披风、CDKEY。
- 设置：启动、Java、游戏管理、联机、日志、更新、个性化、其他。

## 已知差异（未完成）

- PCL2 `Setup.ini` 仅双向同步 DisplayType/描述/自定义 Logo；IsStar、内置 Logo、版本缓存未接。
- 新闻是启动页 3 条带图卡 + 独立瀑布流窗口，不是 CE 首页整页新闻流。
- Authlib 皮肤衣柜目前打开网页/独立窗口，不是 PCL2 整页路由；披风按 PCL2 不做站内管理。
- 实例截图是内嵌缩略图列表 + 预览，不是 PCL2 的完整截图瀑布流。
- 资源详情是级别按钮，不是 PCL2 每行保存/默认目录；安装仍要求实例。
- 双“Mod 管理”条件项未复制；`HasSubTabs` 侧栏不是 6 项 RadioBox。
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
