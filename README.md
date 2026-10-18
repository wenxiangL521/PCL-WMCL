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
| M3 下载/校验完善 | ✅ | 校验/修复、缓存命中速度修正、GUI 安装冒烟 |
| M4 Java 自动下载 | ✅ | Mojang Java Runtime（官方/BMCLAPI），缺 Java 时自动安装 |
| M5 微软登录 | 🟡 | 设备码/刷新流程 + UI 已实现；需 Azure Client ID 才能真实登录 |
| M6 Fabric / Modrinth | ✅ | Fabric 安装/启动 + Modrinth 搜索/安装（含 required 依赖） |
| Forge / NeoForge | ✅ | Forge/NeoForge 安装 + GUI；Forge Modrinth Mod 实测 |
| 本地 Mod 管理 | ✅ | 实例 mods 列表、启用/禁用（.disabled/.old）、删除 |
| M7 打包 / 自动更新 | 🟡 | tar.gz 打包 + latest.json 检查/下载；自动替换待做 |

![PCL Linux M2 启动页](docs/screenshots/m2-launch.png)

![Fabric 安装页](docs/screenshots/m6-mods.png)

![Forge/NeoForge 安装页](docs/screenshots/m9-forge.png)

![本地 Mod 管理](docs/screenshots/m10-localmods.png)

已验证：

- 安装 `1.21.1`：3,947 个文件，约 872 MB（含 assets/libraries），重复安装幂等。
- CLI 启动：Java 21（Zulu）、Wayland/XWayland（niri），窗口标题 `Minecraft 1.21.1`。
- GUI 启动：版本列表（120+）、已安装标记、实时日志、`--auto-launch 1.21.1` 成功拉起游戏。
- GUI 安装：`--auto-install 1.21.1` 显示“安装完成：3,947 个文件，872.7 MB”。
- Java 自动下载：Mojang Runtime 21（136 个文件 / 105 MB），`bin/java -version` 为 21.0.7。
- Fabric：1.21.1 + Loader 0.19.5（67 个文件），日志 `Loading Minecraft 1.21.1 with Fabric Loader 0.19.5`，窗口 `Minecraft* 1.21.1`。
- Modrinth：搜索 Sodium（47 条结果）→ 安装 `sodium-fabric-0.8.13+mc1.21.1.jar` → Fabric 启动日志加载 Sodium。
- 实例隔离：Fabric 的 `--gameDir` 指向 `versions/<id>`，mods 落在实例内；原版仍使用根目录。
- 打包：`PCL-linux-x64-0.1.0.tar.gz`（39 MB；解压后 92 MB 自包含二进制可直接运行）。
- 更新：本地 `latest.json`（v0.2.0）→ 检查、下载、SHA256 校验通过。
- 微软登录：设备码/Refresh 流程通过 mock 测试；GUI 登录对话框可打开；未做真实账号 E2E（缺 Azure Client ID）。
- Forge：1.20.1-47.3.0 headless 安装成功，启动窗口 `Minecraft Forge* 1.20.1`；JEI + mezz_config 从实例 mods 加载。
- NeoForge：1.21.1-21.1.80 成功安装/启动，窗口 `Minecraft NeoForge* 1.21.1`；官方 Maven 不可达时自动用 BMCLAPI/Central 预置库并 `--offline` 安装。
- GUI：1.21.1 下 69 个 Forge 版本可选。
- 本地 Mod：Fabric 实例显示 sodium 为“已启用”，支持禁用/删除/打开目录。
- 自动更新：打包版 `PCL` 从本地 latest.json 下载 0.2.0 → 解包 → 退出替换 → 新 PID 重启；目标二进制 SHA256 与更新包一致。
- 单元测试：45 个通过（规则判定、Maven 坐标、版本合并、库去重、离线 UUID、启动参数、更新清单/版本比较、隔离判定、微软认证流程、Forge/Neo 版本解析、本地 Mod 切换、更新包解压）。

## 环境要求

- .NET SDK 10.0.x（`global.json` 固定 10.0.402）
- Java：启动 1.21.1 需要 Java 21；启动器会自动发现 `JAVA_HOME`、`PATH`、`/usr/lib/jvm`、`~/.jdks`、SDKMAN 等位置，缺失时自动下载 Mojang Java Runtime
- Linux 桌面（X11 或 Wayland/XWayland）

## 构建与运行

