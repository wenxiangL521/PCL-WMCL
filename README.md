# Plain Craft Launcher (PCL) WMCL

**WMCL**（wenxiangL Minecraft Launcher）是 PCL2 的第三方、非官方跨平台重写，使用 **.NET 10 + C# + Avalonia 11**，目标平台为 Windows / Linux / macOS。

> 与龙腾猫跃（LTCat）/ PCL 官方无隶属关系，不代表官方发布。  
> 原版作者：龙腾猫跃（LTCat） · 原版仓库：<https://github.com/Hex-Dragon/PCL2>  
> 原版使用《PCL 分发有限许可与合理使用指南》，不是 OSI 开源许可。

## 状态

公开预览版（当前版本 `0.1.1`）。功能、构建、GitHub 发布与验证细节见 [GUIDE.md](GUIDE.md)。

## 快速构建

```bash
dotnet build WMCL.slnx -c Release
dotnet test  WMCL.slnx -c Release
dotnet run   --project src/WMCL.App -c Release
```

## 许可

- 新增代码：MIT，见 [LICENSE.md](LICENSE.md)
- PCL2 指南与第三方素材：[PCL-LICENCE.md](PCL-LICENCE.md)、[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
- Minecraft 是 Mojang / Microsoft 的付费游戏，请购买正版

## 贡献

欢迎第三方开发者贡献。开始前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md) 与 [GUIDE.md](GUIDE.md)，并遵守 PCL2 重度使用约束（署名、命名、Windows 蓝色主色相、正版购买劝导等）。
