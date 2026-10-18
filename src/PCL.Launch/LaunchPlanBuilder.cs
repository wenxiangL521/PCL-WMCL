using PCL.Core;
using PCL.Launch.Java;
using PCL.Minecraft;
using PCL.Minecraft.Models;

namespace PCL.Launch;

public sealed class LaunchRequest
{
    public required VersionJson Version { get; init; }
    public required string VersionId { get; init; }
    public required string GameDirectory { get; init; }
    /// <summary>Minecraft 根目录（assets/libraries/versions）。为空时与 GameDirectory 相同。</summary>
    public string? MinecraftDirectory { get; init; }
    public required JavaRuntime Java { get; init; }
    public required AccountInfo Account { get; init; }
    public int MemoryMb { get; init; } = 4096;
    public int? Width { get; init; }
    public int? Height { get; init; }
    public IReadOnlyList<string> ExtraJvmArguments { get; init; } = [];
    public IReadOnlyList<string> ExtraGameArguments { get; init; } = [];
    public string LauncherName { get; init; } = "PCL Linux";
    public string LauncherVersion { get; init; } = "0.1.0";
}

public sealed record LaunchPlan(
    string JavaPath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    string Classpath,
    string NativesDirectory,
    string AssetsDirectory)
{
    public string Preview => $"{JavaPath} {string.Join(' ', Arguments.Select(Quote))}";

    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;
}

public static class LaunchPlanBuilder
{
    public static LaunchPlan Build(LaunchRequest request)
    {
        var version = request.Version;
        var gameDirectory = PclPaths.Expand(request.GameDirectory);
        var minecraftDirectory = PclPaths.Expand(
            string.IsNullOrWhiteSpace(request.MinecraftDirectory)
                ? request.GameDirectory
                : request.MinecraftDirectory);
        var assetsDirectory = Path.Combine(minecraftDirectory, "assets");
        var nativesDirectory = Path.Combine(minecraftDirectory, "versions", request.VersionId, "natives");
        var clientJar = Path.Combine(minecraftDirectory, "versions", request.VersionId, request.VersionId + ".jar");

        var features = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["is_demo_user"] = false,
            ["has_custom_resolution"] = request.Width is not null && request.Height is not null,
            ["has_quick_plays_support"] = false,
            ["is_quick_play_singleplayer"] = false,
            ["is_quick_play_multiplayer"] = false,
            ["is_quick_play_realms"] = false
        };
        var context = RuleContext.CreateDefault(features);

        var libraries = LibraryResolver.ResolveArtifacts(version, context);
        var classpathEntries = libraries
            .Select(library => Path.Combine(
                minecraftDirectory, "libraries",
                library.RelativePath.Replace('/', Path.DirectorySeparatorChar)))
            .ToList();
        if (File.Exists(clientJar)) classpathEntries.Add(clientJar);
        var classpath = string.Join(Path.PathSeparator, classpathEntries);

        var placeholders = BuildPlaceholders(request, gameDirectory, minecraftDirectory,
            assetsDirectory, nativesDirectory, classpath);

        var jvm = new List<string>
        {
            $"-Xmx{request.MemoryMb}M",
            $"-Xms{Math.Min(request.MemoryMb, 1024)}M"
        };

        if (version.Arguments?.Jvm is { Count: > 0 })
        {
            AppendArguments(jvm, version.Arguments.Jvm, context, placeholders);
        }

        EnsureJvmArgument(jvm, "-Djava.library.path=", $"-Djava.library.path={nativesDirectory}");
        EnsureJvmArgument(jvm, "-Dminecraft.launcher.brand=", $"-Dminecraft.launcher.brand={request.LauncherName}");
        EnsureJvmArgument(jvm, "-Dminecraft.launcher.version=",
            $"-Dminecraft.launcher.version={request.LauncherVersion}");

        if (!jvm.Contains("-cp") && !jvm.Contains("-classpath"))
        {
            jvm.Add("-cp");
            jvm.Add(classpath);
        }

        if (version.Logging?.Client is { } logging &&
            !string.IsNullOrEmpty(logging.File?.Id))
        {
            var logConfig = Path.Combine(assetsDirectory, "log_configs", logging.File.Id);
            EnsureJvmArgument(jvm, "-Dlog4j.configurationFile=",
                $"-Dlog4j.configurationFile={logConfig}");
        }

