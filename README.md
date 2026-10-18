# PCL Linux（工作名）

以 **原版 PCL2**（`Hex-Dragon/PCL2`，2.14.0.0）为规格的跨平台重写。
目标：.NET 10 LTS + C# 14 + Avalonia，Linux 原生优先，Windows / macOS 同源。

> 非官方项目，与龙腾猫跃/PCL 官方无隶属关系。发布前需按《PCL 分发有限许可》处理命名、署名、开源与正版提示。

## 当前状态

最新审计：[第二次大审计](docs/AUDIT_2026-10-19.md)。

| 里程碑 | 状态 | 说明 |
|---|---|---|
| M0 工具链/脚手架 | ✅ | .NET 10.0.402 SDK（用户级）、解决方案、GitHub Actions CI |
| M1 核心库 + CLI 启动 | ✅ | 安装/校验/Java/参数/启动全链路，本机已启动 1.21.1 |
| M2 Avalonia 壳 | ✅ | 主窗口、导航、启动/下载/实例/设置/关于，GUI 已拉起 1.21.1 |
| M3 下载/校验完善 | ✅ | 校验/修复、缓存命中速度修正、GUI 安装冒烟 |
| M4 Java 自动下载 | ✅ | Mojang Java Runtime（官方/BMCLAPI），缺 Java 时自动安装 |
| M5 微软登录 | 🟡 | 设备码/刷新流程 + UI 已实现；需 Azure Client ID 才能真实登录 |
| M6 Fabric / Modrinth | ✅ | Fabric 安装/启动 + Modrinth 搜索/安装（含 required 依赖） |
| Forge / NeoForge | ✅ | Forge/NeoForge 安装 + GUI；Forge Modrinth Mod 实测 |
| 本地 Mod 管理 | ✅ | 实例 mods 列表、启用/禁用、删除确认、更新检查/全部更新 |
| 资源/光影/存档 | ✅ | 列表、导入、删除确认、资源包启用/禁用、存档备份、Modrinth 下载 |
| CurseForge | 🟡 | 搜索/文件/依赖安装 + GUI/CLI；需有效 API Key 做 E2E |
| Modrinth 整合包 | ✅ | Fabric/Forge/NeoForge `.mrpack` 安装 + 实例导出 `.mrpack`（overrides） |
| 实例导出/导入 | ✅ | 隔离实例 zip 备份/恢复；导入改写 id 并补全资源 |
| 实例管理 | ✅ | 克隆、重命名（目录/JSON/jar）、删除确认 |
| OptiFine | ✅ | 版本列表、官方签名下载、headless 安装、GUI/CLI |
| Java 管理 | ✅ | 扫描/下拉选择/自动；自定义路径可保存 |
| 崩溃分析 | ✅ | 启动输出/latest.log → Java/OOM/Mod/GLFW/权限规则 + 建议 |
| 主题 | ✅ | 浅色/深色/跟随系统 + PCL2 风格命名预设 + HSL 自定义（顶栏/按钮/滑杆/页面联动） |
| Logo/音乐 | ✅ | 自定义 Logo；背景音乐文件夹、随机播放、音量、启动播放、游戏运行时暂停/恢复（ffplay） |
| 自定义主页 | ✅ | 本地 `.axaml` 运行时加载，启动页“主页/启动日志”标签 |
| 实例级设置 | ✅ | 内存/Java/额外参数/窗口大小/自动加入服务器 |
| 全局启动参数 | ✅ | 全局 JVM/游戏参数与实例参数叠加 |
| HTTP 代理 | ✅ | 启动时注入 HTTP(S)_PROXY/ALL_PROXY，修改后重启 |
| 个性化背景 | ✅ | 单图或背景文件夹随机图，透明度/模糊/主题色叠加，卡片半透明 |
| 动画 | ✅ | 页面整体淡入/上浮 + 卡片错峰 + 侧栏宽度 + 按钮缩放 + 控件颜色过渡 |
| 组合安装 | ✅ | 合法组合；展平 inheritsFrom 合并实例 + 客户端 jar + `PCL/install.json` 清单 |
| 全局任务球 | ✅ | 右下角百分比圆球，任一安装/下载进行时显示 |
| 任务管理 | ✅ | 任务窗口：总进度、任务列表/组合安装阶段树、速度/文件/线程、取消 |
| 联机 | 🟡 | EasyTier 手动中继；失败可自动/手动切换陶瓦（HMCL Terracotta）；需 TUN/可访问中继 |
| M7 打包 / 自动更新 | ✅ | tar.gz/deb/AppImage + latest.json + 自动替换/重启 |

![PCL2 风格主界面](docs/screenshots/m32-pcl-launch-final.png)

![PCL2 风格主界面（早期）](docs/screenshots/m25-pcl-shell.png)

![PCL2 风格设置页](docs/screenshots/m34-pcl-settings-4.png)

![PCL2 风格更多页](docs/screenshots/m33-pcl-more.png)

