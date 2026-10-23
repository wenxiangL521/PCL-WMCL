# 人工配置

启动器尽量自动完成安装/Java/下载；以下需要你本人提供账号、Key 或系统权限。**所有密钥都不要提交到 Git、截图或日志。**

## 1. CurseForge API Key（可选）

仅 CurseForge 搜索/下载需要；Modrinth、原版安装和启动不需要。

1. 打开 <https://console.curseforge.com/> → **API Keys** → 创建 Key。
2. 启动器 **设置 → 其他 → CurseForge Key** 粘贴并点“测试”。
3. 或启动时使用环境变量：

```bash
WMCL_CURSEFORGE_API_KEY='你的key' dotnet run --project src/WMCL.App -c Release
```

Key 是账号凭据，不要写入仓库或公开 issue。

## 2. 微软正版登录 Client ID（可选）

离线账户/Authlib 不需要。微软登录使用**设备码 + 公共客户端**，不保存 Client Secret。

1. 打开 <https://entra.microsoft.com/> → **应用注册** → **新注册**。
2. 账户类型选 **任何组织目录中的账户和个人 Microsoft 账户**。
3. 复制 **应用程序(客户端) ID**。
4. **身份验证 → 高级设置 → 允许公共客户端流** 设为 **是**。
5. 不需要创建客户端密码。
6. 启动器 **设置 → 其他 → 微软 Client ID** 填入并保存。
7. 启动页选“正版”，按提示打开 <https://microsoft.com/link> 输入设备码。

> 新应用默认不能调用 Minecraft 服务，`login_with_xbox` 会返回 `403 Invalid app registration`。
> 需要按 <https://help.minecraft.net/hc/en-us/articles/16254801392141> 申请 Minecraft 白名单；
> 审批通过后同一个 Client ID 生效，代码不用改。本项目不提供 Client ID。

## 3. EasyTier（默认联机方案，可选）

- 需要能访问公共中继；可“获取公共节点”或“自动选择中继”。
- Linux 需要 `/dev/net/tun` 与 `CAP_NET_ADMIN`：

```bash
sudo modprobe tun
ls -l /dev/net/tun
sudo setcap cap_net_admin+ep ~/.cache/WMCL/easytier/<版本>/easytier-core
```

失败时启动器会提示，并可切换到陶瓦。

## 4. 陶瓦 / Terracotta（备用）

1. 联机页切换为 **陶瓦（HMCL）**。
2. 房主创建房间 → 启动陶瓦 → 在 Minecraft 中对局域网开放 → 把 `U/...` 房间码发给朋友。
3. 客人加入房间 → 启动陶瓦 → 多人游戏直连界面显示的 `127.0.0.1:端口`。
4. 停止时点“停止”。

Terracotta 由 burningtnt 开发，AGPL-3.0：<https://github.com/burningtnt/Terracotta>。本项目通过本地 HTTP API 调用未修改的二进制；可用 `WMCL_TERRACOTTA_PATH` 指定本地文件。

## 5. Nide / 第三方认证

启动页“第三方” → 填 Nide 服务器 ID 或完整 Authlib/Yggdrasil 地址 → 登录。实例设置可固定认证服务器。

## 6. Java

设置页可安装 Java 8/17/21，或手动指定路径；已有系统 Java 会优先复用。下载失败通常是网络/镜像问题。

## 7. 更新源（可选）

准备 `latest.json` 后，在 **设置 → 其他 → 更新源** 填 URL，在关于页检查/下载。Linux 运行中的单文件替换需要辅助脚本/AppImage；Windows/macOS 自动更新尚未实机验证。

## 8. 账号与密钥边界

- 微软/CurseForge 账号与密钥必须由你本人提供。
- 真实 Azure Client ID、CurseForge Key 的联网验证需要在你的网络/账号下完成。
- PCL2 官方 EasyTier 房间服务、遥测/赞助/秘钥不在开源重写范围内。
