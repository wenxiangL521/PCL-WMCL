# 验证记录

环境：Arch Linux x86_64、.NET SDK 10.0.402、Avalonia 11.3、Java 8/11/17/21/25。
测试目录：`/home/wenxiangl/wmcl-test-mc`。

## 自动化

```bash
dotnet test -c Release --nologo
dotnet publish src/WMCL.App/WMCL.App.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o /tmp/wmcl-publish
```

当前：69 个测试通过（Core 9 / Minecraft 36 / Launch 24）。

## 端到端

| 功能 | 结果 |
|---|---|
| 原版安装/启动 | 1.21.1、1.20.1 启动成功 |
| Fabric | 1.21.1 + Loader 0.19.5，Sodium 加载 |
| Forge | 1.20.1 + 47.3.0，JEI/mezz_config 加载 |
| NeoForge | 1.21.1 + 21.1.80，BMCLAPI 预置 + `--offline` |
| OptiFine | 1.20.1 HD_U_I6，官方安装器 headless，日志 `[OptiFine]` |
| 实例隔离 | Fabric `--gameDir versions/<id>`；原版根目录 |
| Modrinth | 搜索/安装/依赖、资源包/光影、更新检查/全部更新 |
| 整合包 | Fabric/Forge/NeoForge `.mrpack` 安装、导出、GUI 搜索 |
| 资源/存档 | 导入、删除确认、资源包启用、世界 zip 备份 |
| 实例管理 | 导出/导入、克隆、重命名、删除 |
| Java | 发现 25/21/21/17/11/8，可选择/自动 |
| 崩溃分析 | 假 mainClass → “主类/依赖缺失” |
| 主题 | 浅色/深色/跟随系统 |
| 打包 | tar.gz、deb、AppImage（无 FUSE 时 extract-and-run） |
| 更新 | latest.json → 下载 → 退出替换 → 新 PID 重启 |
| 微软登录 | 设备码/XBL/XSTS/MC/授权/档案 + refresh，fake HTTP 测试通过 |
| CurseForge | 搜索/文件/依赖 + fake HTTP 测试；缺 Key 提示 |
| HTTP 代理 | 本地 CONNECT 代理记录到应用请求 |

## 外部限制

- **M5 真实登录 E2E**：需要 Azure 公共 Client ID（允许设备码流）。
- **CurseForge 真实 API E2E**：需要有效 API Key；镜像会转发无效 Key 并返回 403/500。
- **AppImage 直接挂载**：本机无 FUSE，使用 `APPIMAGE_EXTRACT_AND_RUN=1` 验证。
- **Windows/macOS**：代码跨平台，但未在对应系统实机验证。
- 尚未实现：多语言、皮肤/披风、HTTP 代理、服务器列表编辑器、Forge/Neo 旧版本安装器。
