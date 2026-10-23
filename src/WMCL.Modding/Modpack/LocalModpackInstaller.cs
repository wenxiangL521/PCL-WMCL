using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Minecraft;
using WMCL.Minecraft.Models;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Fabric;
using WMCL.Modding.Forgelike;

namespace WMCL.Modding.Modpack;

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
    string OverridesFolder,
    string? QuiltLoader = null);

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
    private readonly FabricInstaller _quilt;
    private readonly ForgelikeInstaller _forgelike;
    private readonly CurseForgeClient _curseForge;
    private readonly DownloadManager _downloads;
    private readonly Func<int, CancellationToken, Task<string>>? _javaResolver;
    private readonly Func<ModpackJavaRequest, CancellationToken, Task<string>>? _javaRequirementResolver;

    public LocalModpackInstaller(
        VersionRepository repository,
        VersionInstaller? installer = null,
        FabricInstaller? fabric = null,
        ForgelikeInstaller? forgelike = null,
        Func<int, CancellationToken, Task<string>>? javaResolver = null,
        string? isolationMode = null,
        FabricInstaller? quilt = null,
        CurseForgeClient? curseForge = null,
        DownloadManager? downloads = null,
        Func<ModpackJavaRequest, CancellationToken, Task<string>>? javaRequirementResolver = null)
    {
        _repository = repository;
        _installer = installer ?? new VersionInstaller(repository);
        _fabric = fabric ?? new FabricInstaller(repository, _installer);
        _quilt = quilt ?? new FabricInstaller(repository, _installer, FabricFamilyKind.Quilt);
        _forgelike = forgelike ?? new ForgelikeInstaller(repository, _installer);
        _curseForge = curseForge ?? new CurseForgeClient();
        _downloads = downloads ?? new DownloadManager();
        _javaResolver = javaResolver;
        _javaRequirementResolver = javaRequirementResolver;
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
        => FindMetadata(archive)?.Kind ?? LocalModpackKind.Unknown;

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
        ct.ThrowIfCancellationRequested();
        using var archive = ZipFile.OpenRead(path);
        var kind = Detect(archive);
        if (kind == LocalModpackKind.Unknown)
            throw new InvalidDataException("不是支持的 MMC/HMCL/MCBBS 整合包。");
        var info = Parse(archive, kind);
        progress?.Report(new JobProgress("解析本地整合包", 0.02));

        var id = CreateInstanceId(instanceName ?? info.Name);
        if (id.Equals(info.Minecraft, StringComparison.OrdinalIgnoreCase)) id = CreateInstanceId(id + "-Pack");
        ModpackContentPaths.ValidateOverrides(archive, info.OverridesFolder, _repository.GetVersionDirectory(id), id);
        var mcbbs = kind == LocalModpackKind.Mcbbs
            ? await McbbsPackContent.PlanAsync(archive, MetadataEntry(archive, kind), info,
                _repository.GetVersionDirectory(id), id, _curseForge, ct).ConfigureAwait(false)
            : null;
        var content = mcbbs?.Files ?? [];
        MmcModpackMetadata? mmc = null;
        if (kind == LocalModpackKind.Mmc)
        {
            using var metadata = ParseEntry(MetadataEntry(archive, kind));
            mmc = MmcModpackMetadata.Read(archive, metadata.RootElement, info.BaseFolder);
            await mmc.ValidateBundledFilesAsync(ct).ConfigureAwait(false);
        }
        string loaderName;
        string loaderVersion;
        if (!string.IsNullOrWhiteSpace(info.FabricLoader) || !string.IsNullOrWhiteSpace(info.QuiltLoader))
        {
            var isQuilt = !string.IsNullOrWhiteSpace(info.QuiltLoader);
            loaderName = isQuilt ? "quilt" : "fabric";
            loaderVersion = (isQuilt ? info.QuiltLoader : info.FabricLoader)!;
            progress?.Report(new JobProgress($"安装 {(isQuilt ? "Quilt" : "Fabric")} 加载器", 0.06));
            var loaderProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                update.Stage, 0.06 + (update.Percent ?? 0) * 0.35, null, null, update.Detail)));
            await (isQuilt ? _quilt : _fabric).InstallMergedAsync(
                info.Minecraft, loaderVersion, id, loaderProgress, ct).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(info.Forge) || !string.IsNullOrWhiteSpace(info.NeoForge))
        {
            var isNeo = !string.IsNullOrWhiteSpace(info.NeoForge);
            loaderName = isNeo ? "neoforge" : "forge";
            loaderVersion = (isNeo ? info.NeoForge : info.Forge)!;
            var java = await ResolveJavaAsync(
                info.Minecraft, isNeo ? "NeoForge" : "Forge", loaderVersion, ct).ConfigureAwait(false);
            progress?.Report(new JobProgress("安装 Forge/NeoForge 加载器", 0.06));
            var loaderProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                update.Stage, 0.06 + (update.Percent ?? 0) * 0.35, null, null, update.Detail)));
            await _forgelike.InstallMergedAsync(
                isNeo ? ForgelikeKind.NeoForge : ForgelikeKind.Forge,
                info.Minecraft, loaderVersion, id, java, loaderProgress, ct).ConfigureAwait(false);
        }
        else
        {
            loaderName = "vanilla";
            loaderVersion = "";
            progress?.Report(new JobProgress("安装原版游戏", 0.06));
            await _installer.InstallAsync(
                info.Minecraft, new InstallOptions { Mirror = MirrorMode.Auto },
                progress, ct).ConfigureAwait(false);
            progress?.Report(new JobProgress("创建整合包实例", 0.45));
            await MergedInstanceBuilder.BuildAsync(_repository.GameDirectory, info.Minecraft, id, ct)
                .ConfigureAwait(false);
            await _installer.InstallAsync(
                id, new InstallOptions { Mirror = MirrorMode.Auto }, progress, ct).ConfigureAwait(false);
        }

        var gameDirectory = _repository.GetVersionDirectory(id);
        var mmcLibraries = mmc is null ? [] : await mmc.ApplyAsync(_repository, id, ct).ConfigureAwait(false);
        var mcbbsLibraries = mcbbs is null
            ? []
            : await ApplyMcbbsLibrariesAsync(mcbbs.Manifest.Libraries, id, progress, ct).ConfigureAwait(false);
        if (mmc is { HasChanges: true })
        {
            await _installer.InstallAsync(id, new InstallOptions { Mirror = MirrorMode.Auto }, progress, ct).ConfigureAwait(false);
            mmcLibraries = [.. mmcLibraries, .. await MmcModpackMetadata.RemoteFilesAsync(
                _repository, id, await _repository.GetVersionAsync(id, ct).ConfigureAwait(false), ct).ConfigureAwait(false)];
        }
        var extracted = new List<string>();
        if (info.Kind == LocalModpackKind.Hmcl)
        {
            progress?.Report(new JobProgress("解压整合包文件", 0.62));
            extracted = ExtractGameFiles(archive, info, gameDirectory, ct);
        }
        var hmclLibraries = new List<InstallManifestFile>();
        if (info.Kind == LocalModpackKind.Hmcl)
        {
            using var profile = ParseEntry(FindExactEntry(archive, info.OverridesFolder + "pack.json")!);
            var metadata = await HmclModpackComponents.ApplyLaunchMetadataAsync(
                _repository, id, profile.RootElement, info.OverridesFolder, ct).ConfigureAwait(false);
            if (metadata.Changed)
            {
                await _installer.InstallAsync(id,
                    new InstallOptions { Mirror = MirrorMode.Auto, VerifyExisting = true }, progress, ct).ConfigureAwait(false);
                hmclLibraries.AddRange(await HmclModpackComponents.CollectManagedFilesAsync(
                    _repository, id, metadata.BundledArchives, ct).ConfigureAwait(false));
                foreach (var archivePath in metadata.BundledArchives.Values)
                    extracted.RemoveAll(path => path.Equals(
                        FileUtil.SafeChildPath(gameDirectory, archivePath[info.OverridesFolder.Length..]),
                        StringComparison.OrdinalIgnoreCase));
            }
        }
        InstanceSettingsStore.Save(gameDirectory, new InstanceSettings { IsolationMode = IsolationPolicy.Isolated });

        if (info.Kind != LocalModpackKind.Hmcl)
        {
            progress?.Report(new JobProgress("解压整合包文件", 0.62));
            extracted = ExtractGameFiles(archive, info, gameDirectory, ct);
        }
        var downloaded = new List<InstallManifestFile>();
        var excludedOverrides = new List<string>();
        if (info.Kind == LocalModpackKind.Hmcl) excludedOverrides.Add("pack.json");
        if (info.Kind == LocalModpackKind.Hmcl)
        {
            foreach (var file in hmclLibraries.Where(file => file.ArchivePath is not null))
                excludedOverrides.Add(file.ArchivePath![info.OverridesFolder.Length..]);
        }
        foreach (var file in content.Where(file => file.Download is not null))
        {
            ct.ThrowIfCancellationRequested();
            var destination = FileUtil.SafeChildPath(gameDirectory, file.Path);
            var fileProgress = new DownloadProgressAdapter(update => progress?.Report(new JobProgress(
                "下载 MCBBS 整合包文件", 0.65 + (update.Percent ?? 0) * 0.3, null, null, file.Path)));
            await _downloads.DownloadAsync([file.Download!], new DownloadOptions { VerifyExisting = true }, fileProgress, ct).ConfigureAwait(false);
            if (file.Download!.Size is { } size && new FileInfo(destination).Length != size)
            {
                File.Delete(destination);
                throw new InvalidDataException("MCBBS 文件大小与元数据不一致：" + file.Path);
            }
            extracted.RemoveAll(path => path.Equals(destination, StringComparison.OrdinalIgnoreCase));
            excludedOverrides.Add(file.Path);
            downloaded.Add(new InstallManifestFile(
                Path.GetRelativePath(_repository.GameDirectory, destination).Replace('\\', '/'),
                await InstallManifestStore.ComputeFileSha1Async(destination, ct), "MCBBS", file.Download!.Urls[0]));
        }
        ApplyPackSettings(archive, info, _repository.GetVersionDirectory(id));
        if (mcbbs is not null)
        {
            var settings = InstanceSettingsStore.Load(gameDirectory);
            if (mcbbs.Manifest.LaunchInfo.MinMemoryMb is { } minMemory && minMemory > 0)
                settings.MemoryMb = Math.Max(settings.MemoryMb ?? 0, minMemory);
            if (!string.IsNullOrWhiteSpace(mcbbs.Manifest.LaunchInfo.AuthlibInjectorServer))
                settings.AuthServer = mcbbs.Manifest.LaunchInfo.AuthlibInjectorServer!;
            if (mcbbs.Manifest.LaunchInfo.JavaArguments.Count > 0)
                settings.ExtraJvmArgs = string.Join(" ", mcbbs.Manifest.LaunchInfo.JavaArguments);
            if (mcbbs.Manifest.LaunchInfo.LaunchArguments.Count > 0)
                settings.ExtraGameArgs = string.Join(" ", mcbbs.Manifest.LaunchInfo.LaunchArguments);
            InstanceSettingsStore.Save(gameDirectory, settings);
        }

        var managedFiles = await InstallManifestStore.CollectManagedFilesAsync(
            _repository.GameDirectory, id, "merged", ct).ConfigureAwait(false);
        managedFiles.AddRange(downloaded);
        managedFiles.AddRange(mmcLibraries);
        managedFiles.AddRange(mcbbsLibraries);
        managedFiles.AddRange(hmclLibraries);
        foreach (var overridePath in extracted)
        {
            var relative = Path.GetRelativePath(
                _repository.GameDirectory, overridePath).Replace('\\', '/');
            managedFiles.Add(new InstallManifestFile(
                relative, await InstallManifestStore.ComputeFileSha1Async(overridePath, ct),
                "overrides"));
        }
        ct.ThrowIfCancellationRequested();
        var packHashes = new Dictionary<string, string>();
        foreach (var file in content)
            packHashes[file.Path] = await InstallManifestStore.ComputeFileSha1Async(
                FileUtil.SafeChildPath(gameDirectory, file.Path), ct).ConfigureAwait(false);
        InstallManifestStore.Save(_repository.GameDirectory, id, new InstallManifest
        {
            SourceArchive = Path.GetFullPath(path),
            OverridesPrefix = info.OverridesFolder,
            OverridesExcludedPaths = excludedOverrides,
            OverridesRoot = gameDirectory,
            ModpackKind = mcbbs is null ? null : "MCBBS",
            ModpackManifestJson = mcbbs?.Manifest.RawJson,
            ModpackFileHashes = packHashes,
            McbbsMinMemoryMb = mcbbs?.Manifest.LaunchInfo.MinMemoryMb,
            McbbsSupportJava = mcbbs?.Manifest.LaunchInfo.SupportJava.ToList() ?? [],
            McbbsAuthlibInjectorServer = mcbbs?.Manifest.LaunchInfo.AuthlibInjectorServer,
            ManagedFiles = managedFiles,
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

    private async Task<IReadOnlyList<InstallManifestFile>> ApplyMcbbsLibrariesAsync(
        IReadOnlyList<Library> libraries, string id, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        if (libraries.Count == 0) return [];
        var profile = VersionMerger.Clone(await _repository.GetVersionAsync(id, ct).ConfigureAwait(false));
        var extra = VersionMerger.Clone(new VersionJson { Libraries = libraries.ToList() }).Libraries;
        var scope = "wmcl-packs/" + id + "/";
        foreach (var library in extra)
        {
            if (library.Downloads!.Artifact is { } artifact) artifact.Path = scope + artifact.Path;
            foreach (var download in library.Downloads.Classifiers?.Values.AsEnumerable() ?? [])
                download.Path = scope + download.Path;
            profile.Libraries.Add(library);
        }
        var resolved = LibraryResolver.Resolve(profile, RuleContext.CreateDefault())
            .Where(item => item.RelativePath.StartsWith(scope, StringComparison.Ordinal)).ToList();
        var items = resolved.Select(item => new DownloadItem
        {
            Destination = FileUtil.SafeChildPath(Path.Combine(_repository.GameDirectory, "libraries"), item.RelativePath),
            Urls = [item.Url!],
            Sha1 = item.Sha1,
            Size = item.Size
        });
        var downloadProgress = new DownloadProgressAdapter(update => progress?.Report(new JobProgress(
            "下载 MCBBS 额外运行库", update.Percent, null, null, update.CurrentFile)));
        await _downloads.DownloadAsync(items, new DownloadOptions { VerifyExisting = true }, downloadProgress, ct)
            .ConfigureAwait(false);
        foreach (var item in resolved)
        {
            var path = FileUtil.SafeChildPath(Path.Combine(_repository.GameDirectory, "libraries"), item.RelativePath);
            if (!File.Exists(path)) throw new InvalidDataException("MCBBS library 下载失败：" + item.Name);
            if (item.Size is { } size && new FileInfo(path).Length != size)
            {
                File.Delete(path);
                throw new InvalidDataException("MCBBS library 大小与元数据不一致：" + item.Name);
            }
        }
        var jsonPath = Path.Combine(_repository.GetVersionDirectory(id), id + ".json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(profile,
            new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        await _installer.InstallAsync(id, new InstallOptions { Mirror = MirrorMode.Auto }, progress, ct)
            .ConfigureAwait(false);
        var result = new List<InstallManifestFile>();
        foreach (var item in resolved.DistinctBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var path = FileUtil.SafeChildPath(Path.Combine(_repository.GameDirectory, "libraries"), item.RelativePath);
            if (!File.Exists(path)) throw new InvalidDataException("MCBBS library 下载失败：" + item.Name);
            result.Add(new InstallManifestFile("libraries/" + item.RelativePath,
                await InstallManifestStore.ComputeFileSha1Async(path, ct), "MCBBS-library", item.Url));
        }
        return result;
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
            var cfg = FindExactEntry(archive, info.BaseFolder + "instance.cfg");
            if (cfg is null) return;
            using var stream = cfg.Open();
            using var reader = new StreamReader(stream);
            packSettings = ParseMmcSettings(
                reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
        else if (info.Kind == LocalModpackKind.Mcbbs)
        {
            var entry = FindExactEntry(archive, info.BaseFolder + "mcbbs.packmeta") ??
                        FindExactEntry(archive, info.BaseFolder + "manifest.json");
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
        var jsonEntry = MetadataEntry(archive, LocalModpackKind.Mmc);
        var baseFolder = BaseFolder(jsonEntry);
        string name = "MMC Pack";
        var instanceCfg = FindExactEntry(archive, baseFolder + "instance.cfg");
        if (instanceCfg is not null)
        {
            using var stream = instanceCfg.Open();
            using var reader = new StreamReader(stream);
            var match = Regex.Match(reader.ReadToEnd(), @"(?m)^name=(.+)$");
            if (match.Success) name = match.Groups[1].Value.Trim();
        }

        using var document = ParseEntry(jsonEntry);
        var metadata = MmcModpackMetadata.Read(archive, document.RootElement, baseFolder);

        var overrides = HasDirectory(archive, baseFolder + "minecraft/")
            ? baseFolder + "minecraft/"
            : HasDirectory(archive, baseFolder + ".minecraft/")
                ? baseFolder + ".minecraft/"
                : "";
        return metadata.Components.Build(LocalModpackKind.Mmc, baseFolder, name, overrides);
    }

    private static LocalModpackInfo ParseHmcl(ZipArchive archive)
    {
        var entry = MetadataEntry(archive, LocalModpackKind.Hmcl);
        using var document = ParseEntry(entry);
        var root = document.RootElement;
        var name = root.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
        var game = root.TryGetProperty("gameVersion", out var gameValue) ? gameValue.GetString() : null;
        var baseFolder = BaseFolder(entry);
        var profileEntry = FindExactEntry(archive, baseFolder + "minecraft/pack.json")
                           ?? throw new InvalidDataException("HMCL 整合包缺少 minecraft/pack.json。");
        using var profile = ParseEntry(profileEntry);
        return HmclModpackComponents.Parse(profile.RootElement, game)
            .Build(LocalModpackKind.Hmcl, baseFolder, name ?? "HMCL Pack", baseFolder + "minecraft/");
    }

    private static LocalModpackInfo ParseMcbbs(ZipArchive archive)
    {
        var entry = MetadataEntry(archive, LocalModpackKind.Mcbbs);
        using var document = ParseEntry(entry);
        return ParseMcbbs(document.RootElement, BaseFolder(entry));
    }

    internal static LocalModpackInfo ParseMcbbs(JsonElement root, string baseFolder = "")
    {
        var name = root.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
        if (!root.TryGetProperty("addons", out var addons))
            throw new InvalidDataException("MCBBS 整合包缺少 addons。");
        var components = new LocalModpackComponents();
        if (addons.ValueKind == JsonValueKind.Array)
        {
            foreach (var addon in addons.EnumerateArray())
            {
                var id = addon.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
                var version = addon.TryGetProperty("version", out var versionValue) ? versionValue.GetString() : null;
                components.Add(id, version);
            }
        }
        else if (addons.ValueKind == JsonValueKind.Object)
        {
            foreach (var addon in addons.EnumerateObject())
                components.Add(addon.Name, addon.Value.GetString());
        }
        else throw new InvalidDataException("MCBBS addons 必须是数组或对象。");
        return components.Build(LocalModpackKind.Mcbbs, baseFolder, name ?? "MCBBS Pack", baseFolder + "overrides/");
    }

    private static JsonDocument ParseEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return JsonDocument.Parse(reader.ReadToEnd());
    }

    private static (LocalModpackKind Kind, ZipArchiveEntry Entry)? FindMetadata(ZipArchive archive)
    {
        var candidates = new List<(LocalModpackKind Kind, ZipArchiveEntry Entry)>();
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName[(entry.FullName.LastIndexOf('/') + 1)..].ToLowerInvariant();
            var kind = name switch
            {
                "mmc-pack.json" => LocalModpackKind.Mmc,
                "modpack.json" => LocalModpackKind.Hmcl,
                "mcbbs.packmeta" => LocalModpackKind.Mcbbs,
                _ => LocalModpackKind.Unknown
            };
            if (name == "manifest.json")
            {
                try
                {
                    using var document = ParseEntry(entry);
                    if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("addons", out _))
                        kind = LocalModpackKind.Mcbbs;
                }
                catch (JsonException) { }
            }
            if (kind != LocalModpackKind.Unknown) candidates.Add((kind, entry));
        }
        if (candidates.Count == 0) return null;
        var depth = candidates.Min(candidate => candidate.Entry.FullName.Count(character => character == '/'));
        var nearest = candidates.Where(candidate => candidate.Entry.FullName.Count(character => character == '/') == depth).ToList();
        if (nearest.Select(candidate => BaseFolder(candidate.Entry)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1 ||
            nearest.Select(candidate => candidate.Kind).Distinct().Count() != 1)
            throw new InvalidDataException("整合包存在多个实例或冲突格式，无法确定导入哪个实例。");
        var selected = nearest.FirstOrDefault(candidate => candidate.Entry.FullName.EndsWith("mcbbs.packmeta", StringComparison.OrdinalIgnoreCase));
        if (selected.Entry is null) selected = nearest[0];
        FindExactEntry(archive, selected.Entry.FullName);
        return selected;
    }

    internal static ZipArchiveEntry MetadataEntry(ZipArchive archive, LocalModpackKind kind)
    {
        var metadata = FindMetadata(archive);
        if (metadata is null || metadata.Value.Kind != kind)
            throw new InvalidDataException($"整合包缺少 {kind} 元数据或格式不匹配。");
        return metadata.Value.Entry;
    }

    private static ZipArchiveEntry? FindExactEntry(ZipArchive archive, string path)
    {
        var matches = archive.Entries.Where(entry => entry.FullName.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count > 1) throw new InvalidDataException("整合包元数据路径重复：" + path);
        return matches.SingleOrDefault();
    }

    private static string BaseFolder(ZipArchiveEntry entry)
    {
        var index = entry.FullName.LastIndexOf('/');
        return index < 0 ? "" : entry.FullName[..(index + 1)];
    }

    private static bool HasDirectory(ZipArchive archive, string prefix) =>
        archive.Entries.Any(entry => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static List<string> ExtractGameFiles(
        ZipArchive archive, LocalModpackInfo info, string gameDirectory, CancellationToken ct)
    {
        var extracted = new List<string>();
        if (string.IsNullOrWhiteSpace(info.OverridesFolder)) return extracted;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/') ||
                !entry.FullName.StartsWith(info.OverridesFolder, StringComparison.OrdinalIgnoreCase))
                continue;
            var relative = entry.FullName[info.OverridesFolder.Length..];
            if (info.Kind == LocalModpackKind.Hmcl && relative.Equals("pack.json", StringComparison.OrdinalIgnoreCase))
                continue;
            var destination = SafePath(gameDirectory, relative);
            if (destination is null) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
            extracted.Add(destination);
        }
        return extracted;
    }

    private static string? SafePath(string root, string relative)
    {
        var normalized = relative.Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0 || normalized.Split('/').Contains("..")) return null;
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        return full.StartsWith(rootFull, StringComparison.Ordinal) ? full : null;
    }

    private Task<string> ResolveJavaAsync(string minecraft, string loader,
        string? loaderVersion, CancellationToken ct)
    {
        if (_javaRequirementResolver is not null)
            return _javaRequirementResolver(
                new ModpackJavaRequest(minecraft, loader, loaderVersion, InferJavaMajor(minecraft)), ct);
        if (_javaResolver is not null)
            return _javaResolver(InferJavaMajor(minecraft), ct);
        throw new InvalidOperationException("安装 Forge/NeoForge 整合包需要 Java。");
    }

    private static int InferJavaMajor(string minecraft)
    {
        var parts = minecraft.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var minor)) return 21;
        var patch = parts.Length >= 3 && int.TryParse(parts[2], out var value) ? value : 0;
        if (minor > 20 || (minor == 20 && patch >= 5)) return 21;
        if (minor >= 17) return 17;
        return 8;
    }

    private string CreateInstanceId(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(raw.Select(character => invalid.Contains(character) || character is '/' or '\\' ? '-' : character).ToArray()).Trim('-', ' ', '.');
        if (string.IsNullOrWhiteSpace(name)) name = "LocalPack";
        if (name.Length > 64) name = name[..64];
        var candidate = name;
        var suffix = 2;
        while (Directory.Exists(Path.Combine(_repository.VersionsDirectory, candidate)))
        {
            var ending = "-" + suffix++;
            candidate = name[..Math.Min(name.Length, 64 - ending.Length)] + ending;
        }
        return candidate;
    }

    private sealed class ProgressAdapter(Action<JobProgress> action) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => action(value);
    }

    private sealed class DownloadProgressAdapter(Action<DownloadProgress> action) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => action(value);
    }
}
