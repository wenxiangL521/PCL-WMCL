# 微软正版登录：从零到可登录

本文是把「微软账号登录」跑通的一次性完整流程：**拿租户 → 注册应用 → 填 Client ID → 验证**。
普通用户不需要做任何一步；只有构建/分发这个启动器的人需要做一次。

```
租户(一次性) ──► 应用注册(一次性) ──► Client ID ──► 设置/config/CLI ──► 设备码登录
```

> 相关代码：`src/PCL.Launch/Auth/MicrosoftAuthService.cs`（协议实现）、
> `src/PCL.App/Services/AppSettings.cs`（`MicrosoftClientId`）、
> `src/PCL.App/ViewModels/MicrosoftLoginViewModel.cs`（GUI 入口）、
> `src/PCL.Cli/Program.cs`（`pcl login microsoft --client-id`）。

---

## 0. 为什么必须有租户

Minecraft 登录第一步是拿一个 **Microsoft 身份平台的 token**，而 token 只能由「某个租户里的应用」签发。
启动器团队必须自己注册这个应用（PCL、HMCL、Prism 都是这么做的），但**普通玩家登录时不需要属于任何租户**——
因为应用注册成「支持个人账号」后，个人微软账号可以直接向它授权。

反过来，如果你用个人账号去登 <https://entra.microsoft.com>（门户应用 `ADIbizaUX` 只接受租户内用户），
就会得到 `AADSTS50020 ... tenant 'Microsoft Services'`。那个租户是微软自己的，加不进去，也不该去加。

---

## 1. 拿到一个租户（三选一）

### 方案 A：Azure 免费账户（推荐，自己可控）

1. 用你要长期维护的微软账号打开 <https://azure.microsoft.com/free/>。
2. 按提示完成手机号 + 信用卡验证（只是验证，免费额度内不扣费；国内双币卡一般可用）。
3. 注册过程会自动创建一个目录，形如 `yourname.onmicrosoft.com`，你是**全局管理员**。
4. 打开 <https://entra.microsoft.com>，能进去即成功。

账号在目录里的 UPN 会显示成 `xxx_163.com#EXT#@yourname.onmicrosoft.com`，类型是「访客」。
**这是正常的**：微软账号在 Entra 里天然是外部身份，不影响管理员权限。

> ⚠️ 不要选 Azure 中国（21Vianet / `portal.azure.cn`）。那是独立云，和 Xbox Live、Minecraft 的全球认证不互通。

### 方案 B：被邀请为访客（不想绑卡）

1. 找任意已有租户的管理员，在 Entra 门户执行 **用户 → 邀请外部用户**，填你的邮箱。
2. 接受邀请后，让管理员给你 **「应用程序开发人员」(Application Developer)** 角色。
3. 你在那个租户里就能「应用注册 → 新注册」。

非验证域名只能作为访客加入，这是设计如此。

### 方案 C：已有公司/学校账号

直接用它登录 <https://entra.microsoft.com>，确认你有创建应用的权限（Application Developer 或更高）。

---

## 2. 注册应用（一次性）

入口：<https://entra.microsoft.com> → **应用 → 应用注册 → 新注册**。

| 字段 | 填什么 | 说明 |
|---|---|---|
| 名称 | `PCL Linux`（随意） | 会显示在用户授权页 |
| 受支持的帐户类型 | **任何组织目录中的帐户和个人 Microsoft 帐户** | 个人账号必须支持，否则 `consumers` 端点会拒绝 |
| 重定向 URI | **留空** | 设备码流程不需要；以后加授权码流程再加 `http://localhost` |

创建后做三件事：

1. **概述**页复制 **应用程序(客户端) ID** —— 这就是 `client_id`，形如 `00000000-0000-0000-0000-000000000000`。
2. **身份验证 → 高级设置 → 允许公共客户端流** 设为 **是**。
   设备码 / 授权码 + PKCE 都是公共客户端，不开会报 `AADSTS7000218`。
3. **不创建**客户端密码。公共客户端无法保守秘密，创建了反而会被误用。

可选：如果想确认受众改对了，在 **清单(Manifest)** 里看

```json
"signInAudience": "AzureADandPersonalMicrosoftAccount"
```

