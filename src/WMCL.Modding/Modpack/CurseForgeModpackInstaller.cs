using System.IO.Compression;
using System.Text.Json;
using WMCL.Core;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Fabric;
using WMCL.Modding.Forgelike;

namespace WMCL.Modding.Modpack;

public sealed record CurseForgeManifestFile(long ProjectId, long FileId, bool Required);

public sealed record CurseForgeOptionalFile(
    long ProjectId,
    long FileId,
    string DisplayName);

public sealed record CurseForgeManifest(
    string Name,
    string Version,
    string Minecraft,
    string LoaderId,
    IReadOnlyList<CurseForgeManifestFile> Files,
    string Overrides);

/// <summary>
/// CurseForge 整合包：manifest.json → 加载器 → overrides → 逐文件下载。
/// 对应 PCL2 InstallPackCurseForge 的最小客户端实现。
/// </summary>
public sealed class CurseForgeModpackInstaller
{
    private readonly VersionRepository _repository;
    private readonly VersionInstaller _installer;
    private readonly FabricInstaller _fabric;
    private readonly FabricInstaller _quilt;
    private readonly ForgelikeInstaller _forgelike;
    private readonly CurseForgeClient _client;
    private readonly DownloadManager _downloads;
    private readonly string _archiveCacheDirectory;
    private readonly Func<int, CancellationToken, Task<string>>? _javaResolver;
    private readonly Func<ModpackJavaRequest, CancellationToken, Task<string>>? _javaRequirementResolver;

    public CurseForgeModpackInstaller(
        VersionRepository repository,
        VersionInstaller? installer = null,
        FabricInstaller? fabric = null,
        ForgelikeInstaller? forgelike = null,
        CurseForgeClient? client = null,
        Func<int, CancellationToken, Task<string>>? javaResolver = null,
        string? isolationMode = null,
        DownloadManager? downloads = null,
        FabricInstaller? quilt = null,
        string? archiveCacheDirectory = null,
        Func<ModpackJavaRequest, CancellationToken, Task<string>>? javaRequirementResolver = null)
    {
        _repository = repository;
        _installer = installer ?? new VersionInstaller(repository);
        _fabric = fabric ?? new FabricInstaller(repository, _installer);
        _quilt = quilt ?? new FabricInstaller(repository, _installer, FabricFamilyKind.Quilt);
        _forgelike = forgelike ?? new ForgelikeInstaller(repository, _installer);
        _client = client ?? new CurseForgeClient();
        _downloads = downloads ?? new DownloadManager();
        _archiveCacheDirectory = archiveCacheDirectory ?? Path.Combine(WmclPaths.CacheDirectory, "modpacks");
        _javaResolver = javaResolver;
        _javaRequirementResolver = javaRequirementResolver;
    }

    public static CurseForgeManifest ParseManifest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("minecraft", out var minecraft) ||
            minecraft.ValueKind != JsonValueKind.Object || !minecraft.TryGetProperty("version", out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(versionElement.GetString()))
            throw new InvalidDataException("CurseForge 整合包缺少 minecraft.version。");
        InstanceManager.ValidateName(versionElement.GetString()!);
        if (root.TryGetProperty("manifestType", out var manifestType) &&
            (manifestType.ValueKind != JsonValueKind.String || manifestType.GetString() != "minecraftModpack") ||
            root.TryGetProperty("manifestVersion", out var manifestVersion) &&
            (manifestVersion.ValueKind != JsonValueKind.Number || !manifestVersion.TryGetInt32(out var format) || format != 1))
            throw new InvalidDataException("不支持的 CurseForge 整合包格式。");

