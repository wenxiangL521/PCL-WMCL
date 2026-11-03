using System.Net;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using WMCL.Core;

namespace WMCL.Launch.Auth;

public sealed record MicrosoftSkinInfo(string? SkinUrl, string? CapeUrl, string? Variant);
public sealed record MicrosoftSkinDownload(string? FilePath, string? Variant);

public sealed record MicrosoftCape(string Id, string Alias, string? Url, bool Active)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? Id : Alias;
}

/// <summary>从 Minecraft Services profile 获取正版皮肤/披风并缓存 PNG。</summary>
public sealed class MicrosoftSkinService
{
    private const string ProfileUrl = "https://api.minecraftservices.com/minecraft/profile";
    private readonly HttpClient _http;
    private readonly string _cacheDirectory;

    public MicrosoftSkinService(HttpClient? http = null, string? cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(WmclPaths.CacheDirectory, "skins");
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WMCL/0.1");
    }

    public async Task<string?> DownloadSkinAsync(MicrosoftAccount account, CancellationToken ct = default) =>
        (await DownloadSkinWithInfoAsync(account, forceRefresh: true, ct: ct).ConfigureAwait(false)).FilePath;

    public async Task<MicrosoftSkinDownload> DownloadSkinWithInfoAsync(MicrosoftAccount account,
        bool forceRefresh = false, CancellationToken ct = default)
    {
        var skin = await GetSkinAsync(account, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(skin?.SkinUrl)) return new(null, skin?.Variant);
        Directory.CreateDirectory(_cacheDirectory);
        var identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(account.Uuid + "\n" + skin.SkinUrl)));
        var prefix = "ms-" + identity + "-";
        if (!forceRefresh)
        {
            var existing = Directory.EnumerateFiles(_cacheDirectory, prefix + "*.png").FirstOrDefault();
            if (existing is not null) return new(existing, skin.Variant);
        }
        using var response = await _http.GetAsync(skin.SkinUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var target = Path.Combine(_cacheDirectory, prefix + Convert.ToHexStringLower(SHA256.HashData(bytes)) + ".png");
        if (!File.Exists(target))
        {
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                try { File.Move(temporary, target); }
                catch (IOException) when (File.Exists(target)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return new(target, skin.Variant);
    }

    /// <summary>上传本地 PNG 皮肤（classic/slim）。失败时抛出含响应内容的异常。</summary>
    public async Task UploadSkinAsync(
        MicrosoftAccount account,
        string pngPath,
        string variant = "classic",
        CancellationToken ct = default)
    {
        if (!File.Exists(pngPath)) throw new FileNotFoundException("皮肤文件不存在。", pngPath);
        SkinImageValidator.ValidateMinecraftSkin(pngPath);
        if (string.IsNullOrWhiteSpace(account.AccessToken) || account.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("登录已过期，请重新登录微软账号后上传皮肤。");
        if (!string.Equals(variant, "slim", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(variant, "classic", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("皮肤模型必须为 classic 或 slim。");
        var normalized = variant.ToLowerInvariant();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(normalized), "variant");
        var bytes = await File.ReadAllBytesAsync(pngPath, ct).ConfigureAwait(false);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(file, "file", Path.GetFileName(pngPath));

        using var request = new HttpRequestMessage(
            HttpMethod.Post, ProfileUrl + "/skins")
        { Content = content };
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", account.AccessToken);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("登录已过期，请重新登录微软账号。");
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"上传皮肤失败（{(int)response.StatusCode}）：{body}");
        }

        var cache = Path.Combine(_cacheDirectory, "ms-" + account.Uuid + ".png");
        if (File.Exists(cache)) File.Delete(cache);
    }

    public async Task<MicrosoftSkinInfo?> GetSkinAsync(
        MicrosoftAccount account,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ProfileUrl);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", account.AccessToken);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("登录已过期，请重新登录微软账号。");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"获取正版皮肤失败（{(int)response.StatusCode}）。");

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string? skinUrl = null, capeUrl = null, variant = null;
        if (root.TryGetProperty("skins", out var skins) && skins.ValueKind == JsonValueKind.Array)
        {
            var skin = skins.EnumerateArray().FirstOrDefault(item =>
                item.ValueKind == JsonValueKind.Object && item.TryGetProperty("state", out var state) &&
                string.Equals(state.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase));
            if (skin.ValueKind != JsonValueKind.Object)
                skin = skins.EnumerateArray().FirstOrDefault(item => item.ValueKind == JsonValueKind.Object &&
                    !item.TryGetProperty("state", out _));
            if (skin.ValueKind == JsonValueKind.Object)
            {
                if (skin.TryGetProperty("url", out var url)) skinUrl = url.GetString();
                if (skin.TryGetProperty("variant", out var variantValue)) variant = variantValue.GetString();
            }
        }
        if (root.TryGetProperty("capes", out var capes) && capes.ValueKind == JsonValueKind.Array)
        {
            var cape = capes.EnumerateArray().FirstOrDefault(item =>
                item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("state", out var state) &&
                string.Equals(state.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase));
            if (cape.ValueKind == JsonValueKind.Object &&
                cape.TryGetProperty("url", out var url))
                capeUrl = url.GetString();
        }
        return new MicrosoftSkinInfo(skinUrl, capeUrl, variant);
    }

    public async Task<IReadOnlyList<MicrosoftCape>> GetCapesAsync(
        MicrosoftAccount account,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ProfileUrl);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", account.AccessToken);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("登录已过期，请重新登录微软账号。");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"获取披风列表失败（{(int)response.StatusCode}）。");
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("capes", out var capes) ||
            capes.ValueKind != JsonValueKind.Array)
            return [];
        var result = new List<MicrosoftCape>();
        foreach (var cape in capes.EnumerateArray())
        {
            if (cape.ValueKind != JsonValueKind.Object) continue;
            var id = cape.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) continue;
            var alias = cape.TryGetProperty("alias", out var aliasElement) ? aliasElement.GetString() : null;
            var url = cape.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            var active = cape.TryGetProperty("state", out var stateElement) &&
                         string.Equals(stateElement.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
            result.Add(new MicrosoftCape(id, alias ?? "", url, active));
        }
        return result;
    }

    /// <summary>capeId 为 null 时移除当前披风。</summary>
    public async Task SetCapeAsync(
        MicrosoftAccount account,
        string? capeId,
        CancellationToken ct = default)
    {
        using var request = capeId is null
            ? new HttpRequestMessage(HttpMethod.Delete, ProfileUrl + "/capes/active")
            : new HttpRequestMessage(HttpMethod.Put, ProfileUrl + "/capes/active")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { capeId }),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", account.AccessToken);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("登录已过期，请重新登录微软账号。");
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"更改披风失败（{(int)response.StatusCode}）：{body}");
        }
    }
}
