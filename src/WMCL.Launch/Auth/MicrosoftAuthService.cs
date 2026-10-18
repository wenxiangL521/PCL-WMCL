using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WMCL.Core.Logging;

namespace WMCL.Launch.Auth;

public sealed record MicrosoftEndpoints(
    string DeviceCode,
    string Token,
    string XboxUser,
    string Xsts,
    string MinecraftLogin,
    string MinecraftProfile,
    string Entitlements)
{
    public static MicrosoftEndpoints Default { get; } = new(
        "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode",
        "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
        "https://user.auth.xboxlive.com/user/authenticate",
        "https://xsts.auth.xboxlive.com/xsts/authorize",
        "https://api.minecraftservices.com/authentication/login_with_xbox",
        "https://api.minecraftservices.com/minecraft/profile",
        "https://api.minecraftservices.com/entitlements/mcstore");
}

public sealed record DeviceCodeInfo(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string? VerificationUriComplete,
    int Interval,
    int ExpiresIn,
    string Message);

public sealed record MicrosoftTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);

public sealed record MicrosoftAccount(
    string Name,
    string Uuid,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);

public sealed record MicrosoftAuthProgress(
    string Stage,
    string? UserCode = null,
    string? VerificationUri = null,
    string? Message = null);

/// <summary>
/// 微软设备码登录：对照原版 PCL2 的 6 步流程（devicecode → XBL → XSTS → MC → entitlements → profile）。
/// 需要 Azure 应用注册的公共 client ID，并允许设备码流。
/// </summary>
public sealed class MicrosoftAuthService
{
    private const string Scope = "XboxLive.signin offline_access";
    private readonly HttpClient _http;
    private readonly MicrosoftEndpoints _endpoints;

    public MicrosoftAuthService(HttpClient? http = null, MicrosoftEndpoints? endpoints = null)
    {
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WMCL/0.1");
        _endpoints = endpoints ?? MicrosoftEndpoints.Default;
    }

    public async Task<DeviceCodeInfo> RequestDeviceCodeAsync(
        string clientId,
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = Scope
        };
        var json = await PostFormAsync(_endpoints.DeviceCode, form, ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new DeviceCodeInfo(
            GetString(root, "device_code"),
            GetString(root, "user_code"),
            GetString(root, "verification_uri"),
            root.TryGetProperty("verification_uri_complete", out var complete)
                ? complete.GetString()
                : null,
            root.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 5,
            root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 900,
            GetString(root, "message"));
    }

    public async Task<MicrosoftTokens> PollForTokenAsync(
        string clientId,
        DeviceCodeInfo deviceCode,
        CancellationToken ct = default)
    {
        var interval = Math.Max(1, deviceCode.Interval);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(deviceCode.ExpiresIn);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct).ConfigureAwait(false);

