using System.IO.Compression;
using System.Text.Json;
using WMCL.Core;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Core.Logging;
using WMCL.Download;
using WMCL.Minecraft;
using WMCL.Modding.Fabric;
using WMCL.Modding.Forgelike;
using WMCL.Modding.Modrinth;

namespace WMCL.Modding.Modpack;

public sealed record ModpackFile(
    string Path,
    IReadOnlyList<string> Downloads,
    string? Sha1,
    string? EnvironmentClient,
    long? Size = null);

public sealed record ModpackIndex(
    string Name,
    string VersionId,
    string Minecraft,
    string? FabricLoader,
    string? Forge,
    string? NeoForge,
    IReadOnlyList<ModpackFile> Files,
    string? QuiltLoader = null);

/// <summary>
/// Modrinth .mrpack 安装。对照 PCL2 的 InstallPackModrinth：
/// 读取 modrinth.index.json → 安装依赖加载器 → 下载 files → 解压 overrides/client-overrides。
/// 支持原版、Fabric、Quilt、Forge 与 NeoForge 整合包。
/// </summary>
public sealed class ModpackInstaller
{
    private readonly VersionRepository _repository;
    private readonly VersionInstaller _installer;
    private readonly FabricInstaller _fabric;
    private readonly FabricInstaller _quilt;
    private readonly DownloadManager _downloads;
    private readonly string _archiveCacheDirectory;
    private readonly ModrinthClient _client;
    private readonly ForgelikeInstaller _forgelike;
    private readonly Func<int, CancellationToken, Task<string>>? _javaResolver;
    private readonly Func<ModpackJavaRequest, CancellationToken, Task<string>>? _javaRequirementResolver;

    public ModpackInstaller(
        VersionRepository repository,
        VersionInstaller? installer = null,
        FabricInstaller? fabric = null,
        ModrinthClient? client = null,
        ForgelikeInstaller? forgelike = null,
        Func<int, CancellationToken, Task<string>>? javaResolver = null,
        FabricInstaller? quilt = null,
        DownloadManager? downloads = null,
        string? archiveCacheDirectory = null,
        Func<ModpackJavaRequest, CancellationToken, Task<string>>? javaRequirementResolver = null)
    {
        _repository = repository;
        _installer = installer ?? new VersionInstaller(repository);
        _fabric = fabric ?? new FabricInstaller(repository, _installer);
        _quilt = quilt ?? new FabricInstaller(repository, _installer, FabricFamilyKind.Quilt);
        _downloads = downloads ?? new DownloadManager();
        _archiveCacheDirectory = archiveCacheDirectory ?? Path.Combine(WmclPaths.CacheDirectory, "modpacks");
        _client = client ?? new ModrinthClient();
        _forgelike = forgelike ?? new ForgelikeInstaller(repository, _installer);
        _javaResolver = javaResolver;
        _javaRequirementResolver = javaRequirementResolver;
    }

    public static ModpackIndex ParseIndex(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("formatVersion", out var format) ||
            format.ValueKind != JsonValueKind.Number || !format.TryGetInt32(out var formatVersion) || formatVersion != 1)
            throw new InvalidDataException("不支持的 Modrinth 整合包格式版本。");
        if (!root.TryGetProperty("game", out var game) || game.ValueKind != JsonValueKind.String || game.GetString() != "minecraft")
            throw new InvalidDataException("该 Modrinth 整合包不适用于 Minecraft。");
        if (!root.TryGetProperty("dependencies", out var dependencies) ||
            dependencies.ValueKind != JsonValueKind.Object ||
            !dependencies.TryGetProperty("minecraft", out var minecraft))
            throw new InvalidDataException("Modrinth 整合包未提供 Minecraft 版本信息。");

        string? GetDependency(string name) =>
            dependencies.TryGetProperty(name, out var value) ? value.GetString() : null;

        foreach (var dependency in dependencies.EnumerateObject())
        {
            if (dependency.Name is not ("minecraft" or "fabric-loader" or "quilt-loader" or "forge" or "neoforge" or "neo-forge"))
                throw new NotSupportedException("不支持的整合包依赖：" + dependency.Name);
            if (dependency.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(dependency.Value.GetString()))
                throw new InvalidDataException("整合包依赖版本为空或无效：" + dependency.Name);
        }
        if (new[] { "fabric-loader", "quilt-loader", "forge", "neoforge", "neo-forge" }.Count(name => GetDependency(name) is not null) > 1)
            throw new InvalidDataException("整合包声明了多个互斥的加载器依赖。");
        InstanceManager.ValidateName(minecraft.GetString()!);