![PCL2 风格帮助页](docs/screenshots/m35-pcl-help.png)

![PCL2 风格数据包页](docs/screenshots/m36-pcl-datapack.png)

![PCL2 风格安装页](docs/screenshots/m37-pcl-install.png)

![PCL2 风格个性化页](docs/screenshots/m38-pcl-personalization.png)

![PCL Linux M2 启动页](docs/screenshots/m2-launch.png)

![Fabric 安装页](docs/screenshots/m6-mods.png)

![Forge/NeoForge 安装页](docs/screenshots/m9-forge.png)

![本地 Mod 管理](docs/screenshots/m10-localmods.png)

![资源/光影/存档](docs/screenshots/m11-content.png)

![崩溃分析](docs/screenshots/m12-crash.png)

![深色主题](docs/screenshots/m13-dark.png)

![删除确认](docs/screenshots/m14-confirm.png)

![实例级设置](docs/screenshots/m15-instance.png)

![资源包/光影下载](docs/screenshots/m16-content-download.png)

![Mod 更新检查](docs/screenshots/m17-mod-updates.png)

![Modrinth 整合包](docs/screenshots/m18-modpack.png)

![CurseForge GUI](docs/screenshots/m19-curseforge.png)

![实例管理](docs/screenshots/m20-instance-manage.png)

![Java 管理](docs/screenshots/m21-java.png)

![资源导入](docs/screenshots/m22-content-import.png)

![资源包启用/禁用](docs/screenshots/m23-resource-toggle.png)

![OptiFine](docs/screenshots/m24-optifine.png)

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
- AppImage：appimagetool 构建 37MB `PCL-linux-x64-0.1.0.AppImage`；`APPIMAGE_EXTRACT_AND_RUN=1` 启动窗口 `PCL Linux`。
- deb：`PCL-linux-0.1.0-amd64.deb`（40 MB），`ar t` 三个成员、control 字段正确；解包后的 `/opt/pcl/PCL` 可直接启动。
- 资源页：Fabric 实例显示 1 个资源包、1 个光影、1 个存档；支持删除与存档 zip 备份。
- 崩溃分析：用 `mainClass` 不存在的假版本触发 `ClassNotFoundException`，启动页显示“主类/依赖缺失”与修复建议。
- 主题：`--theme dark` 启动，设置页、侧栏、输入框、日志区全部切换为深色。
- 删除确认：Mod/资源包/光影/存档删除前弹出确认窗（标题、说明、取消/确定删除）。
- 实例设置：Fabric 实例 2048 MB + `-Dpcl.instance=1` + `--fullscreen`，dry-run 输出 `-Xmx2048M`、两个额外参数；实例页可编辑并保存。
- 窗口/服务器：实例设置 1024×600 + `localhost:25565`，dry-run 输出 `--width 1024 --height 600 --server localhost --port 25565`。
- 全局参数：设置 `ExtraJvmArgs=-Dpcl.global=1`、`ExtraGameArgs=--demo` 后 GUI 启动，Java 命令行包含两者。
- HTTP 代理：`http://127.0.0.1:8899` 本地 CONNECT 代理记录到应用对 `bmclapi2.bangbang93.com:443` 的请求。
- 资源下载：Modrinth `project_type:resourcepack/shader` 搜索（faithful → 99 个结果），Faithful 32x 与 BSL Shaders 安装进实例目录。
- CurseForge：核心搜索/文件/依赖安装 + GUI 来源切换/CLI/设置；缺 Key 时界面明确提示；真实 API 需有效 Key。
- Modrinth 整合包：本地 `.mrpack` 安装为 `PCLTestPack`，overrides 写入实例，启动 `Minecraft* 1.21.1` + Fabric 0.19.5。
- 实例导出：`pcl export PCLTestPack` → 4 文件/25.6MB zip，含版本 JSON/jar、config、options 与 `pcl-export.json`。
- 实例导入：`pcl import ... --name ImportedPack` → 改写 id、补全 3888 个资源文件，启动 `Minecraft* 1.21.1` + Fabric 0.19.5。
- Mod 全部更新：`pcl modrinth updates --instance ... --apply` → 旧 JEI 15.0.0.12 被替换，只保留 15.71.0.224。
- 实例管理：`pcl clone PCLTestPack --name PCLTestPackCopy` → JSON id/jar/config 复制，启动 `Minecraft* 1.21.1`；`pcl rename ...` 同步 id/jar；`pcl delete ... --yes` 删除。
- 整合包导出：`pcl modpack export PCLTestPack` → `.mrpack`（Fabric 0.19.5 + overrides），再导入为 `MRPackRoundTrip` 并启动。
- Java 管理：设置页扫描到 Java 25/21/21/17/11/8，可下拉选择或“自动”。
- 资源导入：`content import world CliWorld.zip`、`resourcepack TestImportPack.zip` 均进入实例目录。
- 资源包启用：读写 `options.txt` 的 `resourcePacks`，`file/<名字>` 增删；无 options 时默认禁用。
- OptiFine：1.20.1 HD_U_I6 安装成功，启动窗口 `Minecraft* 1.20.1`，日志出现 `[OptiFine]`。
- OptiFine 隔离：launchwrapper/optifine 判定为 modable，避免出现在原版版本下拉中。
- Forge 整合包：`ForgePackTest`（1.20.1-47.3.0）复制为自定义实例，启动 `Minecraft Forge* 1.20.1`。
- NeoForge 整合包：`NeoPackTest`（1.21.1-21.1.80）启动 `Minecraft NeoForge* 1.21.1`。
- 整合包 GUI：1.21.1 搜索 18 个结果，Fabulously Optimized 可选；支持实例名、本地 `.mrpack` 路径。
- Mod 更新：SHA1 反查发现旧 JEI `15.0.0.12 → 15.71.0.224`；执行更新后旧 jar 被移除。
- 单元测试：132 个通过（规则判定、Maven 坐标、版本合并、库去重、离线 UUID、启动参数、更新清单/版本比较、隔离判定、微软认证流程、Forge/Neo 版本解析、本地 Mod 切换、更新包解压、资源/存档管理/导入、崩溃规则、实例设置/参数拆分、CurseForge 解析、整合包索引、实例导出/导入/重命名）。