门户 UI 创建后不能直接改受众，改的话就在这里改。

> 关于 API 权限：`XboxLive.signin` / `offline_access` 是登录时动态请求的 scope，首次登录由用户同意即可，
> 不必在门户里预配置。如果门户里能加到「Xbox Live」委托权限，加上更稳；加不到也不影响设备码流程。

---

## 3. 把 Client ID 填进项目

三种方式任选，值相同：

```bash
# 1) GUI：设置 → 其他 → 微软 Client ID → 保存
# 2) config.json
~/.config/PCL/config.json      # "MicrosoftClientId": "xxxxxxxx-...."
# 3) CLI 直接传
dotnet run --project src/PCL.Cli -- login microsoft --client-id "xxxxxxxx-...."
```

对应实现：`AppSettings.MicrosoftClientId`，GUI 入口在 `SettingsView.axaml` 的「微软 Client ID」输入框。
未填写时，登录会直接提示「未配置微软 Client ID」，不会发请求。

### 可选：环境变量覆盖（尚未实现）

便于 CI 或临时试用，又不把 ID 写进配置文件：

```csharp
// AppSettings.cs
public string EffectiveMicrosoftClientId =>
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"))
        ? MicrosoftClientId.Trim()
        : Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID")!.Trim();
```

然后把 `MicrosoftLoginViewModel` 和 `LaunchViewModel` 里的 `Settings.MicrosoftClientId?.Trim()` 换成它。
PCL2 正是用 `PCL_MS_CLIENT_ID` 注入的，HMCL 用 `hmcl.microsoft.auth.id`。

---

## 4. 先用 curl 验证，再动启动器

这一步能区分「应用注册的问题」和「代码的问题」，别跳过。

### 4.1 申请设备码

```bash
export CID='<应用程序(客户端) ID>'

curl -sS https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode \
  --data-urlencode "client_id=$CID" \
  --data-urlencode "scope=XboxLive.signin offline_access"
```

成功会返回类似：

```json
{
  "user_code": "ABCDEFGH",
  "device_code": "…",
  "verification_uri": "https://microsoft.com/link",
  "expires_in": 900,
  "interval": 5,
  "message": "To sign in, use a web browser to open the page https://microsoft.com/link and enter the code ABCDEFGH to authenticate."
}
```

失败的话看 `error` 字段，先对照第 6 节。

### 4.2 浏览器输码

打开 <https://microsoft.com/link>，输入 `user_code`，用**有 Minecraft 正版的那个微软账号**登录并同意。

### 4.3 轮询换 token

```bash
export DC='<device_code>'

for i in $(seq 1 60); do
  resp=$(curl -sS https://login.microsoftonline.com/consumers/oauth2/v2.0/token \
    --data-urlencode "client_id=$CID" \
    --data-urlencode "grant_type=urn:ietf:params:oauth:grant-type:device_code" \
    --data-urlencode "device_code=$DC")
  echo "$resp" | head -c 200; echo
  case "$resp" in
    *'"access_token"'*) echo "OK"; break ;;
  esac
  sleep 5
done
```

看到 `access_token` + `refresh_token` 就说明 **应用注册和授权都没问题**，剩下的都是代码的事。
（`authorization_pending` 是正常的，继续轮询；15 分钟内没输码会变 `expired_token`。）

> 想在网页侧先确认账号有没有 Xbox 档案：登录 <https://www.xbox.com/> 能进个人主页即可，
> 否则后面 XSTS 会返回 `2148916233`。

### 4.4 可选：手动走一遍 XBL → XSTS

```bash
export AT='<access_token>'

curl -sS https://user.auth.xboxlive.com/user/authenticate \
  -H 'Content-Type: application/json' -H 'Accept: application/json' \
  -d "{\"Properties\":{\"AuthMethod\":\"RPS\",\"SiteName\":\"user.auth.xboxlive.com\",\"RpsTicket\":\"d=$AT\"},\"RelyingParty\":\"http://auth.xboxlive.com\",\"TokenType\":\"JWT\"}"
```

用返回的 `Token` 换 XSTS：

