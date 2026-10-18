# 联机与版本选择逻辑审计

这次按 PCL2 源码逐项核对，不是只看界面。

## 已修

| # | 问题 | 原行为 | 现在的行为 |
|---|---|---|---|
| 1 | 版本选择页右侧是“版本详情 + 启动游戏” | 右侧详情没有实际用途；按钮只设置 `Launch.SelectedVersion` 并关闭，**不会启动游戏** | 右侧改成 PCL2 式已安装版本列表；单击列表项即选择并返回启动页，符合 PCL2 `PageSelectRight.Item_Click` 的 `McInstanceSelected + PageBack` |
| 2 | 异步首次选中版本触发返回/改选 | 列表加载完成后自动选中第一项，页面可能自动关闭或改启动版本 | 选中不再写回 Launch；仅 `PointerReleased` 在真实 `ListBoxItem` 上触发确认，首次异步选中不会返回或改版本 |
| 3 | EasyTier 和陶瓦可同时启动 | 两个 `CanStart` 只看自己的状态 | 互斥：一个在运行/启动时，另一个按钮禁用；启动一个会停止另一个 |
| 4 | 切换联机方式不停旧后端 | EasyTier 运行中切到陶瓦，EasyTier 仍在跑 | 切换时取消/停止旧后端 |
| 5 | EasyTier 卡片出现陶瓦路径 | EasyTier 卡片复制了“本地陶瓦可执行文件”输入，实际启动不会使用 | 已从 EasyTier 卡片删除 |
| 6 | 陶瓦可执行文件路径不持久化 | 选好后重启丢失 | 新增 `AppSettings.TerracottaExecutablePath`，启动/保存设置时写入 |
| 7 | 加载版本列表时弹出任务按钮 | `Launch.IsBusy` 直接当任务状态 | 改为 `Launch.IsBusy && Launch.Progress > 0`，普通版本目录加载不再显示任务球 |

## 仍存在的边界

- PCL2 官方房间服务依赖闭源配置，本项目无法使用；联机页仍是 **EasyTier 手动网络 / 陶瓦 Terracotta** 替代方案，不是 PCL2 的官方房间列表和玩家列表。
- 联机页不会自动启动 Minecraft 或自动加入服务器；房主仍需在游戏内“对局域网开放”，客人手动连接显示的地址。
- 版本选择页右侧已改为按加载器分组卡片（隐藏的版本/收藏夹/愚人节版本/错误的版本/常规/Fabric/Forge/NeoForge/Quilt/OptiFine/其他），标题带数量；收藏项不再在普通组重复出现，4 月 1 日版本进入愚人节组；隐藏版本可在左侧“显示隐藏版本”开关控制。
- 左侧已支持多文件夹列表、添加已有文件夹、新建 `.minecraft` 文件夹、导入整合包、移除文件夹、切换目录和文件夹齿轮菜单。
- `AutoFallbackToTerracotta` 默认开启，EasyTier 失败会自动切到陶瓦；不需要时可在界面关掉。
- 陶瓦/TUN/EasyTier 仍依赖本机权限、二进制下载和网络环境。

## 未完全解释的一次异常

- 一次版本选择页打开时，`Launch.LoadAsync` 曾返回
  `加载版本列表失败：Index was out of range`。
- 之后用同一构建重新启动并等待加载，版本列表正常显示，未能复现。
- 暂未定位到具体栈；如果再次出现，需要抓取完整异常栈。不能把这次异常当作已修复。

## 验证

- `dotnet build WMCL.slnx -c Release --nologo -v q`：0 warning / 0 error
- `dotnet test WMCL.slnx -c Release --nologo -v q`：142 passed
- 版本选择页实测：隔离配置下显示多个文件夹；把 `1.21.1` 写入 `FavoriteVersions` 后，它出现在“收藏夹”且不再出现在常规版本。异步加载不会自动关闭。普通配置下左侧显示新建/添加/导入/移除入口。
- 联机页实测：EasyTier 卡片不再出现陶瓦路径输入。
