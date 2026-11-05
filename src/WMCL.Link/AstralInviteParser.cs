using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace WMCL.Link;

/// <summary>解析出来的 Astral 房间参数（网络名/密钥/节点，对应 EasyTier 的 identity）。</summary>
public sealed record AstralRoom(
    string Name,
    string NetworkName,
    string NetworkSecret,
    IReadOnlyList<string> Peers,
    string Source)
{
    /// <summary>AstralGame 短码；有值时需向短码服务换取房间参数。</summary>
    public string? ShortCode { get; init; }

    /// <summary>Astral 原版的「保护」标记：受保护房间带密码。</summary>
    public bool IsProtected { get; init; }

    /// <summary>需要先解析短码才能拿到网络名与密钥。</summary>
    public bool NeedsResolve => !string.IsNullOrWhiteSpace(ShortCode) && NetworkName.Length == 0;
}

/// <summary>
/// 解析 Astral 系客户端的房间邀请：
/// - 原版 Astral：<c>astral://room?code=base64url(gzip(json))</c>，字段 n/r/p/s
/// - AstralGame 离线邀请：<c>AG1.base64url(json)</c>，字段 network_name/network_secret/peers
/// 6 位短码需要 Astral 的服务端换取载荷，这里只给出提示。
/// </summary>
public static class AstralInviteParser
{
    private const string OfflinePrefix = "AG1.";

    public static bool TryParse(string? input, out AstralRoom? room, out string? error)
    {
        room = null;
        error = null;
        var text = (input ?? "").Trim();
        if (text.Length == 0)
        {
            error = "请粘贴 Astral 分享链接或分享码。";
            return false;
        }

        // 从链接里取出 token
        var token = ExtractToken(text);
        if (token is null)
        {
            error = "无法识别该分享内容：需要 astral://room?code=… 、astralgame://… 或 AG1. 开头的离线邀请。";
            return false;
        }

        if (token.StartsWith(OfflinePrefix, StringComparison.OrdinalIgnoreCase))
            return TryParseAstralGame(token[OfflinePrefix.Length..], out room, out error);

        if (LooksLikeShortCode(token))
        {
            room = new AstralRoom("Astral 房间", "", "", [], "Astral Game")
            {
                ShortCode = NormalizeShortCode(token)
            };
            return true;
        }

        return TryParseOriginal(token, out room, out error);
    }

