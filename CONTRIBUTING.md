# 贡献指南（面向第三方开发者）

本项目是 PCL2 的第三方、非官方跨平台重写，属于对 PCL2 的重度使用场景。贡献前请先理解并遵守 [PCL-LICENCE.md](PCL-LICENCE.md) 中的重度使用要求，以及本仓库的 MIT + 第三方声明边界。

## 开始之前

- 明确标注本项目是第三方基于 PCL 独立二次创作，不暗示与 PCL 官方存在隶属关系。
- 产品名称必须以 `Plain Craft Launcher (PCL)` 开头并带有第三方后缀；不要使用 `PCL 3`、`PCL Pro` 等易混淆名称。
- 关于页面首位保留龙腾猫跃署名、赞助链接与正版购买入口。
- Windows 版主色相固定为蓝色（`hue=210`、`saturation=85`、`lightAdjust=0`）；不要添加类似赞助解锁的主题功能。
- 启动流程必须保留 Minecraft 正版购买劝导与赞助劝导。
- `PCL2`、`PCL-CE`、`PCL-ME` 仅作为行为/UI 对照，保持只读；不要修改、提交或复制其构建产物、源码、字体、音乐、帮助文档。
- 版本卡/加载器方块图标来自 PCL2/PCL-CE；新增图标/素材前请确认许可与来源。

## 环境

- .NET SDK 10.0.x（global.json 固定 10.0.402）
- Java 21
- Avalonia 11.3.10
- Linux 桌面（X11 或 Wayland/XWayland）；Windows/macOS 使用同一套 Avalonia 代码

代码风格由 [.editorconfig](.editorconfig) 约束：UTF-8、LF、4 空格缩进、去除行尾空格；Markdown 不去除行尾空格。

## 项目结构

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

依赖方向：`App/CLI → Launch/Modding/Minecraft → Download → Core`。`AppServices` 保持手工组合根，不引入大型 DI 容器。

## 构建与测试

```bash
dotnet build WMCL.slnx -c Release
dotnet test  WMCL.slnx -c Release
dotnet run   --project src/WMCL.App -c Release
```

提交前至少跑全量自动测试；涉及 UI 的改动应说明验证类别（Headless/静态截图/原生交互/动画录像/公共服务烟测/真实 Minecraft E2E）。

```bash
dotnet test WMCL.slnx -c Release --no-build --nologo
dotnet test WMCL.slnx -c Release --filter "FullyQualifiedName~..."
```

测试隔离：`tests/TestEnvironment.cs` 为每个测试程序集重定向 XDG 路径；自动保存、账号存储、截图测试不得写入真实用户配置。真实实例的删除、更新、导出、导入不得在测试中执行。

## 提交前检查

```bash
git diff --check
dotnet format whitespace WMCL.slnx --verify-no-changes --no-restore
```

- 不提交 `bin/`、`obj/`、`artifacts/`、`dist/`、日志、测试结果或密钥。
- 不提交真实 Azure Client ID、CurseForge Key、账号 token、Cookie。
- 二进制发布物只作为 GitHub Release asset。
- 不要开启程序集裁剪；程序集名保持 `WMCL.App`。

## 安全与配置边界

- 外部配置只能通过 `SettingsService.ImportFromFile/Json` 导入，统一执行 `AppSettingsImportPolicy`。
- `FileAppSettingsStore` 采用同目录临时文件、刷新与原子替换。
- 导出共用 `Core/IO/ExportSafety` 的逐段链接/reparse 校验；导出路径拒绝符号链接/重解析点。
- `DownloadManager` 提交前核验已知大小、206 范围与实际正文；无 SHA1 不续接旧 `.part`。
- `GameFileVerificationService` 在每次真正启动前校验客户端、平台库/native 归档、资源索引/对象与日志配置。
- 不要让自动保存、账号存储或截图测试写入真实用户配置。

## 许可

- 提交即表示同意以本仓库 MIT 条款发布你的代码贡献；PCL2/PCL-CE 素材仍归原作者。
- 不得提交真实密钥或 PCL2/PCL-CE/PCL-ME 的源码、字体、音乐、帮助文档、官方图标。
- GitHub 仓库整体按 Other 发布：自有代码 MIT + PCL2 指南 + 第三方声明，不是单一 MIT。

## 参考

- [GUIDE.md](GUIDE.md)
- [LICENSE.md](LICENSE.md)
- [PCL-LICENCE.md](PCL-LICENCE.md)
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
