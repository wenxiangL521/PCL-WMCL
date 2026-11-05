using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WMCL.Link;

/// <summary>
/// AstralGame 短码服务客户端。
/// 协议来自 AstralGame 的 share_code_service.dart：
/// POST /v1/codes 创建短码，GET /v1/codes/{code} 换取房间参数。
/// </summary>
public sealed class AstralShareCodeService
{
    /// <summary>内置短码服务地址（与 AstralGame 保持一致）。</summary>
    public const string BaseUrl = "http://103.194.107.25:8080/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public AstralShareCodeService(HttpClient? http = null, string? baseUrl = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _baseUrl = (baseUrl ?? BaseUrl).TrimEnd('/');
    }

    /// <summary>用短码换取房间参数；失败时抛出带说明的异常。</summary>
    public async Task<AstralRoom> ResolveAsync(string code, CancellationToken ct = default)
    {
        var normalized = AstralInviteParser.NormalizeShortCode(code);
        if (normalized.Length != 6)
            throw new InvalidOperationException("短码格式不正确。");

        using var response = await _http.GetAsync($"{_baseUrl}/v1/codes/{normalized}", ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("短码无效或已过期。");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"短码服务返回 {(int)response.StatusCode}。");

        var payload = await response.Content.ReadFromJsonAsync<CodePayload>(JsonOptions, ct).ConfigureAwait(false)
                      ?? throw new InvalidOperationException("短码服务返回了空内容。");
        if (string.IsNullOrWhiteSpace(payload.NetworkName) || string.IsNullOrWhiteSpace(payload.NetworkSecret))
            throw new InvalidOperationException("短码载荷不完整。");

        return new AstralRoom(
            string.IsNullOrWhiteSpace(payload.DisplayName) ? "Astral 房间" : payload.DisplayName!,
            payload.NetworkName!,
            payload.NetworkSecret!,
            (payload.Peers ?? []).Select(peer => peer.Uri)
                .Where(uri => !string.IsNullOrWhiteSpace(uri))
                .Select(uri => uri!)
                .ToList(),
            "Astral Game")
        {
            ShortCode = normalized
        };
    }

    /// <summary>为房间申请短码；失败时抛出异常，调用方回退到离线邀请。</summary>
    public async Task<string> CreateAsync(AstralRoom room, CancellationToken ct = default)
    {
        var body = new
        {
            v = 1,
            game_id = "minecraft",
            game_name = "我的世界",
            network_name = room.NetworkName,
            network_secret = room.NetworkSecret,
            peers = room.Peers.Select(uri => new { uri }).ToArray(),
            display_name = room.Name
        };

        using var response = await _http.PostAsJsonAsync($"{_baseUrl}/v1/codes", body, JsonOptions, ct)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("创建短码过于频繁，请稍后再试。");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"短码服务返回 {(int)response.StatusCode}。");

        var created = await response.Content.ReadFromJsonAsync<CreatedCode>(JsonOptions, ct).ConfigureAwait(false);
        var code = created?.Code;
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("短码服务没有返回短码。");
        return code!;
    }

    private sealed class CodePayload
    {
        [JsonPropertyName("v")]
        public int V { get; set; }

        [JsonPropertyName("network_name")]
        public string? NetworkName { get; set; }

        [JsonPropertyName("network_secret")]
        public string? NetworkSecret { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("peers")]
        public List<Peer>? Peers { get; set; }

        public sealed class Peer
        {
            [JsonPropertyName("uri")]
            public string? Uri { get; set; }
        }
    }

    private sealed class CreatedCode
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("expires_at")]
        public string? ExpiresAt { get; set; }
    }
}
