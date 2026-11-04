# WMCL 使用与构建指南

Plain Craft Launcher (PCL) WMCL 是 PCL2 的第三方、非官方跨平台重写，使用 .NET 10 + C# + Avalonia 11。本文件集中说明功能、构建与 GitHub 发布；贡献细节见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 功能说明

### 主界面与窗口

- 顶栏页面：启动、下载、联机、设置、更多
- 浅色/深色主题，跟随系统主题
- 窗口可自由拖拽缩放并记住尺寸；最小 810×470
- UI 缩放 80%~150%，缩放包装层与启动动画隔离
- 窗口透明度与桌面背景模糊（合成器支持时生效）
- 界面字体：默认「内置鸿蒙拉丁 + 系统中文字体」，可在个性化里自选已安装字体
- 图标：PCL-CE 风格层叠方块，主字 PCL、副字 WMCL
- 功能隐藏：设置 → 个性化勾选隐藏导航/子页；按住 F12 临时显示
- Niri 规则生成与应用（仅在用户确认后写入 WMCL 专用规则）

### 启动与账号

- 账号：正版（微软设备码）、离线、第三方（Authlib/Yggdrasil/Nide）
- 微软登录后设备码自动复制并打开固定官方验证页（每会话一次）
- 皮肤与披风：预览、旋转/缩放、模型识别（classic/slim）、像素编辑器、上传与应用
- 版本选择、启动/取消、日志查看、崩溃提示
- 首页新闻卡与独立新闻窗口
- 启动前校验客户端、平台库/native 归档、资源索引/对象与日志配置

### 下载

- 版本目录：原版、Cleanroom、LabyMod、Mod、整合包、数据包、资源包、光影、存档、资源收藏
- 组件安装、资源详情、安装目标选择
- Modrinth / CurseForge 搜索、筛选、分页、详情、版本、依赖、安装
- 资源收藏与批量管理

### 联机

- EasyTier（缺 TUN 权限时使用 pkexec 提权）
- 陶瓦 / Terracotta（备用）
- 创建房间 / 加入房间、状态切换
- 加入 Astral / Astral Game 房间：粘贴 `astral://room?code=…` 或 `AG1.` 离线邀请，复用 EasyTier 组网
- 6 位短码需要 Astral 官方服务，本项目不支持，请改用离线邀请

### 实例管理

- 十个分区：概览、设置、修改、Mod 管理、导出、服务器、资源包、存档、光影、截图
- 克隆、重命名、删除/回收站、Java 选择
- Mod 批量启用/禁用/删除、搜索、图标显示
- 资源包/光影/存档管理与更新检查
- 导出：备份 ZIP 或分享整合包（mrpack/mcbpack），可选择导出文件
- 导入：导入整合包（含 WMCL/PCL 备份标记路由）
- 截图浏览与预览

### 设置

- 启动、Java、游戏管理、联机、日志、更新、个性化、其他
- 帮助页内置 16 个主题，切换时带入场动画
- Java 安装：8/11/17/21/25（Temurin，校验 SHA256 后提交）
- 诊断导出：环境、脱敏设置、启动器日志、实例日志与崩溃报告
- 更新：读取 GitHub Releases；通道（稳定/测试）与方式（不检查 / 仅提示 / 自动下载并提示 / 自动下载并安装）
- 个性化：主题色、缩放、窗口效果、快捷键、背景壁纸均收进折叠组，按需展开

### 命令行

`WMCL.Cli` 提供 `install`、`launch`、`java`、`fabric`、`forgelike`、`curseforge`、`modpack`、`export`、`import`、`content`、`optifine`、`clone`、`rename`、`delete`、`modrinth`、`update`、`login`、`server`、`versions`、`cleanroom`、`labymod` 等命令。

## 环境要求

- .NET SDK 10.0.x（global.json 固定 10.0.402，允许 latestFeature 回滚）
- 运行游戏需要 Java；启动器可扫描系统 Java 或按需下载 Temurin
- Linux 桌面（X11 或 Wayland/XWayland）；Windows/macOS 使用同一套 Avalonia 代码
- 不要开启程序集裁剪

