# 开发、测试与打包

## 环境

- .NET SDK 10.0.x（`global.json` 固定 10.0.402）
- Java 21（启动 1.21.1；启动器也会扫描/下载 Java）
- Linux 桌面（X11 或 Wayland/XWayland）；Windows/macOS 使用同一 Avalonia 代码

## 目录

```text
src/WMCL.Core/      路径、日志、HTTP、Job、文件工具
src/WMCL.Download/  并发下载、多源回退、断点、SHA1、进度
src/WMCL.Minecraft/ 版本 JSON、规则、库、资源、安装、加载器识别
src/WMCL.Launch/    Java、参数构建、账户、启动计划、微软/Authlib 认证
src/WMCL.Modding/   Fabric/Forge/NeoForge、Modrinth、CurseForge、整合包
src/WMCL.UI/        Avalonia 主题、颜色、样式
src/WMCL.App/       Avalonia 壳、页面、ViewModel、服务组合根
src/WMCL.Cli/       命令行入口
tests/              xUnit + Avalonia.Headless
```

## 构建与测试

```bash
dotnet build WMCL.slnx -c Release
dotnet test  WMCL.slnx -c Release
dotnet run   --project src/WMCL.App -c Release
```

- App 测试会启动 Avalonia Headless，部分用例要求串行执行。
- 目标测试可用 `--filter FullyQualifiedName~...`。
- 格式检查：

```bash
dotnet format whitespace WMCL.slnx --verify-no-changes --no-restore
git diff --check
```

## 打包

### Windows 单文件

```bash
dotnet publish src/WMCL.App/WMCL.App.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false \
  -o dist/win-x64
```

产物为 `WMCL.exe`。保留 `WinExe`，否则 Windows 会弹控制台。不要开启裁剪，避免 Avalonia/XAML/反射被裁坏。

### Linux

`scripts/package-linux.sh`、`package-appimage.sh`、`package-deb.sh` 生成 tar.gz/AppImage/deb；`packaging/wmcl-linux.desktop` 与 `wmcl-linux.svg` 是桌面入口。

## 验证分类

不同证据不能互相替代：

1. **Headless**：ViewModel、绑定、动画中间状态、取消/失败回归。
2. **静态截图**：只证明某一帧布局。
3. **原生交互**：真实窗口、鼠标/键盘、焦点、DPI。
4. **动画录像**：逐帧/方向/快速反转。
5. **公共服务烟测**：Mojang/Modrinth/CurseForge 等真实接口。
6. **真实 Minecraft E2E**：实际启动、进入世界、声音/输入/长时间运行。

提交前至少跑全量自动测试；涉及 UI 的改动应说明证据属于哪一类。当前摘要见 [VERIFICATION.md](VERIFICATION.md)。

## 文档政策

- 不为每次修复新增独立 `*_VERIFICATION.md`；更新 `VERIFICATION.md` 的摘要。
- 历史验证证据放在仓库外，不提交 TRX、日志、临时截图和安装目录。
- 新增文档保持聚焦：开发、配置、验证、UI 对照、发布各一篇即可。

## 参考仓库

`PCL2`、`PCL-CE`、`PCL-ME` 仅作为行为/UI 对照，保持只读，不复制其构建产物或密钥。