## 环境要求

- .NET SDK 10.0.x（`global.json` 固定 10.0.402）
- Java：启动 1.21.1 需要 Java 21；启动器会自动发现 `JAVA_HOME`、`PATH`、`/usr/lib/jvm`、`~/.jdks`、SDKMAN 等位置，缺失时自动下载 Mojang Java Runtime
- Linux 桌面（X11 或 Wayland/XWayland）

## 构建与运行

需要 API Key、微软 Client ID、EasyTier 权限或陶瓦联机的配置步骤，见 **[人工配置步骤](docs/MANUAL_SETUP.md)**。

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

# CLI：导出/导入实例
pcl export PCLTestPack --output /tmp/PCLTestPack.zip
pcl import /tmp/PCLTestPack.zip --name ImportedPack

# CLI：Modrinth 整合包（Fabric/Forge/Neo）
pcl modpack install /path/to/pack.mrpack --name MyPack
pcl modpack install-modrinth <projectId> --game 1.21.1
pcl modpack export <实例> --output /tmp/pack.mrpack

# CLI：OptiFine
pcl optifine versions 1.20.1
pcl optifine install 1.20.1 --patch I6

# CLI：Mod 更新全部
pcl modrinth updates --instance 1.20.1-forge-47.3.0 --apply

# CLI：CurseForge（需有效 API Key）
PCL_CURSEFORGE_API_KEY=<KEY> pcl curseforge search jei --game 1.20.1 --loader forge
PCL_CURSEFORGE_API_KEY=<KEY> pcl curseforge install 238222 --game 1.20.1 --loader forge

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

## 验证

- 自动化测试与端到端记录见 [docs/VERIFICATION.md](docs/VERIFICATION.md)。
- 当前 69 个测试通过。
- 外部限制：真实微软登录需 Azure Client ID；CurseForge 需有效 API Key；AppImage 直接运行需 FUSE。

## 下一步

- **M5 剩余**：用真实 Azure Client ID 做端到端登录测试。
- **CurseForge 剩余**：用有效 API Key 做真实 API E2E。
- **跨平台**：Windows/macOS 实机验证。
- **后续增强**：多语言、皮肤/披风、服务器列表、安装队列/重试、UI 自动化测试。

已知限制：

- 微软登录/CurseForge 的真实 E2E 需要外部凭据；代码路径与 fake HTTP 测试已通过。
- Forge/NeoForge 安装依赖官方安装器/BMCLAPI 的外网下载；部分版本可能因网络失败。
- NeoForge 依赖 BMCLAPI 预置库；所有镜像不可用时仍会失败。
- 自动替换仅支持打包后的单文件 `PCL`（Linux）；开发态 `PCL.App`、Windows/macOS 暂不支持。
- AppImage 直接运行需要 FUSE；无 FUSE 时用 `APPIMAGE_EXTRACT_AND_RUN=1`。
- 更新源默认留空，需自行生成/托管 `latest.json`。
- 原版实例使用共享根目录；可装 Mod/加载器实例自动隔离。
- Modrinth CDN 在当前网络下下载较慢。
- Java 自动下载尚未在“系统完全没有 Java”的机器上做 E2E。
- 尚无自动化 UI 测试（Avalonia.Headless）。
- 多语言、披风显示、服务器图标预览尚未实现；服务器列表编辑器已支持。
- 多语言尚未做；窗口任务栏图标已加入 `Assets/pcl-icon.png`，自定义标题栏/暗色主题已实现。

详细计划见 [PLAN.md](PLAN.md)，实施记录见 [docs/PROGRESS.md](docs/PROGRESS.md)。
