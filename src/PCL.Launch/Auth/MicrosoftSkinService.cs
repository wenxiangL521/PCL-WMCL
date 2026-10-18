using System.Net;
using System.Text.Json;
using PCL.Core;

namespace PCL.Launch.Auth;

public sealed record MicrosoftSkinInfo(string? SkinUrl, string? CapeUrl, string? Variant);

/// <summary>从 Minecraft Services profile 获取正版皮肤/披风并缓存 PNG。</summary>
public sealed class MicrosoftSkinService
{
    private const string ProfileUrl = "https://api.minecraftservices.com/minecraft/profile";
    private readonly HttpClient _http;
    private readonly string _cacheDirectory;

    public MicrosoftSkinService(HttpClient? http = null, string? cacheDirectory = null)
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

    public async Task<string?> DownloadSkinAsync(MicrosoftAccount account, CancellationToken ct = default)
    {
        var skin = await GetSkinAsync(account, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(skin?.SkinUrl)) return null;

        Directory.CreateDirectory(_cacheDirectory);
        var target = Path.Combine(_cacheDirectory, "ms-" + account.Uuid + ".png");
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

    public async Task<MicrosoftSkinInfo?> GetSkinAsync(
        MicrosoftAccount account,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ProfileUrl);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", account.AccessToken);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"获取正版皮肤失败（{(int)response.StatusCode}）。");

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string? skinUrl = null, capeUrl = null, variant = null;
        if (root.TryGetProperty("skins", out var skins) && skins.ValueKind == JsonValueKind.Array)
        {
            var skin = skins.EnumerateArray().FirstOrDefault();
            if (skin.ValueKind == JsonValueKind.Object)
            {
                if (skin.TryGetProperty("url", out var url)) skinUrl = url.GetString();
                if (skin.TryGetProperty("variant", out var variantValue)) variant = variantValue.GetString();
            }
        }
        if (root.TryGetProperty("capes", out var capes) && capes.ValueKind == JsonValueKind.Array)
        {
            var cape = capes.EnumerateArray().FirstOrDefault();
            if (cape.ValueKind == JsonValueKind.Object &&
                cape.TryGetProperty("url", out var url))
                capeUrl = url.GetString();
        }
        return new MicrosoftSkinInfo(skinUrl, capeUrl, variant);
    }
}