    private static string? ExtractToken(string text)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme.Length > 0)
        {
            var fromQuery = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .Where(parts => parts.Length == 2 && parts[0] is "code" or "c")
                .Select(parts => Uri.UnescapeDataString(parts[1]))
                .FirstOrDefault(value => value.Length > 0);
            if (fromQuery is not null) return fromQuery;

            var segment = uri.Segments.LastOrDefault()?.Trim('/');
            if (!string.IsNullOrWhiteSpace(segment)) return Uri.UnescapeDataString(segment);

            if (!string.IsNullOrWhiteSpace(uri.Fragment))
                return Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
            return null;
        }

        return text;
    }

    /// <summary>按 Astral 原版格式生成分享链接：base64url(gzip(json))。</summary>
    public static string Encode(
        string name, string networkName, string networkSecret, IEnumerable<string> peers, bool isProtected = false)
    {
        var payload = JsonSerializer.Serialize(new
        {
            n = name,
            r = networkName,
            p = networkSecret,
            e = isProtected || !string.IsNullOrWhiteSpace(networkSecret) ? 1 : 0,
            s = peers.ToArray()
        });
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            gzip.Write(bytes, 0, bytes.Length);
        }
        var code = Convert.ToBase64String(buffer.ToArray())
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return "astral://room?code=" + code;
    }

    /// <summary>分享码没带节点时使用的公共中继（与 EasyTier / Astral 默认一致）。</summary>
    /// <summary>节点是否来自分享内容（false 表示用了公共中继兜底）。</summary>
    public static bool UsedFallbackPeers(AstralRoom room) =>
        room.Peers.Count == 2 &&
        room.Peers.SequenceEqual(DefaultPeers());

    public static List<string> DefaultPeers() =>
    [
        "tcp://public.easytier.top:11010",
        "tcp://public2.easytier.cn:54321"
    ];

    /// <summary>短码规范化：去空白/连字符、大写，并把 I/L→1、O→0。</summary>
    public static string NormalizeShortCode(string raw)
    {
        var builder = new StringBuilder();
        foreach (var ch in raw.Trim().ToUpperInvariant())
        {
            if (ch is '-' or ' ' or '_') continue;
            builder.Append(ch switch { 'I' or 'L' => '1', 'O' => '0', _ => ch });
        }
        return builder.ToString();
    }

    public static bool LooksLikeShortCode(string token)
    {
        var normalized = NormalizeShortCode(token);
        if (normalized.Length != 6) return false;
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        return normalized.All(alphabet.Contains);
    }

    /// <summary>AstralGame 的短链分享地址。</summary>
    public static string ShortLink(string code) => "https://next.astral.fan/j?c=" + Uri.EscapeDataString(code);

    private static bool TryParseOriginal(string token, out AstralRoom? room, out string? error)
    {
        room = null;
        error = null;
        if (!TryDecodeJson(token, out var json, out error)) return false;

        var name = ReadString(json, "n") ?? "Astral 房间";
        var networkName = ReadString(json, "r");
        var networkSecret = ReadString(json, "p") ?? "";
        var peers = ReadPeers(json, "s");
        var protectedFlag = ReadInt(json, "e") is > 0;

        if (string.IsNullOrWhiteSpace(networkName))
        {
            error = "分享码里缺少房间标识，可能不是 Astral 房间分享码。";
            return false;
        }
        if (peers.Count == 0) peers = DefaultPeers();

        room = new AstralRoom(name, networkName, networkSecret, peers, "Astral")
        {
            IsProtected = protectedFlag || networkSecret.Length > 0
        };
        return true;
    }

    private static bool TryParseAstralGame(string payload, out AstralRoom? room, out string? error)
    {
        room = null;
        error = null;
        if (!TryDecodeJson(payload, out var json, out error)) return false;

        var networkName = ReadString(json, "network_name");
        var networkSecret = ReadString(json, "network_secret");
        var peers = ReadPeers(json, "peers");
        var gameName = ReadString(json, "game_name");
        var display = ReadString(json, "display_name");

        if (string.IsNullOrWhiteSpace(networkName) || string.IsNullOrWhiteSpace(networkSecret))
        {
            error = "离线邀请里缺少网络名或密码，可能已损坏。";
            return false;
        }
        if (peers.Count == 0) peers = DefaultPeers();

        room = new AstralRoom(
            string.IsNullOrWhiteSpace(display) ? gameName ?? "Astral 房间" : display!,
            networkName, networkSecret, peers, "Astral Game")
        {
            IsProtected = networkSecret.Length > 0
        };
        return true;
    }

    private static bool TryDecodeJson(string token, out JsonElement json, out string? error)
    {
        json = default;
        error = null;
        byte[] bytes;
        try
        {
            var padded = token.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            bytes = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            error = "分享码不是有效的 Base64。";
            return false;
        }

        // 原版 Astral 是 gzip 压缩；离线邀请是明文 JSON，两种都试
        var text = Decompress(bytes) ?? Encoding.UTF8.GetString(bytes);
        try
        {
            using var document = JsonDocument.Parse(text);
            json = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            error = "分享码内容不是有效 JSON。";
            return false;
        }
    }

    private static string? Decompress(byte[] bytes)
    {
        if (bytes.Length < 2 || bytes[0] != 0x1F || bytes[1] != 0x8B) return null;
        try
        {
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    private static int? ReadInt(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    private static string? ReadString(JsonElement json, string name)
    {
        if (json.ValueKind != JsonValueKind.Object) return null;
        if (!json.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static List<string> ReadPeers(JsonElement json, string name)
    {
        var peers = new List<string>();
        if (json.ValueKind != JsonValueKind.Object ||
            !json.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Array)
            return peers;

        foreach (var item in value.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                    Add(item.GetString());
                    break;
                case JsonValueKind.Object:
                    foreach (var key in new[] { "uri", "url", "address", "addr" })
                    {
                        if (item.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String)
                        {
                            Add(field.GetString());
                            break;
                        }
                    }
                    break;
            }
        }
        return peers;

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !peers.Contains(value!)) peers.Add(value!);
        }
    }
}