## 内置密钥（可选）

对照 PCL2：微软 Client ID 与 CurseForge API Key 属于发行方内置值，仓库不携带真实值。

- 复制 `secrets.local.props.example` 为 `secrets.local.props` 并填入；
- 该文件已被 `.gitignore` 忽略，构建时由 MSBuild 生成常量注入 `LauncherSecrets`；
- 与 PCL2 一致：密钥不可在设置里填写，只认构建期内置值与同名环境变量（`PCL_MS_CLIENT_ID` / `PCL_CURSEFORGE_API_KEY`）；
- 没有 CurseForge Key 时不阻断功能，客户端只请求镜像（`mod.mcimirror.top`）。

## 构建与运行

```bash
dotnet build WMCL.slnx -c Release
dotnet test  WMCL.slnx -c Release
dotnet run   --project src/WMCL.App -c Release
```

命令行入口：

```bash
dotnet run --project src/WMCL.Cli -c Release -- help
```

## 打包

Windows 单文件（产物重命名为 `WMCL.exe`，程序集名保持 `WMCL.App`）：

```bash
dotnet publish src/WMCL.App/WMCL.App.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false \
  -o dist/win-x64
mv dist/win-x64/WMCL.App.exe dist/win-x64/WMCL.exe
```

Linux 与 Windows 一次性构建（推荐）：

```bash
bash scripts/make-release.sh 0.2.1
```

会依次产出 `WMCL.exe`、Linux tar.gz、deb、Arch `.pkg.tar.zst`，本机有 `rpmbuild` 时还会产出 RPM，最后生成**统一的 `SHA256SUMS`**。单独打包可用：

```bash
bash scripts/package-linux.sh <版本> linux-x64
bash scripts/package-deb.sh <版本> amd64 linux-x64
bash scripts/package-arch.sh <版本> 1 linux-x64
bash scripts/package-rpm.sh <版本> 1 linux-x64   # 需要 rpmbuild
bash scripts/package-appimage.sh <版本> linux-x64
```

所有产物统一校验：`cd artifacts && sha256sum -c SHA256SUMS`。

## 发布流程（本地构建，无 CI）

仓库不使用 GitHub Actions，发布全部在本地完成：

1. 改 `src/WMCL.App/WMCL.App.csproj` 的 `<Version>`（关于页、更新检查都读程序集版本）。
2. 跑全量测试：`dotnet test WMCL.slnx -c Release`。
3. 构建产物：`bash scripts/make-release.sh <版本>`。
4. 提交并打标签：`git commit`、`git tag v<版本>`、`git push origin main v<版本>`。
5. 创建 Release 并上传 `artifacts/` 下的产物与 `SHA256SUMS`：

   ```bash
   gh release create v<版本> --title "…" --notes-file notes.md \
     artifacts/WMCL.exe artifacts/PCL-WMCL-linux-x64-<版本>.tar.gz \
     artifacts/PCL-WMCL-linux-<版本>-amd64.deb \
     artifacts/pcl-wmcl-bin-<版本>-1-x86_64.pkg.tar.zst \
     artifacts/pcl-wmcl-<版本>-1.x86_64.rpm artifacts/SHA256SUMS
   ```

6. 同步更新 `README.md`（版本号、功能概览）与 `GUIDE.md`（新功能说明）——两者与发布内容必须一致。

更新源文件可使用：

```bash
bash scripts/make-latest.sh <版本> <安装包> <下载URL> <说明>
python3 -m http.server 8765 --bind 127.0.0.1 --directory artifacts
```

## 验证

- 全量：2876 通过（App 1604、Core 83、Launch 205、Minecraft 984）
- Release 构建 0 warning / 0 error
- `dotnet format whitespace --verify-no-changes` 与 `git diff --check` 通过

## 许可

- 新增代码：MIT，见 [LICENSE.md](LICENSE.md)
- PCL2 指南与第三方素材：[PCL-LICENCE.md](PCL-LICENCE.md)、[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
- Minecraft 是 Mojang / Microsoft 的付费游戏，请购买正版
