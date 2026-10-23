using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WMCL.Core.IO;
using WMCL.Minecraft;

namespace WMCL.Modding.LiteLoader;

public sealed record LiteLoaderVersion(
    string Minecraft,
    string Version,
    string TweakClass,
    bool IsPreview,
    DateTimeOffset? Published,
    string? Md5,
    JsonElement Libraries)
{
    public bool IsLegacy => Minecraft.StartsWith("1.7", StringComparison.Ordinal);
    public string InstallerFileName => "liteloader-installer-" + Minecraft +
        (Minecraft is "1.8" or "1.9" ? ".0" : "") + "-00-SNAPSHOT.jar";
}

public sealed class LiteLoaderService
{
    private static readonly HttpClient DefaultHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly string[] Sources =
    [
        "https://dl.liteloader.com/versions/versions.json",
        "https://bmclapi2.bangbang93.com/maven/com/mumfrey/liteloader/versions.json"
    ];

    private readonly HttpClient _http;

    public LiteLoaderService(HttpClient? http = null) => _http = http ?? DefaultHttp;

    public static bool SupportsMinecraft(string minecraft) =>
        Regex.IsMatch(minecraft, @"^1\.(7(?:\.[0-9]+)?|8(?:\.[0-9]+)?|9(?:\.[0-9]+)?|10(?:\.[0-9]+)?|11(?:\.[0-9]+)?|12(?:\.[0-2])?)$");

    public static IReadOnlyList<LiteLoaderVersion> ParseVersions(string json, string minecraft)
    {
        if (!SupportsMinecraft(minecraft)) return [];
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("LiteLoader 版本清单缺少 versions。");
        if (!versions.TryGetProperty(minecraft, out var game)) return [];
        if (game.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("LiteLoader 游戏版本元数据无效。");
        var catalog = game.TryGetProperty("artefacts", out var artefacts) && artefacts.ValueKind != JsonValueKind.Null
            ? artefacts : game.TryGetProperty("snapshots", out var snapshots) ? snapshots : default;
        if (catalog.ValueKind != JsonValueKind.Object ||
            !catalog.TryGetProperty("com.mumfrey:liteloader", out var package) || package.ValueKind != JsonValueKind.Object ||
            !package.TryGetProperty("latest", out var latest) || latest.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("LiteLoader 版本元数据缺少 latest。");

        var version = RequiredString(latest, "version");
        if (!Regex.IsMatch(version, @"^[A-Za-z0-9_.+-]+$"))
            throw new InvalidDataException("LiteLoader 版本号无效。");
        var tweakClass = RequiredString(latest, "tweakClass");
        if (!Regex.IsMatch(tweakClass, @"^[A-Za-z_$][A-Za-z0-9_$]*(\.[A-Za-z_$][A-Za-z0-9_$]*)+$"))
            throw new InvalidDataException("LiteLoader tweakClass 无效。");
        if (!latest.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Array ||
            libraries.EnumerateArray().Any(library => library.ValueKind != JsonValueKind.Object ||
                !library.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(name.GetString())))
            throw new InvalidDataException("LiteLoader 支持库清单无效。");

        DateTimeOffset? published = null;
        if (latest.TryGetProperty("timestamp", out var timestamp) &&
            long.TryParse(timestamp.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            try { published = DateTimeOffset.FromUnixTimeSeconds(seconds); }
            catch (ArgumentOutOfRangeException) { }
        }
        return [new LiteLoaderVersion(minecraft, version, tweakClass,
            OptionalString(latest, "stream")?.Equals("snapshot", StringComparison.OrdinalIgnoreCase) == true,
            published, OptionalString(latest, "md5"), libraries.Clone())];
    }

    public async Task<IReadOnlyList<LiteLoaderVersion>> GetVersionsAsync(string minecraft, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!SupportsMinecraft(minecraft)) return [];
        var failures = new List<Exception>();
        foreach (var source in Sources)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                var json = await _http.GetStringAsync(source, timeout.Token).ConfigureAwait(false);
                return ParseVersions(json, minecraft);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException or OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                failures.Add(exception);
            }
        }
        throw new AggregateException("获取 LiteLoader 版本失败。", failures);
    }

    public static string CreateProfile(LiteLoaderVersion version, string? parentInstanceId = null)
    {
        if (!SupportsMinecraft(version.Minecraft))
            throw new InvalidOperationException("LiteLoader 仅支持 Minecraft 1.7–1.12.2。");
        if (!Regex.IsMatch(version.Version, @"^[A-Za-z0-9_.+-]+$") ||
            !Regex.IsMatch(version.TweakClass, @"^[A-Za-z_$][A-Za-z0-9_$]*(\.[A-Za-z_$][A-Za-z0-9_$]*)+$"))
            throw new InvalidDataException("LiteLoader 版本号或 tweakClass 无效。");
        parentInstanceId ??= version.Minecraft;
        InstanceManager.ValidateName(parentInstanceId);
        if (parentInstanceId == version.Minecraft + "-LiteLoader")
            throw new InvalidOperationException("LiteLoader 不能继承自身。");
        var libraries = JsonNode.Parse(version.Libraries.GetRawText()) as JsonArray
            ?? throw new InvalidDataException("LiteLoader 支持库清单无效。");
        libraries.Add(new JsonObject
        {
            ["name"] = "com.mumfrey:liteloader:" + version.Version,
            ["url"] = "https://dl.liteloader.com/versions/"
        });
        var profile = new JsonObject
        {
            ["id"] = version.Minecraft + "-LiteLoader",
            ["type"] = "release",
            ["arguments"] = new JsonObject
            {
                ["game"] = new JsonArray("--tweakClass", version.TweakClass)
            },
            ["libraries"] = libraries,
            ["mainClass"] = "net.minecraft.launchwrapper.Launch",
            ["minimumLauncherVersion"] = 18,
            ["inheritsFrom"] = parentInstanceId,
            ["jar"] = version.Minecraft
        };
        if (version.Published is { } published)
        {
            profile["time"] = published.ToString("O", CultureInfo.InvariantCulture);
            profile["releaseTime"] = published.ToString("O", CultureInfo.InvariantCulture);
        }
        return profile.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static async Task<string> InstallProfileAsync(string stagingRoot, LiteLoaderVersion version,
        CancellationToken ct = default, string? parentInstanceId = null)
    {
        ct.ThrowIfCancellationRequested();
        var profile = CreateProfile(version, parentInstanceId);
        var instanceId = version.Minecraft + "-LiteLoader";
        InstanceManager.ValidateName(instanceId);
        var directory = FileUtil.SafeChildPath(Path.Combine(stagingRoot, "versions"), instanceId);
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new IOException("LiteLoader 暂存实例已存在：" + instanceId);
        parentInstanceId ??= version.Minecraft;
        var parent = FileUtil.SafeChildPath(Path.Combine(stagingRoot, "versions"), parentInstanceId);
        if (!File.Exists(Path.Combine(parent, parentInstanceId + ".json")))
            throw new FileNotFoundException("安装 LiteLoader 前需要准备原版 profile。");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, instanceId + ".json");
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(profile.AsMemory(), ct).ConfigureAwait(false);
        return instanceId;
    }

    private static string RequiredString(JsonElement element, string name) =>
        OptionalString(element, name) is { Length: > 0 } value ? value
            : throw new InvalidDataException("LiteLoader 版本元数据缺少 " + name + "。");

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
