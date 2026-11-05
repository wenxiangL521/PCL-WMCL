using WMCL.Minecraft;

namespace WMCL.App.Services;

/// <summary>搜索用的版本选项：已安装 + 正式版完整列表（离线时只有已安装）。</summary>
public sealed class GameVersionOptions
{
    public List<string> Installed { get; } = [];
    public List<string> Releases { get; } = [];

    public bool IsLoaded { get; private set; }

    /// <summary>已安装在前、正式版在后，去重。</summary>
    public IEnumerable<string> All => Installed.Concat(Releases).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>把实例 id 解析成真实游戏版本（读 versions/&lt;id&gt; 下的版本 json）。</summary>
    public static string? ResolveGameVersion(VersionRepository repository, string? instanceId, string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return null;
        try
        {
            var directory = Path.Combine(gameRoot, "versions", instanceId);
            if (!Directory.Exists(directory)) return null;
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                var resolved = ResolveFromJson(file);
                if (!string.IsNullOrWhiteSpace(resolved)) return resolved;
            }
        }
        catch
        {
            // 忽略：解析失败就不设默认值
        }
        return null;
    }

    private static string? ResolveFromJson(string path)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            // CurseForge 整合包清单：{"minecraft":{"version":"1.21.1"}}
            if (root.TryGetProperty("minecraft", out var minecraft) &&
                minecraft.ValueKind == System.Text.Json.JsonValueKind.Object &&
                minecraft.TryGetProperty("version", out var mcVersion) &&
                mcVersion.ValueKind == System.Text.Json.JsonValueKind.String &&
                mcVersion.GetString() is { Length: > 0 } mcText && LooksLikeGameVersion(mcText))
                return mcText;
            // Prism/MultiMC：components 里 net.minecraft 的 version
            if (root.TryGetProperty("components", out var components) &&
                components.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in components.EnumerateArray())
                {
                    if (item.TryGetProperty("uid", out var uid) &&
                        uid.GetString() == "net.minecraft" &&
                        item.TryGetProperty("version", out var version) &&
                        version.GetString() is { Length: > 0 } versionText && LooksLikeGameVersion(versionText))
                        return versionText;
                }
            }
            // 常见字段：inheritsFrom / jar / clientVersion
            foreach (var name in new[] { "inheritsFrom", "jar", "clientVersion" })
            {
                if (!root.TryGetProperty(name, out var value)) continue;
                if (value.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                if (value.GetString() is { Length: > 0 } text && LooksLikeGameVersion(text)) return text;
            }
            // 退一步：文件名或 id 本身是版本号
            var name0 = Path.GetFileNameWithoutExtension(path);
            if (LooksLikeGameVersion(name0)) return name0;
            if (root.TryGetProperty("id", out var id) &&
                id.GetString() is { Length: > 0 } own && LooksLikeGameVersion(own)) return own;
        }
        catch
        {
            // 忽略损坏的版本文件
        }
        return null;
    }

    /// <summary>形如 1.20.1 / 1.21 / 26.3 / 1.7.10 的版本号。</summary>
    public static bool LooksLikeGameVersion(string text) =>
        System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+\.\d+(\.\d+)?$");

    public static async Task<GameVersionOptions> LoadAsync(VersionRepository repository, CancellationToken ct = default)
    {
        var options = new GameVersionOptions();
        try
        {
            foreach (var item in repository.ListInstalled())
                if (!string.IsNullOrWhiteSpace(item.Id)) options.Installed.Add(item.Id);
        }
        catch
        {
            // 忽略：仅用正式版列表
        }
        try
        {
            var manifest = await repository.GetManifestAsync(ct: ct).ConfigureAwait(false);
            options.Releases.AddRange(manifest.Versions
                .Where(version => string.Equals(version.Type, "release", StringComparison.OrdinalIgnoreCase))
                .Select(version => version.Id));
        }
        catch
        {
            // 离线：只保留已安装
        }
        options.IsLoaded = true;
        return options;
    }
}