        var files = new List<ModpackFile>();
        if (!root.TryGetProperty("files", out var filesElement) || filesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("整合包文件列表无效。");
        foreach (var file in filesElement.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object) throw new InvalidDataException("整合包文件条目无效。");
            var path = file.TryGetProperty("path", out var pathElement)
                && pathElement.ValueKind == JsonValueKind.String ? pathElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("整合包文件路径为空。");

            var downloads = file.TryGetProperty("downloads", out var downloadsElement) &&
                            downloadsElement.ValueKind == JsonValueKind.Array
                ? downloadsElement.EnumerateArray()
                    .Select(element => element.ValueKind == JsonValueKind.String ? element.GetString() : null)
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Cast<string>()
                    .ToList()
                : [];

            var sha1 = file.TryGetProperty("hashes", out var hashes) &&
                       hashes.ValueKind == JsonValueKind.Object && hashes.TryGetProperty("sha1", out var sha1Element) &&
                       sha1Element.ValueKind == JsonValueKind.String
                ? sha1Element.GetString()
                : null;
            var environment = file.TryGetProperty("env", out var env) &&
                              env.ValueKind == JsonValueKind.Object && env.TryGetProperty("client", out var client) &&
                              client.ValueKind == JsonValueKind.String
                ? client.GetString()
                : null;

            if (environment is not (null or "required" or "optional" or "unsupported"))
                throw new InvalidDataException("整合包客户端环境无效：" + path);
            if (environment != "unsupported" && !IsSha1(sha1))
                throw new InvalidDataException("整合包文件缺少有效 SHA-1：" + path);
            foreach (var url in downloads)
                if (!IsDownloadUrl(url)) throw new InvalidDataException("整合包下载地址无效：" + path);
            long? size = null;
            if (file.TryGetProperty("fileSize", out var fileSize))
            {
                if (fileSize.ValueKind != JsonValueKind.Number || !fileSize.TryGetInt64(out var length) || length < 0)
                    throw new InvalidDataException("整合包文件大小无效：" + path);
                size = length;
            }
            files.Add(new ModpackFile(path, downloads, sha1, environment, size));
        }