        var loader = "";
        if (minecraft.TryGetProperty("modLoaders", out var loaders))
        {
            if (loaders.ValueKind != JsonValueKind.Array) throw new InvalidDataException("CurseForge modLoaders 必须是数组。");
            foreach (var item in loaders.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var loaderId) ||
                    loaderId.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(loaderId.GetString()))
                    throw new InvalidDataException("CurseForge 加载器缺少 id。");
                if (item.TryGetProperty("primary", out var primaryValue) && primaryValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException("CurseForge 加载器 primary 必须是布尔值。");
            }
            if (loaders.EnumerateArray().Count(item => item.TryGetProperty("primary", out var primaryFlag) && primaryFlag.GetBoolean()) > 1)
                throw new InvalidDataException("CurseForge 整合包声明了多个主要加载器。");
            var primary = loaders.EnumerateArray()
                .FirstOrDefault(item => item.TryGetProperty("primary", out var p) && p.GetBoolean());
            if (primary.ValueKind == JsonValueKind.Undefined)
                primary = loaders.EnumerateArray().FirstOrDefault();
            if (primary.ValueKind == JsonValueKind.Object &&
                primary.TryGetProperty("id", out var idElement))
                loader = idElement.GetString() ?? "";
        }

        var files = new List<CurseForgeManifestFile>();
        var identities = new Dictionary<long, long>();
        if (root.TryGetProperty("files", out var filesElement))
        {
            if (filesElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("CurseForge files 必须是数组。");
            foreach (var file in filesElement.EnumerateArray())
            {
                if (file.ValueKind != JsonValueKind.Object || !file.TryGetProperty("projectID", out var projectElement) ||
                    projectElement.ValueKind != JsonValueKind.Number || !projectElement.TryGetInt64(out var project) ||
                    !file.TryGetProperty("fileID", out var fileElement) || fileElement.ValueKind != JsonValueKind.Number ||
                    !fileElement.TryGetInt64(out var fileId))
                    throw new InvalidDataException("CurseForge 文件缺少有效项目或文件标识。");
                if (file.TryGetProperty("required", out var requiredElement) && requiredElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException("CurseForge 文件 required 必须是布尔值。");
                var required = !file.TryGetProperty("required", out var r) || r.GetBoolean();
                if (project <= 0 || fileId <= 0) throw new InvalidDataException("CurseForge 文件缺少有效项目或文件标识。");
                if (identities.TryGetValue(fileId, out var existingProject) && existingProject != project)
                    throw new InvalidDataException("CurseForge 同一文件标识对应多个项目：" + fileId);
                identities[fileId] = project;
                var existing = files.FindIndex(entry => entry.FileId == fileId);
                if (existing < 0) files.Add(new CurseForgeManifestFile(project, fileId, required));
                else if (required) files[existing] = files[existing] with { Required = true };
            }
        }

        if (root.TryGetProperty("overrides", out var overrideElement) && overrideElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            throw new InvalidDataException("CurseForge overrides 必须是字符串。");
        return new CurseForgeManifest(
            root.TryGetProperty("name", out var name) ? name.GetString() ?? "CurseForge Pack" : "CurseForge Pack",
            root.TryGetProperty("version", out var packVersion) ? packVersion.GetString() ?? "1.0" : "1.0",
            versionElement.GetString() ?? throw new InvalidDataException("缺少 Minecraft 版本。"),
            loader,
            files,
            root.TryGetProperty("overrides", out var overrides) ? overrides.GetString() ?? "overrides" : "overrides");
    }

    public async Task<IReadOnlyList<CurseForgeOptionalFile>> GetOptionalFilesAsync(
        string zipPath,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var archive = ZipFile.OpenRead(zipPath);
        var manifestEntry = FindManifestEntry(archive);
        string manifestJson;
        using (var stream = manifestEntry.Open())
        using (var reader = new StreamReader(stream))
            manifestJson = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        var manifest = ParseManifest(manifestJson);
        var result = new List<CurseForgeOptionalFile>();
        foreach (var entry in manifest.Files.Where(file => !file.Required))
        {
            string name;
            try
            {
                var file = await _client.GetFileByIdAsync(entry.ProjectId, entry.FileId, ct)
                    .ConfigureAwait(false);
                name = string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                name = $"项目 {entry.ProjectId} / 文件 {entry.FileId}";
            }
            result.Add(new CurseForgeOptionalFile(entry.ProjectId, entry.FileId, name));
        }
        ct.ThrowIfCancellationRequested();
        return result;
    }

    public async Task<string> InstallFromProjectAsync(
        long projectId,
        string gameVersion,
        string? instanceName = null,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default,
        bool includeOptional = true)
    {
        var file = await _client.FindFileAsync(projectId, gameVersion, null, ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException(
                       $"CurseForge 整合包 {projectId} 没有适用于 Minecraft {gameVersion} 的版本。");
        var destination = await DownloadArchiveAsync(file, progress, ct).ConfigureAwait(false);
        return await InstallFromFileAsync(
                destination, instanceName, progress, ct, includeOptional)
            .ConfigureAwait(false);
    }

    public async Task<string> DownloadArchiveAsync(CurseForgeFile file, IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        CurseForgeClient.ValidateDownloadMetadata(file);
        var identity = "curseforge-" + file.Id + (file.Sha1 is null ? "" : "-" + file.Sha1.ToLowerInvariant());
        var destination = FileUtil.SafeChildPath(_archiveCacheDirectory, identity + ".zip");
        await CurseForgeClient.DownloadFileAsync(file, destination, progress, ct, _downloads).ConfigureAwait(false);
        return destination;
    }

    public async Task<string> InstallFromFileAsync(
        string zipPath,
        string? instanceName = null,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default,
        bool includeOptional = true,
        IReadOnlySet<long>? selectedOptionalFileIds = null)
    {
        ct.ThrowIfCancellationRequested();
        selectedOptionalFileIds = selectedOptionalFileIds?.ToHashSet();
        progress?.Report(new JobProgress("解析 CurseForge 整合包", 0.02));
        using var archive = ZipFile.OpenRead(zipPath);
        var manifestEntry = FindManifestEntry(archive);
        string manifestJson;
        using (var stream = manifestEntry.Open())
        using (var reader = new StreamReader(stream))
            manifestJson = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        var manifest = ParseManifest(manifestJson);
        var id = CreateInstanceId(instanceName ?? manifest.Name + "-" + manifest.Version);
        if (id.Equals(manifest.Minecraft, StringComparison.OrdinalIgnoreCase)) id = CreateInstanceId(id + "-Pack");
        var plannedDirectory = _repository.GetVersionDirectory(id);
        var baseFolder = manifestEntry.FullName[..^"manifest.json".Length];
        var overridesPrefix = OverridePrefix(baseFolder, manifest.Overrides, plannedDirectory);
        var overrides = OverrideEntries(archive, manifestEntry, overridesPrefix, manifest.Overrides.Length > 0);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in overrides)
        {
            ModpackContentPaths.Validate(item.Relative, plannedDirectory, id);
            if (!paths.Add(item.Relative.Replace('\\', '/'))) throw new InvalidDataException("CurseForge 覆写路径重复：" + item.Relative);
        }
        var loaderKind = ParseLoader(manifest.LoaderId, out var loaderVersion);
        var planned = new List<(CurseForgeFile File, string Folder)>();
        paths.Clear();
        foreach (var entry in manifest.Files.Where(entry => entry.Required || includeOptional &&
                     (selectedOptionalFileIds is null || selectedOptionalFileIds.Contains(entry.FileId))))
        {
            ct.ThrowIfCancellationRequested();
            CurseForgeFile file;
            try
            {
                file = await _client.GetFileByIdAsync(entry.ProjectId, entry.FileId, ct).ConfigureAwait(false);
            }
            catch (CurseForgeFileUnavailableException exception)
            {
                throw new InvalidOperationException(
                    $"CurseForge {(entry.Required ? "必需文件" : "已选择的可选文件")}不可用：项目 {exception.ProjectId} / 文件 {exception.FileId}。请核对文件是否已删除或访问受限。", exception);
            }
            CurseForgeClient.ValidateDownloadMetadata(file);
            if (file.FileName != FileUtil.SafeFileName(file.FileName)) throw new InvalidDataException("CurseForge 文件名必须是单段名称。");
            var folder = CurseForgePackContent.TargetFolder(file);
            var relative = folder + "/" + file.FileName;
            ModpackContentPaths.Validate(relative, plannedDirectory, id);
            if (!paths.Add(relative)) throw new InvalidDataException("CurseForge 文件目标路径冲突：" + relative);
            if (overrides.Any(item => item.Relative.Equals(relative, StringComparison.OrdinalIgnoreCase) && item.Relative != relative))
                throw new InvalidDataException("CurseForge 覆写与下载文件的路径大小写不一致：" + relative);
            planned.Add((file, folder));
        }

        if (loaderKind is "fabric" or "quilt")
        {
            progress?.Report(new JobProgress($"安装 {(loaderKind == "quilt" ? "Quilt" : "Fabric")} 加载器", 0.05));
            var loaderProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                update.Stage, 0.05 + (update.Percent ?? 0) * 0.35, null, null, update.Detail)));
            await (loaderKind == "quilt" ? _quilt : _fabric).InstallMergedAsync(
                manifest.Minecraft, loaderVersion, id, loaderProgress, ct).ConfigureAwait(false);
        }
        else if (loaderKind is "forge" or "neoforge")
        {
            progress?.Report(new JobProgress("准备 Forge/NeoForge 安装器", 0.04));
            var kind = loaderKind == "neoforge" ? ForgelikeKind.NeoForge : ForgelikeKind.Forge;
            var javaPath = await ResolveJavaAsync(
                manifest.Minecraft, kind.ToString(), loaderVersion, ct).ConfigureAwait(false);
            var loaderProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                update.Stage, 0.05 + (update.Percent ?? 0) * 0.35, null, null, update.Detail)));
            await _forgelike.InstallMergedAsync(
                kind, manifest.Minecraft, loaderVersion, id, javaPath,
                loaderProgress, ct).ConfigureAwait(false);
        }
        else
        {
            progress?.Report(new JobProgress("安装原版游戏", 0.05));
            await _installer.InstallAsync(manifest.Minecraft, new InstallOptions { Mirror = MirrorMode.Auto }, progress, ct).ConfigureAwait(false);
            await MergedInstanceBuilder.BuildAsync(_repository.GameDirectory, manifest.Minecraft, id, ct).ConfigureAwait(false);
            await _installer.InstallAsync(id, new InstallOptions { Mirror = MirrorMode.Auto }, progress, ct).ConfigureAwait(false);
        }

        var instanceDirectory = _repository.GetVersionDirectory(id);
        InstanceSettingsStore.Save(instanceDirectory, new InstanceSettings { IsolationMode = IsolationPolicy.Isolated });

        progress?.Report(new JobProgress("下载整合包文件", 0.6));
        var installed = new List<InstallManifestFile>();
        var managedArchives = new Dictionary<string, InstallManifestArchive>();
        var index = 0;
        foreach (var entry in planned)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            var file = entry.File;
            var destination = FileUtil.SafeChildPath(
                Path.Combine(instanceDirectory, entry.Folder), file.FileName);
            var fileProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                "下载整合包文件",
                0.6 + (double)index / Math.Max(planned.Count, 1) * 0.35,
                null, null, file.FileName)));
            if (entry.Folder == "saves")
            {
                var temporary = Path.Combine(Path.GetTempPath(), "wmcl-cf-world-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var archivePath = FileUtil.SafeChildPath(temporary, file.FileName);
                    await CurseForgeClient.DownloadFileAsync(file, archivePath, fileProgress, ct, _downloads).ConfigureAwait(false);
                    var archiveId = "curseforge-" + file.Id;
                    managedArchives.Add(archiveId, new InstallManifestArchive(
                        [file.DownloadUrl, CurseForgeClient.ToMirror(file.DownloadUrl)], file.Sha1,
                        file.Length > 0 ? file.Length : null));
                    foreach (var extracted in CurseForgePackContent.ExtractWorlds(archivePath, instanceDirectory, id, ct))
                        installed.Add(new InstallManifestFile(Path.GetRelativePath(_repository.GameDirectory, extracted.Path).Replace('\\', '/'),
                            await InstallManifestStore.ComputeFileSha1Async(extracted.Path, ct), "CurseForge-world",
                            ArchivePath: extracted.ArchivePath, ArchiveId: archiveId));
                }
                finally
                {
                    if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
                }
            }
            else
            {
                await CurseForgeClient.DownloadFileAsync(file, destination, fileProgress, ct, _downloads).ConfigureAwait(false);
                installed.Add(new InstallManifestFile(
                    Path.GetRelativePath(_repository.GameDirectory, destination).Replace('\\', '/'),
                    await InstallManifestStore.ComputeFileSha1Async(destination, ct), "CurseForge", file.DownloadUrl));
            }
        }

        progress?.Report(new JobProgress("解压整合包覆写", 0.96));
        ct.ThrowIfCancellationRequested();
        var overrideFiles = ExtractOverrides(overrides, instanceDirectory, ct);
        foreach (var path in overrideFiles)
        {
            var relative = Path.GetRelativePath(_repository.GameDirectory, path).Replace('\\', '/');
            installed.RemoveAll(file => file.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase));
            installed.Add(new InstallManifestFile(
                relative, await InstallManifestStore.ComputeFileSha1Async(path, ct), "overrides"));
        }
        installed.AddRange(await InstallManifestStore.CollectManagedFilesAsync(
            _repository.GameDirectory, id, "merged", ct).ConfigureAwait(false));
        ct.ThrowIfCancellationRequested();
        InstallManifestStore.Save(_repository.GameDirectory, id, new InstallManifest
        {
            SourceArchive = Path.GetFullPath(zipPath),
            OverridesPrefix = overridesPrefix,
            OverridesPrefixes = [overridesPrefix],
            OverridesRoot = instanceDirectory,
            Components =
            {
                ["minecraft"] = manifest.Minecraft,
                [loaderKind] = loaderVersion,
                ["curseforge-pack"] = manifest.Name + " " + manifest.Version
            },
            ManagedFiles = installed,
            ManagedArchives = managedArchives
        });
        progress?.Report(new JobProgress("整合包安装完成", 1));
        return id;
    }

    private static string ParseLoader(string loaderId, out string version)
    {
        var value = loaderId.Trim();
        if (value.Length == 0)
        {
            version = "";
            return "vanilla";
        }
        foreach (var prefix in new[] { "fabric-", "quilt-", "forge-", "neoforge-", "neo-forge-" })
        {
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            version = value[prefix.Length..];
            if (string.IsNullOrWhiteSpace(version) || version.Contains("recommended", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("CurseForge 加载器必须指定实际版本：" + loaderId);
            if (prefix is "forge-" or "neoforge-" or "neo-forge-" &&
                version.StartsWith("1.", StringComparison.Ordinal) &&
                version.Count(c => c == '-') >= 1)
            {
                var parts = version.Split('-', 2);
                version = parts[1];
            }
            InstanceManager.ValidateName(version);
            return prefix.StartsWith("quilt", StringComparison.OrdinalIgnoreCase) ? "quilt"
                : prefix.StartsWith("fabric", StringComparison.OrdinalIgnoreCase) ? "fabric"
                : prefix.StartsWith("neo", StringComparison.OrdinalIgnoreCase) ? "neoforge" : "forge";
        }
        throw new NotSupportedException($"CurseForge 整合包的加载器“{loaderId}”不受支持（支持原版/Fabric/Quilt/Forge/NeoForge）。");
    }

    private static List<string> ExtractOverrides(
        IReadOnlyList<(ZipArchiveEntry Entry, string Relative)> entries, string instanceDirectory, CancellationToken ct)
    {
        var extracted = new List<string>();
        foreach (var item in entries)
        {
            ct.ThrowIfCancellationRequested();
            var destination = FileUtil.SafeChildPath(instanceDirectory, item.Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            item.Entry.ExtractToFile(destination, overwrite: true);
            extracted.Add(destination);
        }
        return extracted;
    }

    public static bool IsArchive(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.GetEntry("modrinth.index.json") is not null) return false;
            FindManifestEntry(archive);
            return true;
        }
        catch { return false; }
    }

    private static ZipArchiveEntry FindManifestEntry(ZipArchive archive)
    {
        var candidates = archive.Entries.Where(entry => entry.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName.Count(character => character == '/')).ToList();
        if (candidates.Count == 0) throw new InvalidDataException("不是有效的 CurseForge 整合包（缺少 manifest.json）。");
        var depth = candidates[0].FullName.Count(character => character == '/');
        if (candidates.Count(entry => entry.FullName.Count(character => character == '/') == depth) != 1)
            throw new InvalidDataException("CurseForge 整合包存在多个 manifest.json，无法确定包根。");
        return candidates[0];
    }

    private static string OverridePrefix(string baseFolder, string declared, string directory)
    {
        if (baseFolder.Length > 0) FileUtil.SafeChildPath(directory, baseFolder.TrimEnd('/'));
        if (declared.Length == 0 || declared is "." or "./") return baseFolder;
        var normalized = declared.Replace('\\', '/').TrimEnd('/');
        FileUtil.SafeChildPath(directory, normalized);
        if (normalized.Split('/').Any(part => part is "" or ".") || normalized.Contains(':'))
            throw new InvalidDataException("CurseForge overrides 路径无效。");
        return baseFolder + normalized + "/";
    }

    private static List<(ZipArchiveEntry Entry, string Relative)> OverrideEntries(
        ZipArchive archive, ZipArchiveEntry manifest, string prefix, bool enabled) => !enabled ? [] :
        archive.Entries.Where(entry => !entry.FullName.EndsWith('/') && entry != manifest && entry.FullName.StartsWith(prefix, StringComparison.Ordinal))
            .Select(entry => (entry, entry.FullName[prefix.Length..])).ToList();

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
        if (string.IsNullOrWhiteSpace(name)) name = "CurseForgePack";
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
}
