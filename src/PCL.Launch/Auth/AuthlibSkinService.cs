using System.Net;
using System.Text;
using System.Text.Json;
using PCL.Core;

namespace PCL.Launch.Auth;

public sealed record AuthlibSkin(string? SkinUrl, string? CapeUrl, string? Model);

/// <summary>
/// 从 Authlib 的 sessionserver 读取皮肤纹理（base64 textures 属性），并缓存 PNG。
/// </summary>
public sealed class AuthlibSkinService
{
    private readonly HttpClient _http;
    private readonly string _cacheDirectory;

    public AuthlibSkinService(HttpClient? http = null, string? cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(PclPaths.CacheDirectory, "skins");
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-Linux/0.1");
    }

    public async Task<string?> DownloadSkinAsync(
        AuthlibAccount account,
        CancellationToken ct = default)
    {
        var skin = await GetSkinAsync(account, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(skin?.SkinUrl)) return null;

        Directory.CreateDirectory(_cacheDirectory);
        var target = Path.Combine(_cacheDirectory, account.Uuid + ".png");
        if (!File.Exists(target))
        {
            using var response = await _http.GetAsync(skin!.SkinUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var destination = File.Create(target);
            await source.CopyToAsync(destination, ct).ConfigureAwait(false);
        }
        return File.Exists(target) ? target : null;
    }

    public async Task<AuthlibSkin?> GetSkinAsync(
        AuthlibAccount account,
        CancellationToken ct = default)
    {
        var url = account.Server.TrimEnd('/') +
                  "/sessionserver/session/minecraft/profile/" +
                  Uri.EscapeDataString(account.Uuid.Replace("-", ""));
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"获取皮肤失败（{(int)response.StatusCode}）。");

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var property in properties.EnumerateArray())
        {
            if (!property.TryGetProperty("value", out var valueElement)) continue;
            var value = valueElement.GetString();
            if (string.IsNullOrWhiteSpace(value)) continue;
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                using var textures = JsonDocument.Parse(decoded);
                if (!textures.RootElement.TryGetProperty("textures", out var textureMap))
                    continue;
                if (!textureMap.TryGetProperty("SKIN", out var skinEntry) ||
                    !skinEntry.TryGetProperty("url", out var skinUrl))
                    continue;
                string? cape = null;
                if (textureMap.TryGetProperty("CAPE", out var capeEntry) &&
                    capeEntry.TryGetProperty("url", out var capeUrl))
                    cape = capeUrl.GetString();
                string? model = null;
                if (skinEntry.TryGetProperty("metadata", out var metadata) &&
                    metadata.TryGetProperty("model", out var modelValue))
                    model = modelValue.GetString();
                return new AuthlibSkin(skinUrl.GetString(), cape, model);
            }
            catch (FormatException)
            {
                // property 不是 base64，继续找下一个
            }
            catch (JsonException)
            {
                // 跳过损坏的 texture
            }
        }
        return null;
    }
}
