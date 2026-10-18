# PCL Linux（工作名）

以 **原版 PCL2**（`Hex-Dragon/PCL2`，2.14.0.0）为规格的跨平台重写。
目标：.NET 10 LTS + C# 14 + Avalonia，Linux 原生优先，Windows / macOS 同源。

> 非官方项目，与龙腾猫跃/PCL 官方无隶属关系。发布前需按《PCL 分发有限许可》处理命名、署名、开源与正版提示。

## 当前状态

| 里程碑 | 状态 | 说明 |
|---|---|---|
| M0 工具链/脚手架 | ✅ | .NET 10.0.402 SDK（用户级）、解决方案、CI 待补 |
| M1 核心库 + CLI 启动 | ✅ | 安装/校验/Java/参数/启动全链路，本机已启动 1.21.1 |
| M2 Avalonia 壳 + 启动页 | ⬜ | 下一步 |

已验证：

- 安装 `1.21.1`：3,947 个文件，约 872 MB（含 assets/libraries），重复安装幂等。
- 本机启动：Java 21（Zulu）、Wayland/XWayland（niri），窗口标题 `Minecraft 1.21.1`。
- 单元测试：27 个通过（规则判定、Maven 坐标、版本合并、库去重、离线 UUID、启动参数）。

## 环境要求

- .NET SDK 10.0.x（`global.json` 固定 10.0.402）
- Java：启动 1.21.1 需要 Java 21；启动器会自动发现 `JAVA_HOME`、`PATH`、`/usr/lib/jvm`、`~/.jdks`、SDKMAN 等位置
- Linux 桌面（X11 或 Wayland/XWayland）

## 构建与运行

```bash
dotnet build -c Release

# 查看 Java 与已安装版本
dotnet run --project src/PCL.Cli -c Release -- java list
dotnet run --project src/PCL.Cli -c Release -- versions --dir ~/.minecraft

# 安装 + 启动（离线账户）
dotnet run --project src/PCL.Cli -c Release -- install 1.21.1 --dir ~/.minecraft
dotnet run --project src/PCL.Cli -c Release -- launch 1.21.1 --offline Steve --dir ~/.minecraft

# 只打印启动命令
dotnet run --project src/PCL.Cli -c Release -- launch 1.21.1 --offline Steve --dry-run
```

可选参数：`--mirror auto|official|bmclapi`、`--threads 24`、`--memory 4096`、`--java <路径>`、`--width/--height`。

## 目录结构

```text
src/
  PCL.Core/      路径、日志、HTTP、Job、文件工具
  PCL.Download/  并发下载、多源回退、断点、SHA1、进度
  PCL.Minecraft/ 版本清单/JSON/规则/库/资源/安装
  PCL.Launch/    Java 发现、参数构建、离线账户、进程
  PCL.Cli/       命令行入口
tests/           xUnit 测试
```

## 下一步

- M2：Avalonia 主窗口、导航、启动页、日志面板（设计 token 取自原版 `Application.xaml`）。
- 下载器：进度速度在“全部命中缓存”时显示异常，需修正为只统计实际传输。
- 补充：断网/取消/含空格路径的集成测试。
- M4：Java 缺失时自动下载 Temurin。

详细计划见 [PLAN.md](PLAN.md)，实施记录见 [docs/PROGRESS.md](docs/PROGRESS.md)。
