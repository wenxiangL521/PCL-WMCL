using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCL.Launch.Auth;

public sealed record AuthlibAccount(
    string Server,
    string Name,
    string Uuid,
    string AccessToken,
    string ClientToken,
    string UserType = "mojang");

/// <summary>
/// 第三方 Yggdrasil / Authlib 登录。对应 PCL2 的 Nide/Auth 登录的公共部分：
/// POST /authserver/authenticate、refresh、validate。
/// </summary>
public sealed class AuthlibAuthService
{
    private readonly HttpClient _http;

    public AuthlibAuthService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-Linux/0.1");
    }

    public async Task<AuthlibAccount> AuthenticateAsync(
        string server,
        string username,
        string password,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("账号和密码不能为空。");

        var baseUrl = NormalizeServer(server);
        var clientToken = Guid.NewGuid().ToString("N");
        var payload = new
        {
            agent = new { name = "Minecraft", version = 1 },
            username,
            password,
            clientToken,
            requestUser = true
        };
        using var response = await _http.PostAsJsonAsync(
            baseUrl + "/authserver/authenticate", payload, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ErrorMessage(json, response.StatusCode));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var accessToken = root.TryGetProperty("accessToken", out var access) ? access.GetString() : null;
        var responseClient = root.TryGetProperty("clientToken", out var client) ? client.GetString() : null;
        var profile = ReadProfile(root);
        if (string.IsNullOrWhiteSpace(accessToken) || profile is null)
            throw new InvalidOperationException("认证服务器没有返回可用的角色。");

        return new AuthlibAccount(
            baseUrl,
            profile.Value.Name,
            profile.Value.Uuid,
            accessToken,
            string.IsNullOrWhiteSpace(responseClient) ? clientToken : responseClient);
    }

    public async Task<AuthlibAccount> RefreshAsync(
        AuthlibAccount account,
        CancellationToken ct = default)
    {
        var payload = new
        {
            accessToken = account.AccessToken,
            clientToken = account.ClientToken,
            requestUser = true
        };
        using var response = await _http.PostAsJsonAsync(
            account.Server + "/authserver/refresh", payload, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ErrorMessage(json, response.StatusCode));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var accessToken = root.TryGetProperty("accessToken", out var access) ? access.GetString() : null;
        var profile = ReadProfile(root);
        if (string.IsNullOrWhiteSpace(accessToken) || profile is null)
            throw new InvalidOperationException("刷新登录没有返回可用的角色。");

        return account with
        {
            Name = profile.Value.Name,
            Uuid = profile.Value.Uuid,
            AccessToken = accessToken
        };
    }

    /// <summary>返回 false 表示 token 已失效；服务器不支持 validate 时按可用处理。</summary>
    public async Task<bool> ValidateAsync(AuthlibAccount account, CancellationToken ct = default)
    {
        var payload = new
        {
            accessToken = account.AccessToken,
            clientToken = account.ClientToken
        };
        using var response = await _http.PostAsJsonAsync(
            account.Server + "/authserver/validate", payload, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent) return true;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            return false;
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            return true;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ErrorMessage(
                await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false),
                response.StatusCode));
        return true;
    }

    public static string NormalizeServer(string server)
    {
        var value = server.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("认证服务器地址不能为空。");
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("认证服务器地址无效。");
        return uri.ToString().TrimEnd('/');
    }

    private static (string Name, string Uuid)? ReadProfile(JsonElement root)
    {
        JsonElement profile;
        if (root.TryGetProperty("selectedProfile", out var selected) &&
            selected.ValueKind == JsonValueKind.Object)
        {
            profile = selected;
        }
        else if (root.TryGetProperty("availableProfiles", out var available) &&
                 available.ValueKind == JsonValueKind.Array &&
                 available.GetArrayLength() > 0)
        {
            profile = available[0];
        }
        else
        {
            return null;
        }

        var name = profile.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
        var id = profile.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) return null;
        return (name, AddDashes(id));
    }

    private static string AddDashes(string id)
    {
        var raw = id.Replace("-", "");
        return raw.Length == 32
            ? $"{raw[..8]}-{raw[8..12]}-{raw[12..16]}-{raw[16..20]}-{raw[20..]}"
            : id;
    }

    private static string ErrorMessage(string json, HttpStatusCode status)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("errorMessage", out var message))
                return message.GetString() ?? $"认证失败（{(int)status}）";
            if (document.RootElement.TryGetProperty("error", out var error))
                return error.GetString() ?? $"认证失败（{(int)status}）";
        }
        catch (JsonException)
        {
            // fallthrough
        }
        return $"认证失败（{(int)status}）";
    }
}

public sealed class AuthlibAccountStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath =>
        Path.Combine(PCL.Core.PclPaths.DataDirectory, "accounts", "authlib.json");

    public static AuthlibAccount? Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        if (!File.Exists(file)) return null;
        try
        {
            return JsonSerializer.Deserialize<AuthlibAccount>(File.ReadAllText(file), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Save(AuthlibAccount account, string? path = null)
    {
        var file = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(account, Options));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static void Clear(string? path = null)
    {
        var file = path ?? DefaultPath;
        if (File.Exists(file)) File.Delete(file);
    }
}
