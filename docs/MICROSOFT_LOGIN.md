# 微软登录与 Minecraft 白名单

## 403 Invalid app registration 是什么意思

微软账号登录本身已经成功（设备码、Xbox Live、XSTS 都通过了），失败发生在最后一步：

```text
POST https://api.minecraftservices.com/authentication/login_with_xbox
HTTP 403 Invalid app registration
```

Minecraft 服务只接受**经过 Mojang 白名单审批的 Azure 应用 Client ID**。新注册的 Entra 应用默认不在白名单里，所以即使 Azure 配置完全正确，也会在这一步被拒绝。

这不是 Client ID、公共客户端流或权限配置的问题，换一个 Java 版本、重启启动器都不会解决。

## 申请白名单

官方说明页：

<https://help.minecraft.net/hc/en-us/articles/16254801392141>

页面中的申请表单：

<https://forms.cloud.microsoft/Pages/ResponsePage.aspx?id=v4j5cvGGr0GRqy180BHbR-ajEQ1td1ROpz00KtS8Gd5UNVpPTkVLNFVROVQxNkdRMEtXVjNQQjdXVC4u>

填写时通常需要：

- Azure 应用的 **Application (client) ID**；
- 启动器名称、用途、下载/项目地址；
- 隐私政策与服务条款地址；
- 说明只使用官方 Microsoft/Xbox/Minecraft 认证流程，不绕过正版验证。

提交后等待 Mojang Enforcement 的邮件。审批是人工的，不保证通过，也不会立即生效。

## 个人临时使用

- 只想进游戏：用**离线账号**，不需要 Client ID；
- 只是想用微软正版登录：可以先用 PCL2 / HMCL 等已经获得批准的启动器登录；
- **不要**把其他启动器的 Client ID 填进自己的项目长期使用：它不属于你，可能被撤销，也不符合微软/ Mojang 的使用条款。

## 项目发布

如果 WMCL 要对外发布并支持微软登录，应由项目维护者注册自己的 Azure 应用并申请白名单，而不是让每个用户自建 Client ID。用户自建的 ID 无法通过 Minecraft 服务认证。

## 内置到启动器给别人用

内置的 Client ID 仍然走**同一个 Mojang 白名单**，但申请主体从“个人测试”变成“项目”：

1. **用项目自己的微软账号/租户注册 Entra 应用**，不要用个人临时账号；应用名与启动器一致（例如 `PCL (WMCL)`），并开启公共客户端流、允许个人 Microsoft 账户。
2. **以项目身份提交白名单表单**，填写启动器名称、项目/下载地址、隐私政策、服务条款、联系方式，说明只使用官方认证流程、不绕过正版验证。
3. 审核通过后，这一个 Client ID 就可以内置给所有用户使用；**用户不需要各自注册 Azure 应用**，也不需要各自申请。
4. 应用注册里不要放客户端密码；公共客户端本来就没有 secret。不要把任何密钥写进二进制。

### 可能还需要

- **发布者验证（Publisher Verification）**：公开分发时，未验证发布者的应用可能出现“未验证应用”提示，多租户应用还可能被限制用户同意。可在 Microsoft Partner Center 验证域名/组织并关联应用。它不是绕过 403 的替代品，但能改善公开分发时的登录体验。
- **隐私政策 / 服务条款 / 支持邮箱**：Mojang 审核和微软应用页面都会看这些。
- **滥用与限流**：所有用户共用同一个 Client ID，被滥用可能被微软或 Mojang 撤销；需要限制异常请求、妥善保存 token，不要明文记录账号令牌。
- **合规**：离线模式与正版登录要明确区分；不要在同一启动器里提供绕过正版验证的功能，否则白名单可能被撤销。

### 不需要

- 不需要每个用户申请 Client ID；
- 不需要客户端密码；
- 通常不需要注册 Xbox 开发者/ID@Xbox（该团队通常不处理启动器白名单，走上面的 Minecraft 表单）；
- 不要把其他启动器已获批的 Client ID 内置到自己的发行版里。