```bash
dotnet build -c Release

# GUI
dotnet run --project src/PCL.App -c Release

# GUI 开发参数：指定游戏目录 + 自动启动某个版本
dotnet run --project src/PCL.App -c Release -- \
  --game-dir /home/wenxiangl/pcl-test-mc --auto-launch 1.21.1

# CLI：微软登录（需 Azure 公共 Client ID）
dotnet run --project src/PCL.Cli -c Release -- login microsoft --client-id <ID>
dotnet run --project src/PCL.Cli -c Release -- login status
dotnet run --project src/PCL.Cli -c Release -- launch 1.21.1 --microsoft

# CLI：查看 Java 与已安装版本
dotnet run --project src/PCL.Cli -c Release -- java list
dotnet run --project src/PCL.Cli -c Release -- versions --dir ~/.minecraft

# CLI：自动安装 Java 21（Mojang Runtime）
dotnet run --project src/PCL.Cli -c Release -- java install 21 --force

# CLI：Forge / NeoForge
dotnet run --project src/PCL.Cli -c Release -- forgelike versions 1.20.1 --loader forge
dotnet run --project src/PCL.Cli -c Release -- forgelike install 1.20.1 --loader forge --version 47.3.0 \
  --java /usr/lib/jvm/zulu-17/bin/java

# CLI：Fabric Loader
dotnet run --project src/PCL.Cli -c Release -- fabric loaders 1.21.1
dotnet run --project src/PCL.Cli -c Release -- fabric install 1.21.1 --dir ~/.minecraft

# CLI：Modrinth 搜索 / 安装（含 required 依赖）
dotnet run --project src/PCL.Cli -c Release -- modrinth search sodium --game 1.21.1
dotnet run --project src/PCL.Cli -c Release -- modrinth install sodium --game 1.21.1 --dir ~/.minecraft

# CLI：安装 + 校验/修复 + 启动（离线账户）
dotnet run --project src/PCL.Cli -c Release -- install 1.21.1 --dir ~/.minecraft
dotnet run --project src/PCL.Cli -c Release -- install 1.21.1 --dir ~/.minecraft --verify
dotnet run --project src/PCL.Cli -c Release -- launch 1.21.1 --offline Steve --dir ~/.minecraft

# 只打印启动命令
dotnet run --project src/PCL.Cli -c Release -- launch 1.21.1 --offline Steve --dry-run
```

CLI 可选参数：`--mirror auto|official|bmclapi`、`--threads 24`、`--memory 4096`、`--java <路径>`、`--width/--height`、`--verify`（安装时校验已有文件）。

# 打包（自包含 tar.gz + sha256）
bash scripts/package-linux.sh 0.1.0
# → artifacts/PCL-linux-x64-0.1.0.tar.gz

# 更新检查 / 下载（CLI）
dotnet run --project src/PCL.Cli -c Release -- update check https://example.com/latest.json
dotnet run --project src/PCL.Cli -c Release -- update download https://example.com/latest.json
```

`latest.json` 格式：

```json
{
  "version": "0.2.0",
  "url": "https://example.com/PCL-linux-x64-0.2.0.tar.gz",
  "sha256": "…",
  "notes": "更新说明"
}
```

GUI 在“设置 → 更新源”填写该地址，然后在“关于”页检查/下载。

## 目录结构

```text
src/
  PCL.Core/      路径、日志、HTTP、Job、文件工具
  PCL.Download/  并发下载、多源回退、断点、SHA1、进度
  PCL.Minecraft/ 版本清单/JSON/规则/库/资源/安装
  PCL.Launch/    Java 发现/自动下载、参数构建、离线账户、进程
  PCL.Modding/   Fabric 安装、Modrinth 搜索/下载/依赖
  PCL.UI/        Avalonia 主题与样式（PCL 蓝色系）
  PCL.App/       Avalonia 壳、页面、ViewModel、服务组合根
  PCL.Cli/       命令行入口
tests/           xUnit 测试
```

## 下一步

- **M5 剩余**：用真实 Azure Client ID 做端到端登录测试。
- **M7 剩余**：AppImage/deb、发布清单托管。
- **下一步增强**：按实例隔离 mods、CurseForge、Mod 启用/禁用。
- **M3 剩余**：安装队列/失败重试提示、镜像切换的 UI 反馈。

已知待修：

- 微软登录尚未用真实 Azure Client ID 做端到端验证；需在设置页填写 Client ID。
- 本地 Mod 删除未做二次确认（PCL 有确认框）。
- Forge/NeoForge 安装依赖官方安装器的外网下载；47.4.28 在本机因网络失败，47.3.0 成功。
- NeoForge 安装依赖 BMCLAPI 预置库；若所有镜像都不可用仍会失败。
- 自动替换仅支持打包后的单文件 `PCL`（Linux）；开发态 `PCL.App`、Windows/macOS 暂不支持。
- 更新源默认留空。
- 只产出 tar.gz，尚未构建 AppImage/deb。
- 仅对可装 Mod 的版本自动隔离；原版实例仍共享根目录 mods。
- Modrinth CDN 在当前网络下下载较慢（1.5MB 约 1 分钟），后续可优化请求策略。
- Java 自动下载尚未在“系统完全没有 Java”的机器上做端到端验证（当前通过 `java install --force` 验证下载链路）。
- 日志面板行数未做上限（当前按 200k 字符截断，滚动性能待观察）。
- 尚无自动化 UI 测试（Avalonia.Headless）。
- 窗口图标/自定义标题栏、暗色主题尚未做。

详细计划见 [PLAN.md](PLAN.md)，实施记录见 [docs/PROGRESS.md](docs/PROGRESS.md)。