        return new ModpackIndex(
            root.TryGetProperty("name", out var name) ? name.GetString() ?? "Modpack" : "Modpack",
            root.TryGetProperty("versionId", out var version) ? version.GetString() ?? "1.0" : "1.0",
            minecraft.GetString() ?? throw new InvalidDataException("缺少 Minecraft 版本。"),
            GetDependency("fabric-loader"),
            GetDependency("forge"),
            GetDependency("neoforge") ?? GetDependency("neo-forge"),
            files, GetDependency("quilt-loader"));
    }

    public async Task<string> InstallFromFileAsync(
        string mrpackPath,
        string? instanceName = null,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default,
        bool includeOptional = true,
        IReadOnlySet<string>? selectedOptionalPaths = null)
    {
        ct.ThrowIfCancellationRequested();
        selectedOptionalPaths = selectedOptionalPaths?.ToHashSet(StringComparer.Ordinal);
        progress?.Report(new JobProgress("解析整合包", 0.02));
        using var archive = ZipFile.OpenRead(mrpackPath);
        var indexEntry = archive.GetEntry("modrinth.index.json")
                         ?? throw new InvalidDataException(
                             "不是有效的 Modrinth 整合包（缺少 modrinth.index.json）。");
        using (var stream = indexEntry.Open())
        using (var reader = new StreamReader(stream))
        {
            var index = ParseIndex(await reader.ReadToEndAsync(ct).ConfigureAwait(false));

            var id = CreateInstanceId(instanceName ?? index.Name + "-" + index.VersionId);
            if (id.Equals(index.Minecraft, StringComparison.OrdinalIgnoreCase)) id = CreateInstanceId(id + "-Pack");
            var plannedDirectory = _repository.GetVersionDirectory(id);
            ValidateContentPaths(archive, index, plannedDirectory, id);
            foreach (var file in index.Files.Where(file => ShouldDownload(file, includeOptional, selectedOptionalPaths)))
                if (file.Downloads.Count == 0) throw new InvalidDataException("整合包文件缺少下载地址：" + file.Path);
            string instanceDirectory;

            if (!string.IsNullOrWhiteSpace(index.FabricLoader) || !string.IsNullOrWhiteSpace(index.QuiltLoader))
            {
                progress?.Report(new JobProgress("安装整合包加载器", 0.05));
                var loaderProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                    update.Stage, 0.05 + (update.Percent ?? 0) * 0.45, null, null, update.Detail)));
                var loader = index.QuiltLoader is null ? _fabric : _quilt;
                await loader.InstallMergedAsync(
                    index.Minecraft, (index.QuiltLoader ?? index.FabricLoader)!, id, loaderProgress, ct).ConfigureAwait(false);
                instanceDirectory = _repository.GetVersionDirectory(id);
            }
            else if (index.Forge is not null || index.NeoForge is not null)
            {
                var kind = index.NeoForge is not null
                    ? ForgelikeKind.NeoForge
                    : ForgelikeKind.Forge;
                var loaderVersion = NormalizeForgelikeVersion(index, kind);
                progress?.Report(new JobProgress("准备 Forge/NeoForge 运行环境", 0.04));
                var javaPath = await ResolveJavaAsync(
                    index.Minecraft, kind.ToString(), loaderVersion, ct).ConfigureAwait(false);

                var loaderProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                    update.Stage, 0.05 + (update.Percent ?? 0) * 0.4, null, null, update.Detail)));
                await _forgelike.InstallMergedAsync(
                    kind, index.Minecraft, loaderVersion, id, javaPath,
                    loaderProgress, ct).ConfigureAwait(false);
                instanceDirectory = _repository.GetVersionDirectory(id);
            }
            else
            {
                var vanillaProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                    update.Stage, 0.05 + (update.Percent ?? 0) * 0.45, null, null, update.Detail)));
                await _installer.InstallAsync(index.Minecraft, progress: vanillaProgress, ct: ct).ConfigureAwait(false);
                await MergedInstanceBuilder.BuildAsync(_repository.GameDirectory, index.Minecraft, id, ct).ConfigureAwait(false);
                var completeProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                    update.Stage, 0.55 + (update.Percent ?? 0) * 0.15, null, null, update.Detail)));
                await _installer.InstallAsync(id, progress: completeProgress, ct: ct).ConfigureAwait(false);
                instanceDirectory = _repository.GetVersionDirectory(id);
            }

            InstanceSettingsStore.Save(instanceDirectory, new InstanceSettings { IsolationMode = IsolationPolicy.Isolated });
            var managed = await DownloadFilesAsync(
                index, instanceDirectory, progress, ct, 0.72, includeOptional, selectedOptionalPaths).ConfigureAwait(false);
            progress?.Report(new JobProgress("解压整合包覆写", 0.97));
            foreach (var file in ExtractOverrides(archive, instanceDirectory, ct)) managed[file] = null;
            await SaveManifestAsync(index, id, mrpackPath, instanceDirectory, managed, ct).ConfigureAwait(false);

            progress?.Report(new JobProgress("整合包安装完成", 1));
            Log.Info($"Modrinth 整合包安装完成：{id}");
            return id;
        }
    }

    public async Task<string> InstallFromModrinthAsync(
        string projectId,
        string? gameVersion = null,
        string? instanceName = null,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default,
        bool includeOptional = true)
    {
        progress?.Report(new JobProgress("获取整合包版本", 0.01));
        var versions = await _client.GetVersionsAsync(projectId, ct).ConfigureAwait(false);
        var version = versions
            .Where(item => gameVersion is null ||
                           item.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
            .Where(item => item.Files.Count > 0)
            .OrderByDescending(item => item.DatePublished)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"找不到适用于 Minecraft {gameVersion ?? "任意版本"} 的整合包版本。");

        var file = version.Files.FirstOrDefault(item => item.Primary) ?? version.Files[0];
        var destination = await DownloadArchiveAsync(file, progress, ct).ConfigureAwait(false);

        return await InstallFromFileAsync(destination, instanceName, progress, ct, includeOptional)
            .ConfigureAwait(false);
    }

    public async Task<string> DownloadArchiveAsync(ModrinthFile file, IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!IsSha1(file.Sha1) || file.Size <= 0 || !IsDownloadUrl(file.Url) ||
            file.Filename != FileUtil.SafeFileName(file.Filename) || !file.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("整合包下载元数据无效。");
        var destination = FileUtil.SafeChildPath(
            Path.Combine(_archiveCacheDirectory, file.Sha1!.ToLowerInvariant()), file.Filename);
        var adapter = new DownloadProgressAdapter(download => progress?.Report(new JobProgress(
            "下载整合包", download.Percent, (long)download.BytesPerSecond, null, file.Filename)));
        await _downloads.DownloadAsync([new DownloadItem
        {
            Destination = destination, Urls = [file.Url], Sha1 = file.Sha1, Size = file.Size
        }], new DownloadOptions { MaxConcurrency = 1, VerifyExisting = true }, adapter, ct).ConfigureAwait(false);
        return destination;
    }

    public static async Task<IReadOnlyList<ModpackFile>> GetOptionalFilesAsync(string path, CancellationToken ct = default)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("modrinth.index.json") ?? throw new InvalidDataException("缺少 modrinth.index.json。");
        using var reader = new StreamReader(entry.Open());
        var index = ParseIndex(await reader.ReadToEndAsync(ct).ConfigureAwait(false));
        return index.Files.Where(file => file.EnvironmentClient == "optional").ToArray();
    }

    private async Task<Dictionary<string, string?>> DownloadFilesAsync(
        ModpackIndex index,
        string instanceDirectory,
        IProgress<JobProgress>? progress,
        CancellationToken ct,
        double startPercent,
        bool includeOptional,
        IReadOnlySet<string>? selectedOptionalPaths)
    {
        var items = new List<DownloadItem>();
        var managed = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var file in index.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (!ShouldDownload(file, includeOptional, selectedOptionalPaths)) continue;

            var destination = FileUtil.SafeChildPath(instanceDirectory, file.Path);
            if (file.Downloads.Count == 0) throw new InvalidDataException("整合包文件缺少下载地址：" + file.Path);

            items.Add(new DownloadItem
            {
                Destination = destination,
                Urls = file.Downloads,
                Sha1 = file.Sha1,
                Size = file.Size
            });
            managed[destination] = file.Downloads[0];
        }

        if (items.Count == 0)
        {
            progress?.Report(new JobProgress("整合包文件已就绪", 0.95));
            return managed;
        }

        var adapter = new DownloadProgressAdapter(download => progress?.Report(new JobProgress(
            "下载整合包文件", startPercent + (download.Percent ?? 0) * (0.95 - startPercent),
            (long)download.BytesPerSecond, null, null)));
        await _downloads
            .DownloadAsync(items, new DownloadOptions { MaxConcurrency = 8, VerifyExisting = true }, adapter, ct)
            .ConfigureAwait(false);
        return managed;
    }

    private static string NormalizeForgelikeVersion(ModpackIndex index, ForgelikeKind kind)
    {
        var version = kind == ForgelikeKind.NeoForge ? index.NeoForge! : index.Forge!;
        if (version.StartsWith("forge-", StringComparison.OrdinalIgnoreCase))
            version = version["forge-".Length..];
        if (version.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase))
            version = version["neoforge-".Length..];
        var prefix = index.Minecraft + "-";
        if (kind == ForgelikeKind.Forge &&
            version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            version = version[prefix.Length..];
        return version;
    }

    private Task<string> ResolveJavaAsync(string minecraft, string loader,
        string? loaderVersion, CancellationToken ct)
    {
        if (_javaRequirementResolver is not null)
            return _javaRequirementResolver(
                new ModpackJavaRequest(minecraft, loader, loaderVersion, InferJavaMajor(minecraft)), ct);
        if (_javaResolver is not null)
            return _javaResolver(InferJavaMajor(minecraft), ct);
        throw new InvalidOperationException("安装 Forge/NeoForge 整合包需要可用的 Java。");
    }

    private static int InferJavaMajor(string minecraftVersion)
    {
        var parts = minecraftVersion.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var minor)) return 21;
        var patch = parts.Length >= 3 && int.TryParse(parts[2], out var value) ? value : 0;
        if (minor > 20 || (minor == 20 && patch >= 5)) return 21;
        if (minor >= 17) return 17;
        if (minor >= 12) return 11;
        return 8;
    }

    private static IReadOnlyList<string> ExtractOverrides(ZipArchive archive, string instanceDirectory, CancellationToken ct)
    {
        var extracted = new List<string>();
        foreach (var prefix in new[] { "overrides/", "client-overrides/" })
            foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith(prefix, StringComparison.Ordinal)))
            {
                ct.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/')) continue;
                var destination = FileUtil.SafeChildPath(instanceDirectory, entry.FullName[prefix.Length..]);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
                extracted.Add(destination);
            }
        return extracted;
    }

    private static void ValidateContentPaths(ZipArchive archive, ModpackIndex index, string directory, string id)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in index.Files)
        {
            ModpackContentPaths.Validate(file.Path, directory, id);
            if (!seen.Add(file.Path.Replace('\\', '/'))) throw new InvalidDataException("整合包文件路径重复：" + file.Path);
        }
        foreach (var prefix in new[] { "overrides/", "client-overrides/" })
        {
            seen.Clear();
            foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith(prefix, StringComparison.Ordinal) && !entry.FullName.EndsWith('/')))
            {
                var relative = entry.FullName[prefix.Length..];
                ModpackContentPaths.Validate(relative, directory, id);
                if (!seen.Add(relative.Replace('\\', '/'))) throw new InvalidDataException("整合包覆写路径重复：" + relative);
            }
        }
    }

    private async Task SaveManifestAsync(ModpackIndex index, string id, string archive, string directory,
        IReadOnlyDictionary<string, string?> files, CancellationToken ct)
    {
        var manifest = new InstallManifest
        {
            SourceArchive = Path.GetFullPath(archive),
            OverridesRoot = directory,
            OverridesPrefix = "overrides/",
            OverridesPrefixes = ["overrides/", "client-overrides/"],
            ManagedFiles = await InstallManifestStore.CollectManagedFilesAsync(_repository.GameDirectory, id, "minecraft", ct).ConfigureAwait(false),
            Components = { ["minecraft"] = index.Minecraft }
        };
        if (index.FabricLoader is not null) manifest.Components["fabric"] = index.FabricLoader;
        if (index.QuiltLoader is not null) manifest.Components["quilt"] = index.QuiltLoader;
        if (index.Forge is not null) manifest.Components["forge"] = NormalizeForgelikeVersion(index, ForgelikeKind.Forge);
        if (index.NeoForge is not null) manifest.Components["neoforge"] = NormalizeForgelikeVersion(index, ForgelikeKind.NeoForge);
        foreach (var file in files)
            manifest.ManagedFiles.Add(new InstallManifestFile(
                Path.GetRelativePath(_repository.GameDirectory, file.Key).Replace('\\', '/'),
                await InstallManifestStore.ComputeFileSha1Async(file.Key, ct).ConfigureAwait(false),
                file.Value is null ? "overrides" : "modrinth", file.Value));
        ct.ThrowIfCancellationRequested();
        InstallManifestStore.Save(_repository.GameDirectory, id, manifest);
    }

    private string CreateInstanceId(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(raw.Select(character => invalid.Contains(character) || character is '/' or '\\' ? '-' : character).ToArray()).Trim('-', ' ', '.');
        if (string.IsNullOrWhiteSpace(name)) name = "Modpack";
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

    private static bool ShouldDownload(ModpackFile file, bool includeOptional, IReadOnlySet<string>? selectedOptionalPaths) =>
        file.EnvironmentClient != "unsupported" && (file.EnvironmentClient != "optional" ||
            (includeOptional && (selectedOptionalPaths is null || selectedOptionalPaths.Contains(file.Path))));

    private static bool IsSha1(string? value) => value is { Length: 40 } && value.All(Uri.IsHexDigit);

    private static bool IsDownloadUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";

    private sealed class ProgressAdapter(Action<JobProgress> action) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => action(value);
    }

    private sealed class DownloadProgressAdapter(Action<DownloadProgress> action)
        : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => action(value);
    }
}
