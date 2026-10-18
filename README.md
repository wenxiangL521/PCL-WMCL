# PCL Linux（工作名）

以 **原版 PCL2**（`Hex-Dragon/PCL2`，2.14.0.0）为规格的跨平台重写。
目标：.NET 10 LTS + C# 14 + Avalonia，Linux 原生优先，Windows / macOS 同源。

> 非官方项目，与龙腾猫跃/PCL 官方无隶属关系。发布前需按《PCL 分发有限许可》处理命名、署名、开源与正版提示。

## 当前状态

| 里程碑 | 状态 | 说明 |
|---|---|---|
| M0 工具链/脚手架 | ✅ | .NET 10.0.402 SDK（用户级）、解决方案、CI 待补 |
| M1 核心库 + CLI 启动 | ✅ | 安装/校验/Java/参数/启动全链路，本机已启动 1.21.1 |
| M2 Avalonia 壳 | ✅ | 主窗口、导航、启动/下载/实例/设置/关于，GUI 已拉起 1.21.1 |
| M3 下载 UI 完善 | ⬜ | 基础安装已可用；队列/修复/镜像细节待做 |
| M4 Java 自动下载 | ⬜ | 当前只发现，不自动安装 |
| M5 微软登录 | ⬜ | 当前仅离线账户 |
| M6 Fabric / Modrinth | ⬜ | 下一阶段 |
| M7 打包 / 自动更新 | ⬜ | — |

已验证：

- 安装 `1.21.1`：3,947 个文件，约 872 MB（含 assets/libraries），重复安装幂等。
- CLI 启动：Java 21（Zulu）、Wayland/XWayland（niri），窗口标题 `Minecraft 1.21.1`。
- GUI 启动：版本列表（120+）、已安装标记、实时日志、`--auto-launch 1.21.1` 成功拉起游戏。
- 单元测试：27 个通过（规则判定、Maven 坐标、版本合并、库去重、离线 UUID、启动参数）。

## 环境要求

- .NET SDK 10.0.x（`global.json` 固定 10.0.402）
- Java：启动 1.21.1 需要 Java 21；启动器会自动发现 `JAVA_HOME`、`PATH`、`/usr/lib/jvm`、`~/.jdks`、SDKMAN 等位置
- Linux 桌面（X11 或 Wayland/XWayland）

## 构建与运行

```bash
dotnet build -c Release

# GUI
dotnet run --project src/PCL.App -c Release

# GUI 开发参数：指定游戏目录 + 自动启动某个版本
dotnet run --project src/PCL.App -c Release -- \
  --game-dir /home/wenxiangl/pcl-test-mc --auto-launch 1.21.1

# CLI：查看 Java 与已安装版本
dotnet run --project src/PCL.Cli -c Release -- java list
dotnet run --project src/PCL.Cli -c Release -- versions --dir ~/.minecraft

# CLI：安装 + 启动（离线账户）
dotnet run --project src/PCL.Cli -c Release -- install 1.21.1 --dir ~/.minecraft
dotnet run --project src/PCL.Cli -c Release -- launch 1.21.1 --offline Steve --dir ~/.minecraft

# 只打印启动命令
dotnet run --project src/PCL.Cli -c Release -- launch 1.21.1 --offline Steve --dry-run
```

CLI 可选参数：`--mirror auto|official|bmclapi`、`--threads 24`、`--memory 4096`、`--java <路径>`、`--width/--height`。

## 目录结构

```text
src/
  PCL.Core/      路径、日志、HTTP、Job、文件工具
  PCL.Download/  并发下载、多源回退、断点、SHA1、进度
  PCL.Minecraft/ 版本清单/JSON/规则/库/资源/安装
  PCL.Launch/    Java 发现、参数构建、离线账户、进程
  PCL.UI/        Avalonia 主题与样式（PCL 蓝色系）
  PCL.App/       Avalonia 壳、页面、ViewModel、服务组合根
  PCL.Cli/       命令行入口
tests/           xUnit 测试
```

## 下一步

- **M3**：下载页完善（安装队列、校验修复、镜像切换、失败重试提示）。
- **M4**：Java 缺失时自动下载 Temurin，并校验版本/架构。
- **M5**：微软设备码登录（需要 Azure 公共 client ID）。
- **M6**：Fabric/Quilt 安装 + Modrinth 搜索/安装。
- **M7**：AppImage/tar.gz 打包 + 自动更新。

已知待修：

- 下载器在“全部命中缓存”时速度显示异常（只统计实际传输字节）。
- 日志面板行数未做上限（当前按 200k 字符截断，滚动性能待观察）。
- 尚无自动化 UI 测试；GUI 安装按钮尚未做端到端冒烟。
- 窗口图标/自定义标题栏、暗色主题尚未做。

详细计划见 [PLAN.md](PLAN.md)，实施记录见 [docs/PROGRESS.md](docs/PROGRESS.md)。
