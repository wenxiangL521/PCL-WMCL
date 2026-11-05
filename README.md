# Plain Craft Launcher (PCL) WMCL

**WMCL**（wenxiangL Minecraft Launcher）是 PCL2 的第三方、非官方跨平台重写，使用 **.NET 10 + C# + Avalonia 11**，目标平台为 Windows / Linux / macOS。

> 与龙腾猫跃（LTCat）/ PCL 官方无隶属关系，不代表官方发布。  
> 原版作者：龙腾猫跃（LTCat） · 原版仓库：<https://github.com/Hex-Dragon/PCL2>  
> 原版使用《PCL 分发有限许可与合理使用指南》，不是 OSI 开源许可。

## 状态

当前版本 `0.2.2`。功能、构建、打包与验证细节见 [GUIDE.md](GUIDE.md)。

## 功能概览

- **启动**：原版 / Fabric / Quilt / LegacyFabric / Forge / NeoForge / OptiFine 安装与启动，版本校验、崩溃分析、日志查看
- **账号**：微软设备码登录（启动时校验令牌）、离线、第三方 Authlib；皮肤与披风编辑
- **下载**：版本目录、Mod / 整合包 / 数据包 / 资源包 / 光影 / 存档，Modrinth 与 CurseForge（无 Key 自动走镜像）
- **实例**：十个分区、克隆/重命名/删除/回收站、导出备份或分享整合包、导入、Mod 批量管理
- **联机**：EasyTier（缺 TUN 权限自动 pkexec 提权）、陶瓦备用、可加入 Astral / Astral Game 房间
- **界面**：PCL2 风格主题与动画、内置鸿蒙字体并支持自选、功能隐藏（F12 临时显示）、窗口自由缩放
- **更新**：读取 GitHub Releases，可配置通道（稳定/测试）与方式（仅提示 / 自动下载 / 自动安装）

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
