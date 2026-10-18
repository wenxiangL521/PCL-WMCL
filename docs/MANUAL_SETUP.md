# 人工配置步骤

本项目尽量把能自动化的部分自动化；以下功能需要你本人完成账号/密钥/系统权限操作。
所有密钥都不要提交到 Git、截图或日志里。

## 1. CurseForge API Key（可选）

只有使用 CurseForge 搜索/下载/整合包时才需要；Modrinth 不需要。

1. 打开 <https://console.curseforge.com/> 并登录 CurseForge 账号。
2. 进入 **API Keys**，创建一个 API Key（名称随意，例如 `PCL-Linux`）。
3. 复制生成的 Key。
4. 在启动器打开 **设置 → 其他 → CurseForge Key**，粘贴并保存。
5. 或使用环境变量启动：

```bash
PCL_CURSEFORGE_API_KEY='你的key' dotnet run --project src/PCL.App
```

6. 重启启动器后，资源页选择 CurseForge 应能搜索。

注意：
- Key 是账号凭据，不要写进仓库、issue 或公开日志。
- CurseForge 可能对 Key 有申请/审核/配额限制；以官网当前说明为准。
- 不配置也能正常使用 Modrinth、原版安装、启动等。

## 2. 微软正版登录 Client ID（可选）

离线账户/Authlib 不需要。若要正版登录：

1. 打开 <https://entra.microsoft.com/> → **应用注册** → **新注册**。
2. 名称任意；账户类型按你的使用范围选择。
3. 注册后在“概述”复制 **应用程序（客户端）ID**。
4. 进入 **身份验证 → 高级设置**，把 **允许公共客户端流** 设为 **是**（设备码登录需要）。
5. 不需要创建客户端密码；本项目使用设备码流程，不保存 secret。
6. 在启动器 **设置 → 其他 → 微软 Client ID** 填入并保存。
7. 回到启动页选“正版”，按提示打开 <https://microsoft.com/link> 输入设备码完成登录。

注意：
- 仍需拥有 Minecraft: Java Edition 的正版账号。
- 若提示 scope/权限错误，确认应用允许公共客户端流，并检查 Azure 租户策略。
- 本项目不提供 Azure Client ID，也不代管你的账号。

## 3. EasyTier（默认联机方案，可选）

EasyTier 不需要 API Key，但需要：

1. 本机能访问公共中继（默认 `tcp://public.easytier.cn:11010`）；
2. 有创建 TUN 设备的权限（Linux 需要 `/dev/net/tun` + `CAP_NET_ADMIN`）。

Linux 可给下载的 core 加能力（路径以实际缓存为准）：

```bash
sudo setcap cap_net_admin+ep ~/.cache/PCL/easytier/<版本>/easytier-core
```

如果公司/学校网络屏蔽 TUN、UDP 或公共中继，EasyTier 可能拿不到虚拟 IP。
本项目会在失败后提示，并可自动/手动切换到陶瓦联机。

## 4. 陶瓦联机 / Terracotta（EasyTier 不可用时的备用方案）

不需要 API Key，也不需要你安装 Easytier。

1. 联机页把“联机方式”切到 **陶瓦（HMCL）**。
2. 第一次启动会从 GitHub 下载 Terracotta 发行包到 `PCL/cache/terracotta`。
3. 房主：
   - 在 Minecraft 中打开世界 → **对局域网开放**；
   - 选择“创建房间”，房间码可留空自动生成；
   - 点“启动陶瓦”，等待状态变成“房主已就绪”；
   - 把生成的 `U/XXXX-XXXX-XXXX-XXXX` 房间码发给朋友。
4. 客人：
   - 选择“加入房间”，填入房主给的房间码；
   - 点“启动陶瓦”，成功后复制显示的 `127.0.0.1:端口` 地址；
   - 在 Minecraft 多人游戏里直连该地址。
5. 停止时点“停止”，会结束 Terracotta 进程。

说明：
- Terracotta 由 burningtnt 开发，基于 EasyTier，AGPL-3.0；本项目只通过本地 HTTP API 调用未修改的二进制，并在界面标注来源。
- 若 GitHub 下载失败，可自行下载 Linux 发行包并设置：

```bash
PCL_TERRACOTTA_PATH=/path/to/terracotta dotnet run --project src/PCL.App
```

- 房主/客人需要能访问 Terracotta 使用的公共节点；受限网络可能仍然失败。
- 没有提前开放局域网世界时，房主会停在扫描阶段。

## 5. Nide / 第三方认证服务器（可选）

1. 向服务器管理员获取 **Nide 服务器 ID**，或完整的 Authlib/Yggdrasil 地址。
2. 启动页点“第三方”。
3. Nide：填服务器 ID，点“使用 Nide”；或直接填认证服务器地址。
4. 输入该服务器的账号/密码登录。
5. 若某个实例要求固定认证服务器，在实例设置里填写“第三方认证服务器”，启动时会匹配对应账号。

## 6. Java（可选，自动下载）

- 设置页可选 Java 8/17/21，点“安装”会从 Mojang/镜像下载运行时。
- 已有系统 Java 时优先复用；也可以手动填 Java 路径。
- 下载失败通常是网络/镜像问题；可切换下载源或手动安装 Java 后填路径。

## 7. 更新源（可选）

1. 准备一个 `latest.json`（格式见项目更新服务）。
2. 设置 → 其他 → 更新源填 URL。
3. 关于页检查/下载更新。
4. Linux 运行中的二进制替换需要辅助脚本/AppImage；当前只保证下载与校验。

## 8. 不能自动完成/不可得的部分

- PCL2 官方 EasyTier 房间服务：开源代码明确不可用，本项目改用陶瓦或手动中继。
- PCL2 官方更新/遥测/赞助/秘钥：开源版为桩或移除。
- 微软/CurseForge 账号与密钥必须由你本人提供。
- 真实 Azure Client ID、CurseForge Key 的联网验证需要在你的网络/账号下完成。
