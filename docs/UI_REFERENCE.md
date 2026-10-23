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
- 任务中心缺少原版逐任务日志、失败堆栈、持久化/重试。
- 所有下载/安装入口的 HTTP 取消与内部进度尚未统一覆盖。
- CE/ME 扩展：LabyMod 实例、ME 蓝图/收藏、CurseForge 数字 ID 远程分享码未接。
- Jar-in-Jar 只读嵌套 Jar/冲突告警；Forge 版本范围/完整依赖图未接。
- Windows/macOS 自动更新、真实 OAuth/CurseForge、高 DPI、真实进世界/声音/输入/长时间运行未验收。

## 参考入口

- PCL2：`PageLaunchLeft`、`PageDownloadLeft`、`PageDownloadCompDetail`、`PageInstanceLeft`、`MyPageRight`、`MyListItem`、`MyCard`。
- PCL-CE：`PageDownloadLeft`、`PageDownloadCleanroom`、`PageDownloadLabyMod`。
- PCL-ME：`PCL.Frontend.Avalonia` 的启动页/下载页/设置页。
