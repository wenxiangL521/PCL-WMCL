# Plain Craft Launcher (PCL) WMCL

**WMCL**（wenxiangL Minecraft Launcher）是 PCL2 的第三方、非官方跨平台重写，使用 **.NET 10 + C# + Avalonia**。

> 与龙腾猫跃（LTCat）/ PCL 官方无隶属关系，不代表官方发布。
> 原版作者：龙腾猫跃（LTCat） · 原版仓库：<https://github.com/Hex-Dragon/PCL2>
> 原版使用《PCL 分发有限许可与合理使用指南》，不是 OSI 开源许可。

## 状态

公开预览版。已覆盖原版安装、Java、启动、Fabric/Forge/NeoForge/OptiFine、Modrinth/CurseForge、整合包、实例管理、资源/光影/存档、微软/Authlib 登录和联机基础流程。

仍未完成：真实微软 OAuth/CurseForge E2E、Windows/macOS 实机更新、高 DPI、进入世界/声音/输入验证，以及部分 PCL2/CE/ME 页面细节。

## 构建

要求 .NET SDK 10.0.x。

```bash
dotnet build WMCL.slnx -c Release
dotnet test  WMCL.slnx -c Release
dotnet run   --project src/WMCL.App -c Release
```

Windows 单文件发布：

```bash
dotnet publish src/WMCL.App/WMCL.App.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o dist/win-x64
```

## 文档

- [开发与测试](docs/DEVELOPMENT.md)
- [账号/Key/联机配置](docs/SETUP.md)
- [验证状态](docs/VERIFICATION.md)
- [UI 对照与已知差异](docs/UI_REFERENCE.md)
- [GitHub 发布信息](docs/GITHUB_PUBLISH.md)
- [贡献指南](CONTRIBUTING.md)

## 许可

新增代码 MIT，见 [LICENSE.md](LICENSE.md)。PCL2 指南与第三方素材见 [PCL-LICENCE.md](PCL-LICENCE.md)、[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。Minecraft 是 Mojang / Microsoft 的付费游戏，请购买正版。
