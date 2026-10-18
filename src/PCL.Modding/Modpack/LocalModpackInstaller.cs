using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCL.Core.Jobs;
using PCL.Download;
using PCL.Minecraft;
using PCL.Modding.Fabric;
using PCL.Modding.Forgelike;

namespace PCL.Modding.Modpack;

public enum LocalModpackKind
{
    Unknown,
    Mmc,
    Hmcl,
    Mcbbs
}

public sealed record LocalModpackInfo(
    LocalModpackKind Kind,
    string BaseFolder,
    string Name,
    string Minecraft,
    string? FabricLoader,
    string? Forge,
    string? NeoForge,
    string OverridesFolder);

public sealed record LocalPackSettings(
    string PreLaunchCommand,
    string JvmArgs,
    string GameArgs,
    string Server,
    int? Port);

/// <summary>
/// MMC / HMCL / MCBBS 本地整合包：识别 → 解析组件 → 安装加载器 → 解压游戏目录覆写。
/// 对照 PCL2 InstallPackMMC / InstallPackHMCL / InstallPackMCBBS 的客户端部分。
/// </summary>
public sealed class LocalModpackInstaller
{
    private readonly VersionRepository _repository;
    private readonly VersionInstaller _installer;
    private readonly FabricInstaller _fabric;
    private readonly ForgelikeInstaller _forgelike;
    private readonly Func<int, CancellationToken, Task<string>>? _javaResolver;
    private readonly string? _isolationMode;

    public LocalModpackInstaller(
        VersionRepository repository,
        VersionInstaller? installer = null,
        FabricInstaller? fabric = null,
        ForgelikeInstaller? forgelike = null,
        Func<int, CancellationToken, Task<string>>? javaResolver = null,
        string? isolationMode = null)
    {
        _repository = repository;
        _installer = installer ?? new VersionInstaller(repository);
        _fabric = fabric ?? new FabricInstaller(repository, _installer);
        _forgelike = forgelike ?? new ForgelikeInstaller(repository, _installer);
        _javaResolver = javaResolver;
        _isolationMode = isolationMode;
    }