            var form = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = deviceCode.DeviceCode
            };

            var (status, json) = await PostFormWithStatusAsync(_endpoints.Token, form, ct)
                .ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (status == HttpStatusCode.OK)
            {
                var access = GetString(root, "access_token");
                var refresh = GetString(root, "refresh_token");
                var expires = root.TryGetProperty("expires_in", out var expiresElement)
                    ? expiresElement.GetInt32()
                    : 3600;
                if (string.IsNullOrEmpty(access))
                    throw new InvalidOperationException("微软返回的 access_token 为空。");
                return new MicrosoftTokens(
                    access, refresh, DateTimeOffset.UtcNow.AddSeconds(expires));
            }

            var error = GetString(root, "error");
            switch (error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += 5;
                    continue;
                case "authorization_declined":
                    throw new OperationCanceledException("用户拒绝了微软登录授权。");
                case "expired_token":
                    throw new InvalidOperationException("设备码已过期，请重新登录。");
                case "bad_verification_code":
                    throw new InvalidOperationException("设备码无效，请重新登录。");
                default:
                    throw new InvalidOperationException(
                        "微软登录失败：" + (GetString(root, "error_description") is { Length: > 0 } description
                            ? description
                            : error));
            }
        }

        throw new TimeoutException("微软登录等待超时，请重新登录。");
    }

    public async Task<MicrosoftAccount> LoginAsync(
        string clientId,
        IProgress<MicrosoftAuthProgress>? progress = null,
        CancellationToken ct = default,
        bool saveAccount = true)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("未配置微软 Client ID。");

        progress?.Report(new MicrosoftAuthProgress("请求设备码"));
        var deviceCode = await RequestDeviceCodeAsync(clientId, ct).ConfigureAwait(false);
        progress?.Report(new MicrosoftAuthProgress(
            "等待微软登录", deviceCode.UserCode, deviceCode.VerificationUri, deviceCode.Message));

        var tokens = await PollForTokenAsync(clientId, deviceCode, ct).ConfigureAwait(false);
        return await CompleteLoginAsync(
            tokens.AccessToken, tokens.RefreshToken, progress, ct, saveAccount)
            .ConfigureAwait(false);
    }

    /// <summary>使用 refresh_token 续期，避免 AccessToken 过期后必须重新走设备码。</summary>
    public async Task<MicrosoftAccount> RefreshAsync(
        string clientId,
        string refreshToken,
        IProgress<MicrosoftAuthProgress>? progress = null,
        CancellationToken ct = default,
        bool saveAccount = true)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("未配置微软 Client ID。");

        progress?.Report(new MicrosoftAuthProgress("刷新微软登录"));
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope
        };

        var (status, json) = await PostFormWithStatusAsync(_endpoints.Token, form, ct)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (status != HttpStatusCode.OK)
            throw new InvalidOperationException(
                "刷新微软登录失败：" + GetString(root, "error_description"));

        var access = GetString(root, "access_token");
        var refresh = GetString(root, "refresh_token");
        if (string.IsNullOrEmpty(access))
            throw new InvalidOperationException("刷新微软登录未返回 access_token。");

        return await CompleteLoginAsync(
            access,
            string.IsNullOrEmpty(refresh) ? refreshToken : refresh,
            progress, ct, saveAccount).ConfigureAwait(false);
    }

    private async Task<MicrosoftAccount> CompleteLoginAsync(
        string oauthAccessToken,
        string refreshToken,
        IProgress<MicrosoftAuthProgress>? progress,
        CancellationToken ct,
        bool saveAccount)
    {
        progress?.Report(new MicrosoftAuthProgress("验证 Xbox Live"));
        var xbl = await XboxAuthenticateAsync(oauthAccessToken, ct).ConfigureAwait(false);

        progress?.Report(new MicrosoftAuthProgress("验证 XSTS"));
        var xsts = await XstsAuthorizeAsync(xbl.Token, ct).ConfigureAwait(false);

        progress?.Report(new MicrosoftAuthProgress("登录 Minecraft"));
        var minecraft = await MinecraftLoginAsync(xsts.Token, xsts.Uhs, ct).ConfigureAwait(false);

        progress?.Report(new MicrosoftAuthProgress("检查游戏授权"));
        await EnsureEntitledAsync(minecraft.AccessToken, ct).ConfigureAwait(false);

        progress?.Report(new MicrosoftAuthProgress("读取玩家档案"));
        var profile = await GetProfileAsync(minecraft.AccessToken, ct).ConfigureAwait(false);

        var account = new MicrosoftAccount(
            profile.Name,
            profile.Uuid,
            minecraft.AccessToken,
            refreshToken,
            DateTimeOffset.UtcNow.AddSeconds(minecraft.ExpiresIn));

        if (saveAccount) MicrosoftAccountStore.Save(account);
        Log.Info($"微软登录完成：{account.Name} ({account.Uuid})");
        return account;
    }

    private async Task<XboxToken> XboxAuthenticateAsync(string accessToken, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = accessToken.StartsWith("d=", StringComparison.Ordinal)
                    ? accessToken
                    : "d=" + accessToken
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        });

        var json = await PostJsonAsync(_endpoints.XboxUser, payload, ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new XboxToken(
            GetString(root, "Token"),
            GetUhs(root));
    }

    private async Task<XboxToken> XstsAuthorizeAsync(string xblToken, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xblToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        });

        var (status, json) = await PostJsonWithStatusAsync(_endpoints.Xsts, payload, ct)
            .ConfigureAwait(false);
        if (status != HttpStatusCode.OK)
        {
            if (json.Contains("2148916233", StringComparison.Ordinal))
                throw new InvalidOperationException("该账号尚未注册 Xbox 账户，请先注册。");
            if (json.Contains("2148916235", StringComparison.Ordinal))
                throw new InvalidOperationException("当前网络所在地区无法登录 Xbox Live。");
            if (json.Contains("2148916238", StringComparison.Ordinal))
                throw new InvalidOperationException("该账号年龄不足，请先修改出生日期。");
            if (json.Contains("2148916227", StringComparison.Ordinal))
                throw new InvalidOperationException("该账号已被微软封禁。");
            throw new InvalidOperationException("XSTS 验证失败：" + json);
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new XboxToken(GetString(root, "Token"), GetUhs(root));
    }

    private async Task<MinecraftSession> MinecraftLoginAsync(
        string xstsToken,
        string uhs,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            identityToken = $"XBL3.0 x={uhs};{xstsToken}"
        });
        var (status, json) = await PostJsonWithStatusAsync(_endpoints.MinecraftLogin, payload, ct)
            .ConfigureAwait(false);
        if (status != HttpStatusCode.OK)
        {
            // 新注册的 Entra 应用默认不在 Minecraft 白名单里，必须先在
            // help.minecraft.net/hc/en-us/articles/16254801392141 申请通过。
            if (status == HttpStatusCode.Forbidden &&
                json.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "该 Client ID 尚未通过 Minecraft 白名单审批（403 Invalid app registration），" +
                    "请改用已获批准的 Client ID，或参考 docs/MICROSOFT_LOGIN.md 提交申请。");
            if (status == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException(
                    "Minecraft 登录被拒绝：Xbox 令牌无效，或该账号没有 Java 版授权。");
            throw new InvalidOperationException("Minecraft 登录失败：" + json);
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var accessToken = GetString(root, "access_token");
        if (string.IsNullOrEmpty(accessToken))
            throw new InvalidOperationException("Minecraft 登录未返回 access_token。");
        var expires = root.TryGetProperty("expires_in", out var expiresElement)
            ? expiresElement.GetInt32()
            : 86400;
        return new MinecraftSession(accessToken, expires);
    }

    private async Task EnsureEntitledAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _endpoints.Entitlements);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("检查 Minecraft 授权失败：" + json);

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array ||
            items.GetArrayLength() == 0)
            throw new InvalidOperationException("该账号没有 Minecraft Java 版授权（或 Game Pass 已过期）。");
    }

    private async Task<MinecraftProfile> GetProfileAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _endpoints.MinecraftProfile);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                response.StatusCode == HttpStatusCode.NotFound
                    ? "该账号尚未创建 Minecraft 玩家档案。"
                    : "读取 Minecraft 档案失败：" + json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new MinecraftProfile(GetString(root, "name"), GetString(root, "id"));
    }

    private async Task<string> PostFormAsync(
        string url,
        Dictionary<string, string> form,
        CancellationToken ct)
    {
        var (status, json) = await PostFormWithStatusAsync(url, form, ct).ConfigureAwait(false);
        if (status != HttpStatusCode.OK)
            throw new InvalidOperationException($"请求 {url} 失败：{json}");
        return json;
    }

    private async Task<(HttpStatusCode Status, string Json)> PostFormWithStatusAsync(
        string url,
        Dictionary<string, string> form,
        CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
        return (response.StatusCode,
            await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    private async Task<string> PostJsonAsync(string url, string payload, CancellationToken ct)
    {
        var (status, json) = await PostJsonWithStatusAsync(url, payload, ct).ConfigureAwait(false);
        if (status != HttpStatusCode.OK)
            throw new InvalidOperationException($"请求 {url} 失败：{json}");
        return json;
    }

    private async Task<(HttpStatusCode Status, string Json)> PostJsonWithStatusAsync(
        string url,
        string payload,
        CancellationToken ct)
    {
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
        return (response.StatusCode,
            await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? "" : "";

    private static string GetUhs(JsonElement root)
    {
        if (root.TryGetProperty("DisplayClaims", out var claims) &&
            claims.TryGetProperty("xui", out var xui) &&
            xui.ValueKind == JsonValueKind.Array &&
            xui.GetArrayLength() > 0)
            return GetString(xui[0], "uhs");
        return "";
    }

    private sealed record XboxToken(string Token, string Uhs);

    private sealed record MinecraftSession(string AccessToken, int ExpiresIn);

    private sealed record MinecraftProfile(string Name, string Uuid);
}
