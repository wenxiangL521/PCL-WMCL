using System.Text.Json;
using WMCL.Core;

namespace WMCL.Link;

/// <summary>Astral 房间记录（加入过的外部房间）。</summary>
public sealed record AstralRoomEntry(
    string Name,
    string NetworkName,
    string NetworkSecret,
    List<string> Peers,
    string Source,
    DateTimeOffset LastUsed)
{
    public string Summary => $"{Peers.Count} 个节点 · {Source}";
}

/// <summary>中转 / 对等节点。</summary>
public sealed record AstralRelay(string Uri, string Name, bool Enabled)
{
    public string Display => string.IsNullOrWhiteSpace(Name) ? Uri : $"{Name}（{Uri}）";
}

/// <summary>Astral 页面配置：用户名、虚拟 IP、房间历史、中转节点。</summary>
public sealed class AstralProfile
{
    /// <summary>为空表示跟随启动器里的 MC 账号名。</summary>
    public string UsernameOverride { get; set; } = "";
    public string VirtualIp { get; set; } = "";
    public List<AstralRoomEntry> Rooms { get; set; } = [];
    public List<AstralRelay> Relays { get; set; } = [];

    public static AstralProfile CreateDefault()
    {
        return new AstralProfile
        {
            UsernameOverride = "",
            Relays =
            [
                new AstralRelay("tcp://public.easytier.top:11010", "公共中继 1", true),
                new AstralRelay("tcp://public2.easytier.cn:54321", "公共中继 2", true)
            ]
        };
    }
}

/// <summary>Astral 配置的读写（数据目录下的 astral.json）。</summary>
public static class AstralProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath => Path.Combine(WmclPaths.DataDirectory, "astral.json");

    public static AstralProfile Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return AstralProfile.CreateDefault();
            var profile = JsonSerializer.Deserialize<AstralProfile>(File.ReadAllText(FilePath), Options);
            return profile ?? AstralProfile.CreateDefault();
        }
        catch
        {
            return AstralProfile.CreateDefault();
        }
    }

    public static void Save(AstralProfile profile)
    {
        try
        {
            Directory.CreateDirectory(WmclPaths.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(profile, Options));
        }
        catch
        {
            // 配置写入失败不影响使用
        }
    }
}