        jvm.AddRange(request.ExtraJvmArguments);

        if (string.IsNullOrWhiteSpace(version.MainClass))
            throw new InvalidOperationException($"版本 {request.VersionId} 没有 mainClass。");

        var game = BuildGameArguments(request, version, context, placeholders);
        game.AddRange(request.ExtraGameArguments);

        var arguments = new List<string>(jvm.Count + game.Count + 1)
        {
            version.MainClass
        };
        arguments.AddRange(game);

        return new LaunchPlan(
            request.Java.Path,
            gameDirectory,
            [.. jvm, .. arguments],
            classpath,
            nativesDirectory,
            assetsDirectory);
    }

    private static Dictionary<string, string> BuildPlaceholders(
        LaunchRequest request,
        string gameDirectory,
        string minecraftDirectory,
        string assetsDirectory,
        string nativesDirectory,
        string classpath)
    {
        var version = request.Version;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = request.Account.Name,
            ["version_name"] = request.VersionId,
            ["game_directory"] = gameDirectory,
            ["assets_root"] = assetsDirectory,
            ["assets_index_name"] = version.AssetIndex?.Id ?? version.Assets ?? "legacy",
            ["auth_uuid"] = request.Account.Uuid,
            ["auth_access_token"] = request.Account.AccessToken,
            ["auth_session"] = $"token:{request.Account.AccessToken}:{request.Account.Uuid}",
            ["clientid"] = request.Account.ClientId ?? "",
            ["auth_xuid"] = request.Account.Xuid ?? "",
            ["user_type"] = request.Account.UserType,
            ["version_type"] = version.Type ?? "release",
            ["natives_directory"] = nativesDirectory,
            ["library_directory"] = Path.Combine(minecraftDirectory, "libraries"),
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["launcher_name"] = request.LauncherName,
            ["launcher_version"] = request.LauncherVersion,
            ["classpath"] = classpath,
            ["resolution_width"] = (request.Width ?? 854).ToString(),
            ["resolution_height"] = (request.Height ?? 480).ToString(),
            ["user_properties"] = "{}",
            ["game_assets"] = Path.Combine(assetsDirectory, "virtual", "legacy")
        };
    }

    private static void AppendArguments(
        List<string> target,
        IEnumerable<ArgumentEntry> entries,
        RuleContext context,
        IReadOnlyDictionary<string, string> placeholders)
    {
        foreach (var entry in entries)
        {
            if (!entry.IsLiteral && !RuleEvaluator.IsAllowed(entry.Rules, context))
                continue;

            foreach (var value in entry.Values)
                target.Add(Replace(value, placeholders));
        }
    }

    private static List<string> BuildGameArguments(
        LaunchRequest request,
        VersionJson version,
        RuleContext context,
        IReadOnlyDictionary<string, string> placeholders)
    {
        var result = new List<string>();

        if (version.Arguments?.Game is { Count: > 0 })
        {
            AppendArguments(result, version.Arguments.Game, context, placeholders);
        }
        else if (!string.IsNullOrWhiteSpace(version.MinecraftArguments))
        {
            foreach (var token in version.MinecraftArguments.Split(
                         ' ', StringSplitOptions.RemoveEmptyEntries))
                result.Add(Replace(token, placeholders));
        }
        else
        {
            throw new InvalidOperationException($"版本 {request.VersionId} 没有游戏参数。");
        }

        if (request.Width is not null && request.Height is not null)
        {
            if (!result.Contains("--width"))
            {
                result.Add("--width");
                result.Add(request.Width.Value.ToString());
            }
            if (!result.Contains("--height"))
            {
                result.Add("--height");
                result.Add(request.Height.Value.ToString());
            }
        }

        return result;
    }

    private static string Replace(string value, IReadOnlyDictionary<string, string> placeholders)
    {
        foreach (var (key, replacement) in placeholders)
            value = value.Replace("${" + key + "}", replacement, StringComparison.Ordinal);
        return value;
    }

    private static void EnsureJvmArgument(List<string> jvm, string prefix, string argument)
    {
        if (jvm.Any(a => a.StartsWith(prefix, StringComparison.Ordinal))) return;
        jvm.Add(argument);
    }
}