    public static LocalModpackKind Detect(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return Detect(archive);
        }
        catch
        {
            return LocalModpackKind.Unknown;
        }
    }

    public static LocalModpackKind Detect(ZipArchive archive)
    {
        if (FindEntry(archive, "mcbbs.packmeta") is not null) return LocalModpackKind.Mcbbs;
        if (FindEntry(archive, "mmc-pack.json") is not null) return LocalModpackKind.Mmc;
        if (FindEntry(archive, "modpack.json") is not null) return LocalModpackKind.Hmcl;
        var manifest = FindEntry(archive, "manifest.json");
        if (manifest is not null)
        {
            using var stream = manifest.Open();
            using var reader = new StreamReader(stream);
            try
            {
                using var document = JsonDocument.Parse(reader.ReadToEnd());
                if (document.RootElement.TryGetProperty("addons", out _))
                    return LocalModpackKind.Mcbbs;
            }
            catch (JsonException)
            {
                // 不是 MCBBS
            }
        }
        return LocalModpackKind.Unknown;
    }

    public static LocalModpackInfo Parse(ZipArchive archive, LocalModpackKind kind)
    {
        return kind switch
        {
            LocalModpackKind.Mmc => ParseMmc(archive),
            LocalModpackKind.Hmcl => ParseHmcl(archive),
            LocalModpackKind.Mcbbs => ParseMcbbs(archive),
            _ => throw new InvalidDataException("无法识别整合包格式。")
        };
    }

    public async Task<string> InstallFromFileAsync(
        string path,
        string? instanceName = null,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        using var archive = ZipFile.OpenRead(path);
        var kind = Detect(archive);
        if (kind == LocalModpackKind.Unknown)
            throw new InvalidDataException("不是支持的 MMC/HMCL/MCBBS 整合包。");
        var info = Parse(archive, kind);
        progress?.Report(new JobProgress("解析本地整合包", 0.02));

        var id = CreateInstanceId(instanceName ?? info.Name);
        var sourceId = "";
        var deleteSource = false;
        string loaderName;
        string loaderVersion;
        if (!string.IsNullOrWhiteSpace(info.FabricLoader))
        {
            loaderName = "fabric";
            loaderVersion = info.FabricLoader!;
            progress?.Report(new JobProgress("安装 Fabric 加载器", 0.06));
            var loaderProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                update.Stage, 0.06 + (update.Percent ?? 0) * 0.35, null, null, update.Detail)));
            sourceId = await _fabric.InstallAsync(
                info.Minecraft, loaderVersion, loaderProgress, ct).ConfigureAwait(false);
            deleteSource = true;
        }
        else if (!string.IsNullOrWhiteSpace(info.Forge) || !string.IsNullOrWhiteSpace(info.NeoForge))
        {
            if (_javaResolver is null)
                throw new InvalidOperationException("安装 Forge/NeoForge 整合包需要 Java。");
            var isNeo = !string.IsNullOrWhiteSpace(info.NeoForge);
            loaderName = isNeo ? "neoforge" : "forge";
            loaderVersion = (isNeo ? info.NeoForge : info.Forge)!;
            var java = await _javaResolver(InferJavaMajor(info.Minecraft), ct).ConfigureAwait(false);
            progress?.Report(new JobProgress("安装 Forge/NeoForge 加载器", 0.06));
            var loaderProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                update.Stage, 0.06 + (update.Percent ?? 0) * 0.35, null, null, update.Detail)));
            sourceId = await _forgelike.InstallAsync(
                isNeo ? ForgelikeKind.NeoForge : ForgelikeKind.Forge,
                info.Minecraft, loaderVersion, java, loaderProgress, null, ct).ConfigureAwait(false);
            deleteSource = true;
        }
        else
        {
            loaderName = "vanilla";
            loaderVersion = "";
            progress?.Report(new JobProgress("安装原版游戏", 0.06));
            await _installer.InstallAsync(
                info.Minecraft, new InstallOptions { Mirror = MirrorMode.Auto },
                progress, ct).ConfigureAwait(false);
            sourceId = info.Minecraft;
        }

        if (sourceId == info.Minecraft && !deleteSource)
        {
            progress?.Report(new JobProgress("创建整合包实例", 0.45));
            await MergedInstanceBuilder.BuildAsync(_repository.GameDirectory, sourceId, id, ct)
                .ConfigureAwait(false);
        }
        else
        {
            progress?.Report(new JobProgress("创建整合包实例", 0.45));
            await MergedInstanceBuilder.BuildAsync(_repository.GameDirectory, sourceId, id, ct)
                .ConfigureAwait(false);
            if (deleteSource) InstanceManager.Delete(_repository.GameDirectory, sourceId);
        }

        await _installer.InstallAsync(
            id, new InstallOptions { Mirror = MirrorMode.Auto }, progress, ct).ConfigureAwait(false);
        var version = await _repository.GetVersionAsync(id, ct).ConfigureAwait(false);
        var gameDirectory = IsolationPolicy.GetGameDirectory(
            _repository.GameDirectory, id, version, _isolationMode);

        progress?.Report(new JobProgress("解压整合包文件", 0.62));
        ExtractGameFiles(archive, info, gameDirectory, ct);
        ApplyPackSettings(archive, info, _repository.GetVersionDirectory(id));

        InstallManifestStore.Save(_repository.GameDirectory, id, new InstallManifest
        {
            Components =
            {
                ["minecraft"] = info.Minecraft,
                [loaderName] = loaderVersion,
                ["local-pack"] = info.Kind.ToString()
            }
        });
        progress?.Report(new JobProgress("整合包安装完成", 1));
        return id;
    }

    public static LocalPackSettings ParseMmcSettings(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var index = line.IndexOf('=');
            if (index <= 0) continue;
            values[line[..index].Trim()] = line[(index + 1)..].Trim();
        }

        bool Enabled(string key) =>
            values.TryGetValue(key, out var value) &&
            (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));

        var preLaunch = Enabled("OverrideCommands")
            ? values.GetValueOrDefault("PreLaunchCommand", "") ?? ""
            : "";
        preLaunch = preLaunch
            .Replace("\\\"", "\"")
            .Replace("$INST_JAVA", "{java}")
            .Replace("$INST_MC_DIR\\", "{minecraft}")
            .Replace("$INST_MC_DIR", "{minecraft}")
            .Replace("$INST_DIR\\", "{verpath}")
            .Replace("$INST_DIR", "{verpath}")
            .Replace("$INST_ID", "{name}")
            .Replace("$INST_NAME", "{name}");

        var jvm = Enabled("OverrideJavaArgs")
            ? values.GetValueOrDefault("JvmArgs", "") ?? ""
            : "";
        var server = "";
        int? port = null;
        if (Enabled("JoinServerOnLaunch"))
        {
            server = (values.GetValueOrDefault("JoinServerOnLaunchAddress", "") ?? "").Trim('"');
            var split = server.LastIndexOf(':');
            if (split > 0 && int.TryParse(server[(split + 1)..], out var parsed))
            {
                port = parsed;
                server = server[..split];
            }
        }
        return new LocalPackSettings(preLaunch, jvm, "", server, port);
    }

    public static LocalPackSettings ParseMcbbsLaunchInfo(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("launchInfo", out var info) ||
            info.ValueKind != JsonValueKind.Object)
            return new LocalPackSettings("", "", "", "", null);

        string Join(string name)
        {
            if (!info.TryGetProperty(name, out var array) ||
                array.ValueKind != JsonValueKind.Array) return "";
            return string.Join(" ", array.EnumerateArray()
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        return new LocalPackSettings("", Join("javaArgument"), Join("launchArgument"), "", null);
    }

    private static void ApplyPackSettings(
        ZipArchive archive, LocalModpackInfo info, string instanceDirectory)
    {
        LocalPackSettings packSettings;
        if (info.Kind == LocalModpackKind.Mmc)
        {
            var cfg = FindEntry(archive, info.BaseFolder + "instance.cfg");
            if (cfg is null) return;
            using var stream = cfg.Open();
            using var reader = new StreamReader(stream);
            packSettings = ParseMmcSettings(
                reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
        else if (info.Kind == LocalModpackKind.Mcbbs)
        {
            var entry = FindEntry(archive, "mcbbs.packmeta") ?? FindEntry(archive, "manifest.json");
            if (entry is null) return;
            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            packSettings = ParseMcbbsLaunchInfo(document.RootElement);
        }
        else
        {
            return;
        }

        var settings = InstanceSettingsStore.Load(instanceDirectory);
        if (!string.IsNullOrWhiteSpace(packSettings.PreLaunchCommand))
        {
            settings.PreLaunchCommand = packSettings.PreLaunchCommand;
            settings.PreLaunchWait = true;
        }
        if (!string.IsNullOrWhiteSpace(packSettings.JvmArgs))
            settings.ExtraJvmArgs = packSettings.JvmArgs;
        if (!string.IsNullOrWhiteSpace(packSettings.GameArgs))
            settings.ExtraGameArgs = packSettings.GameArgs;
        if (!string.IsNullOrWhiteSpace(packSettings.Server))
        {
            settings.Server = packSettings.Server;
            settings.Port = packSettings.Port;
        }
        InstanceSettingsStore.Save(instanceDirectory, settings);
    }

    private static LocalModpackInfo ParseMmc(ZipArchive archive)
    {
        var jsonEntry = FindEntry(archive, "mmc-pack.json")
                        ?? throw new InvalidDataException("MMC 整合包缺少 mmc-pack.json。");
        var baseFolder = BaseFolder(archive, jsonEntry);
        string name = "MMC Pack";
        var instanceCfg = FindEntry(archive, "instance.cfg");
        if (instanceCfg is not null)
        {
            using var stream = instanceCfg.Open();
            using var reader = new StreamReader(stream);
            var match = Regex.Match(reader.ReadToEnd(), @"(?m)^name=(.+)$");
            if (match.Success) name = match.Groups[1].Value.Trim();
        }

        using var document = ParseEntry(jsonEntry);
        string? minecraft = null, forge = null, neo = null, fabric = null;
        foreach (var component in document.RootElement.GetProperty("components").EnumerateArray())
        {
            var uid = component.TryGetProperty("uid", out var uidValue) ? uidValue.GetString() : null;
            var version = component.TryGetProperty("version", out var versionValue) ? versionValue.GetString() : null;
            switch (uid)
            {
                case "net.minecraft": minecraft = version; break;
                case "net.minecraftforge": forge = version; break;
                case "net.neoforged": neo = version; break;
                case "net.fabricmc.fabric-loader": fabric = version; break;
            }
        }

        var overrides = FindEntry(archive, baseFolder + "minecraft/") is not null ||
                        HasDirectory(archive, baseFolder + "minecraft/")
            ? baseFolder + "minecraft/"
            : HasDirectory(archive, baseFolder + ".minecraft/")
                ? baseFolder + ".minecraft/"
                : "";
        return new LocalModpackInfo(
            LocalModpackKind.Mmc, baseFolder, name,
            minecraft ?? throw new InvalidDataException("MMC 整合包缺少 net.minecraft 组件。"),
            fabric, forge, neo, overrides);
    }

    private static LocalModpackInfo ParseHmcl(ZipArchive archive)
    {
        var entry = FindEntry(archive, "modpack.json")
                    ?? throw new InvalidDataException("HMCL 整合包缺少 modpack.json。");
        using var document = ParseEntry(entry);
        var root = document.RootElement;
        var name = root.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
        var game = root.TryGetProperty("gameVersion", out var gameValue) ? gameValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(game))
            throw new InvalidDataException("HMCL 整合包缺少 gameVersion。");
        return new LocalModpackInfo(
            LocalModpackKind.Hmcl, BaseFolder(archive, entry), name ?? "HMCL Pack", game,
            null, null, null, BaseFolder(archive, entry) + "minecraft/");
    }

    private static LocalModpackInfo ParseMcbbs(ZipArchive archive)
    {
        var entry = FindEntry(archive, "mcbbs.packmeta") ?? FindEntry(archive, "manifest.json")
                    ?? throw new InvalidDataException("MCBBS 整合包缺少 packmeta/manifest。");
        using var document = ParseEntry(entry);
        var root = document.RootElement;
        var name = root.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
        if (!root.TryGetProperty("addons", out var addons))
            throw new InvalidDataException("MCBBS 整合包缺少 addons。");
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (addons.ValueKind == JsonValueKind.Array)
        {
            foreach (var addon in addons.EnumerateArray())
            {
                var id = addon.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
                var version = addon.TryGetProperty("version", out var versionValue) ? versionValue.GetString() : null;
                if (id is not null && version is not null) map[id] = version;
            }
        }
        else
        {
            foreach (var addon in addons.EnumerateObject())
                map[addon.Name] = addon.Value.GetString() ?? "";
        }
        if (!map.TryGetValue("game", out var game))
            throw new InvalidDataException("MCBBS 整合包缺少 game 版本。");
        var baseFolder = BaseFolder(archive, entry);
        return new LocalModpackInfo(
            LocalModpackKind.Mcbbs, baseFolder, name ?? "MCBBS Pack", game,
            map.GetValueOrDefault("fabric"), map.GetValueOrDefault("forge"),
            map.GetValueOrDefault("neoforge"), baseFolder + "overrides/");
    }

    private static JsonDocument ParseEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return JsonDocument.Parse(reader.ReadToEnd());
    }

    private static JsonDocument ParseEntry(ZipArchive archive, string name) =>
        ParseEntry(FindEntry(archive, name)
                   ?? throw new InvalidDataException($"整合包缺少 {name}。"));

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string suffix) =>
        archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName, suffix, StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.EndsWith("/" + suffix, StringComparison.OrdinalIgnoreCase));

    private static string BaseFolder(ZipArchive archive, ZipArchiveEntry entry)
    {
        var index = entry.FullName.LastIndexOf('/');
        return index < 0 ? "" : entry.FullName[..(index + 1)];
    }

    private static bool HasDirectory(ZipArchive archive, string prefix) =>
        archive.Entries.Any(entry => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static void ExtractGameFiles(
        ZipArchive archive, LocalModpackInfo info, string gameDirectory, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(info.OverridesFolder)) return;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/') ||
                !entry.FullName.StartsWith(info.OverridesFolder, StringComparison.OrdinalIgnoreCase))
                continue;
            var relative = entry.FullName[info.OverridesFolder.Length..];
            var destination = SafePath(gameDirectory, relative);
            if (destination is null) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private static string? SafePath(string root, string relative)
    {
        var normalized = relative.Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0 || normalized.Split('/').Contains("..")) return null;
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        return full.StartsWith(rootFull, StringComparison.Ordinal) ? full : null;
    }

    private static int InferJavaMajor(string minecraft)
    {
        var parts = minecraft.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var minor)) return 21;
        var patch = parts.Length >= 3 && int.TryParse(parts[2], out var value) ? value : 0;
        if (minor > 20 || (minor == 20 && patch >= 5)) return 21;
        if (minor >= 17) return 17;
        if (minor >= 12) return 11;
        return 8;
    }

    private string CreateInstanceId(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(raw.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim('-', ' ');
        if (string.IsNullOrWhiteSpace(name)) name = "LocalPack";
        if (name.Length > 64) name = name[..64];
        var candidate = name;
        var suffix = 2;
        while (Directory.Exists(Path.Combine(_repository.VersionsDirectory, candidate)))
            candidate = name + "-" + suffix++;
        return candidate;
    }

    private sealed class ProgressAdapter(Action<JobProgress> action) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => action(value);
    }
}