```bash
export XBL='<上一步的 Token>'

curl -sS https://xsts.auth.xboxlive.com/xsts/authorize \
  -H 'Content-Type: application/json' -H 'Accept: application/json' \
  -d "{\"Properties\":{\"SandboxId\":\"RETAIL\",\"UserTokens\":[\"$XBL\"]},\"RelyingParty\":\"rp://api.minecraftservices.com/\",\"TokenType\":\"JWT\"}"
```

拿到 `Token` + `DisplayClaims.xui[0].uhs` 就齐了；最后一步 `login_with_xbox` 已由项目实现。

---

## 5. 在项目里验证

```bash
# CLI 登录（会打印设备码并自动打开验证页）
dotnet run --project src/PCL.Cli -- login microsoft --client-id "$CID"

# 查看已保存的账号
dotnet run --project src/PCL.Cli -- login status

# 用正版账号启动
dotnet run --project src/PCL.Cli -- launch 1.21.1 --microsoft
```

GUI：启动器 → 设置 → 其他 → 填 Client ID → 保存 → 启动页选「正版」→ 按提示输码。

成功后账号信息（含 refresh token）保存在：

```
~/.local/share/PCL/account/microsoft.json
```

access token 约 24 小时过期，启动时会自动用 refresh token 续期。

---

## 6. 错误对照表

| 现象 | 含义 | 处理 |
|---|---|---|
| `AADSTS50020` / `AADSTS16000` … `Microsoft Services` | 账号不属于该租户，或应用不支持个人账号 | 应用的受众必须含「个人 Microsoft 帐户」；别拿个人账号去登 Entra 门户 |
| `AADSTS700016` | 该租户里找不到应用 | Client ID 抄错、应用建在别的租户，或单租户应用配了 `consumers` 端点 |
| `AADSTS7000218` | 请求要求 client_secret | 「允许公共客户端流」没开 |
| `AADSTS65001` / `consent_required` | 用户/管理员未同意 | 重新登录并同意；或让管理员授予同意 |
| `unauthorized_client` | 应用类型不支持设备码 | 检查受众与公共客户端设置 |
| `invalid_client` | Client ID 不存在或与端点云不匹配 | 检查是否用了 Azure 中国租户的应用 |
| `invalid_grant` / `AADSTS70000` | refresh token 失效（改密、风控、撤销） | 重新走设备码 |
| `authorization_pending` | 用户还没在网页上输码 | 正常，继续轮询 |
| `slow_down` | 轮询太快 | 间隔 +5 秒 |
| `expired_token` | 设备码超过 15 分钟 | 重新申请 |
| XSTS `2148916233` | 该账号没有 Xbox 档案 | 去 <https://www.xbox.com/> 建一个 |
| XSTS `2148916238` | 未成年 / 未加入家庭组 | 改生日或由家庭组织者添加 |
| XSTS `2148916235` | 账号所在国家/地区不支持 | 换网络环境 |
| XSTS `2148916227` | 账号被封禁 | 无法登录 |

---

## 7. 安全与发布

- **Client ID 不是机密**，但别硬编码进开源仓库：它代表你的应用身份，泄露后可能被他人冒用（限流、封禁、冒名授权页）。
  参照 PCL/HMCL 的做法，构建时注入 + 运行时覆盖。
- **永远不要**为桌面应用创建 client secret；设备码和 PKCE 都不需要它。
- `microsoft.json` 里的 **refresh token 等价于账号凭据**，当前是明文存储。
  发行版建议至少 `chmod 600`，后续可接系统钥匙串（libsecret / DPAPI / Keychain）。
- 日志里不要打印 `access_token`、`refresh_token`、`device_code`。
- 联调阶段可以用官方启动器的公共 client ID `00000000402b5328` 快速验证代码路径，
  但**不要用于发布**：那是微软的客户端标识，随时可能被限制。

---

## 8. 验收清单

- [ ] 能用该账号登录 <https://entra.microsoft.com>
- [ ] 应用「受支持的帐户类型」含个人 Microsoft 帐户
- [ ] 「允许公共客户端流」= 是，且没有 client secret
- [ ] 第 4 节 curl 流程能拿到 `access_token`
- [ ] `pcl login microsoft --client-id` 成功
- [ ] GUI 设置页填写后能登录，且生成 `~/.local/share/PCL/account/microsoft.json`
