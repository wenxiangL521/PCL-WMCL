# 验证状态

更新：2026-10-30（UI 向 PCL2 风格美化二期 ui-pcl2-polish）。本文只保留当前摘要；详细历史证据在仓库外归档，不随仓库发布。

## 自动测试

- 全解决方案：**2096 通过**，0 失败/跳过。
- App **1029**、Core **78**、Launch **155**、Minecraft **834**。
- 命令：`dotnet test WMCL.slnx -c Release`。
- 最近 TRX：`../wmcl-validation-tmp/priority4/windows-blue-theme/`。
- Release 构建：**0 warning / 0 error**；`dotnet format whitespace --verify-no-changes` 与 `git diff --check` 通过。

## 原生静态/交互证据

| 项目 | 证据 |
|---|---|
| Cleanroom 真实启动 | `../wmcl-validation-tmp/cleanroom-real-launch6.png`（1.12.2 主菜单，人工终止） |
| 启动页新闻 | `../wmcl-validation-tmp/home-news-final.png`（自动加载、带图、整卡点击） |
| 侧栏图标 | `../wmcl-validation-tmp/download-icons.png`、`settings-icons.png` |
| Windows exe | `dist/win-x64/WMCL.exe`，PE32+ GUI x64，47 MB，内嵌 6 尺寸图标 |

## 已验证范围

- Windows 主题合规：强制蓝色主色相、隐藏 HSL/彩色预设，3 个专项测试通过。
- 任务中心增强（R1）：逐任务日志（环形 500 条/脱敏）、失败堆栈展示与复制、失败任务按原参数重试、历史持久化 v2（原子写/退出终态化/v1 兼容）、刷新合并调度与差量更新——Headless 覆盖 46 项测试。
- 取消清理与进度统一（R2）：取消路径 `.part` 清理（暂停保留续传）、进度统一格式（ProgressFormatter 逐字节等价锚点）、取消幂等守卫。
- CI 三平台矩阵（R3）：ubuntu/windows/macos 构建+测试（format 仅 ubuntu）；tag 发布流 `release.yml` 待首个 `v*` 标签实证（CI 运行记录为证据）。
- UI 向 PCL2 风格美化二期（A 设计系统全量 + C1/C2/C4 + B0~B4 四页重排，L4 证据）：三色标对称渐变/页面背景亮度 91/Color4 公式/Gray6~8/圆角七档与字号四档 token/按钮五级/卡片阴影 3/20 矢量图标与 11 处字符清零/三态组件/MyHint·MySearchBox/侧栏选中块/四页重排；L1 截图 64 帧归档 docs/verification/ui-pcl2-polish/screenshots/。
- UI 向 PCL2 风格对齐一期（P0 全量 + P1 四样式，L4 证据）：悬空资源键清零（StyleTokenTests B1）、本期 token 双主题齐备（B2）、axaml 内联色值清零按值白名单豁免（B3）、标题栏渐变不变量与字节序锁定、滚动条/ToolTip/Slider/菜单/按压反馈契约（ControlStyleTests 共 9 项断言）。
- 原版安装/校验/修复、Java 自动下载、CLI/GUI 启动。
- Fabric/Forge/NeoForge/OptiFine/Cleanroom 安装与部分真实启动。
- Modrinth/CurseForge 搜索/文件/依赖（CurseForge 需 Key）。
- 本地 Mod、资源包、光影、存档、截图、实例克隆/重命名/删除。
- 整合包 `.mrpack` 导入/导出、实例 zip 导入/导出。
- 微软设备码/刷新（mock）、Authlib/Nide、皮肤/披风基础操作。
- EasyTier/陶瓦流程（受 TUN/中继条件限制）。
- Windows 单文件 exe 构建；Linux tar.gz/AppImage/deb（历史验证）。

## 未完成/不可宣称

- Windows/macOS 实机运行、签名、SmartScreen 体验；exe 未签名。
- 真实 Azure OAuth、真实 CurseForge Key 的完整 E2E。
- 高 DPI、跨平台组合矩阵、原生逐帧动画。
- 进入世界、声音、输入注入、长时间运行；Cleanroom 只到主菜单。
- 资源详情每行保存/默认目录、Windows/macOS 自动更新。
- 非下载入口（整合包/组件安装等）的内部进度细粒度覆盖；tag 发布流未经首个标签实跑。
- UI PCL2 对齐批次的 L1/L2 人工验证：双主题截图（标题栏渐变两端取色、8px 圆角、暗色叠层/关闭悬停红、WelcomeWindow 着色）、悬停交互（滚动条加宽、ToolTip、菜单）、滑块拖动联动、按压反馈巡检；Dark 叠层取值为初值，待截图裁决微调。
- UI 二期范围（MyMsg/MyHint/MySearchBox/MyIconButton、动画覆盖、图标矢量化、内置字体）未施工。
- PCL2/PCL-CE 方块素材公开分发的版权确认。

## 证据分类

Headless、静态截图、原生交互、动画录像、公共服务烟测、真实 Minecraft E2E 是不同证据层级，不能相互替代。提交 UI 改动时必须在 PR/提交说明中注明证据类型。
