using System.IO.Compression;
using System.Text.Json;
using PCL.Core;
using PCL.Core.Jobs;
using PCL.Download;
using PCL.Minecraft;
using PCL.Modding.CurseForge;
using PCL.Modding.Fabric;
using PCL.Modding.Forgelike;

namespace PCL.Modding.Modpack;

public sealed record CurseForgeManifestFile(long ProjectId, long FileId, bool Required);

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
    private readonly ForgelikeInstaller _forgelike;
    private readonly CurseForgeClient _client;
    private readonly Func<int, CancellationToken, Task<string>>? _javaResolver;
    private readonly string? _isolationMode;

    public CurseForgeModpackInstaller(
        VersionRepository repository,
        VersionInstaller? installer = null,
        FabricInstaller? fabric = null,
        ForgelikeInstaller? forgelike = null,
        CurseForgeClient? client = null,
        Func<int, CancellationToken, Task<string>>? javaResolver = null,
        string? isolationMode = null)
    {
        _repository = repository;
        _installer = installer ?? new VersionInstaller(repository);
        _fabric = fabric ?? new FabricInstaller(repository, _installer);
        _forgelike = forgelike ?? new ForgelikeInstaller(repository, _installer);
        _client = client ?? new CurseForgeClient();
        _javaResolver = javaResolver;
        _isolationMode = isolationMode;
    }

    public static CurseForgeManifest ParseManifest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("minecraft", out var minecraft) ||
            !minecraft.TryGetProperty("version", out var versionElement))
            throw new InvalidDataException("CurseForge 整合包缺少 minecraft.version。");

        var loader = "";
        if (minecraft.TryGetProperty("modLoaders", out var loaders) &&
            loaders.ValueKind == JsonValueKind.Array)
        {
            var primary = loaders.EnumerateArray()
                .FirstOrDefault(item => item.TryGetProperty("primary", out var p) && p.GetBoolean());
            if (primary.ValueKind == JsonValueKind.Undefined)
                primary = loaders.EnumerateArray().FirstOrDefault();
            if (primary.ValueKind == JsonValueKind.Object &&
                primary.TryGetProperty("id", out var idElement))
                loader = idElement.GetString() ?? "";
        }

        var files = new List<CurseForgeManifestFile>();
        if (root.TryGetProperty("files", out var filesElement) &&
            filesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in filesElement.EnumerateArray())
            {
                var project = file.TryGetProperty("projectID", out var p) ? p.GetInt64() : 0;
                var fileId = file.TryGetProperty("fileID", out var f) ? f.GetInt64() : 0;
                var required = !file.TryGetProperty("required", out var r) || r.GetBoolean();
                if (project > 0 && fileId > 0)
                    files.Add(new CurseForgeManifestFile(project, fileId, required));
            }
        }

        return new CurseForgeManifest(
            root.TryGetProperty("name", out var name) ? name.GetString() ?? "CurseForge Pack" : "CurseForge Pack",
            root.TryGetProperty("version", out var packVersion) ? packVersion.GetString() ?? "1.0" : "1.0",
            versionElement.GetString() ?? throw new InvalidDataException("缺少 Minecraft 版本。"),
            loader,
            files,
            root.TryGetProperty("overrides", out var overrides) ? overrides.GetString() ?? "overrides" : "overrides");
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
        var destination = Path.Combine(PclPaths.CacheDirectory, "modpacks", file.FileName);
        if (!File.Exists(destination))
            await CurseForgeClient.DownloadFileAsync(file, destination, progress, ct)
                .ConfigureAwait(false);
        return await InstallFromFileAsync(
                destination, instanceName, progress, ct, includeOptional)
            .ConfigureAwait(false);
    }

    public async Task<string> InstallFromFileAsync(
        string zipPath,
        string? instanceName = null,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default,
        bool includeOptional = true)
    {
        progress?.Report(new JobProgress("解析 CurseForge 整合包", 0.02));
        using var archive = ZipFile.OpenRead(zipPath);
        var manifestEntry = archive.GetEntry("manifest.json")
                            ?? throw new InvalidDataException("不是有效的 CurseForge 整合包（缺少 manifest.json）。");
        string manifestJson;
        using (var stream = manifestEntry.Open())
        using (var reader = new StreamReader(stream))
            manifestJson = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        var manifest = ParseManifest(manifestJson);
        var id = CreateInstanceId(instanceName ?? manifest.Name + "-" + manifest.Version);
        var loaderKind = ParseLoader(manifest.LoaderId, out var loaderVersion);

        string sourceId;
        if (loaderKind == "fabric")
        {
            progress?.Report(new JobProgress("安装 Fabric 加载器", 0.05));
            var loaderProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                update.Stage, 0.05 + (update.Percent ?? 0) * 0.35, null, null, update.Detail)));
            sourceId = await _fabric.InstallAsync(
                manifest.Minecraft, loaderVersion, loaderProgress, ct).ConfigureAwait(false);
        }
        else if (loaderKind is "forge" or "neoforge")
        {
            if (_javaResolver is null)
                throw new InvalidOperationException("安装 Forge/NeoForge 整合包需要 Java。");
            progress?.Report(new JobProgress("准备 Forge/NeoForge 安装器", 0.04));
            var javaPath = await _javaResolver(InferJavaMajor(manifest.Minecraft), ct)
                .ConfigureAwait(false);
            var kind = loaderKind == "neoforge" ? ForgelikeKind.NeoForge : ForgelikeKind.Forge;
            var loaderProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                update.Stage, 0.05 + (update.Percent ?? 0) * 0.35, null, null, update.Detail)));
            sourceId = await _forgelike.InstallAsync(
                kind, manifest.Minecraft, loaderVersion, javaPath,
                loaderProgress, null, ct).ConfigureAwait(false);
        }
        else
        {
            throw new NotSupportedException(
                $"CurseForge 整合包的加载器“{manifest.LoaderId}”不受支持（支持 Fabric/Forge/NeoForge）。");
        }

        progress?.Report(new JobProgress("创建整合包实例", 0.42));
        await MergedInstanceBuilder.BuildAsync(_repository.GameDirectory, sourceId, id, ct)
            .ConfigureAwait(false);
        InstanceManager.Delete(_repository.GameDirectory, sourceId);
        await _installer.InstallAsync(
            id, new InstallOptions { Mirror = MirrorMode.Auto }, progress, ct).ConfigureAwait(false);
        var instanceDirectory = _repository.GetVersionDirectory(id);
        var version = await _repository.GetVersionAsync(id, ct).ConfigureAwait(false);
        var gameDirectory = IsolationPolicy.GetGameDirectory(
            _repository.GameDirectory, id, version, _isolationMode);

        progress?.Report(new JobProgress("解压整合包覆写", 0.5));
        ExtractOverrides(archive, instanceDirectory, manifest.Overrides);

        progress?.Report(new JobProgress("下载整合包文件", 0.6));
        var installed = new List<InstallManifestFile>();
        var index = 0;
        foreach (var entry in manifest.Files.Where(entry => includeOptional || entry.Required))
        {
            ct.ThrowIfCancellationRequested();
            index++;
            var file = await _client.GetFileByIdAsync(entry.ProjectId, entry.FileId, ct)
                .ConfigureAwait(false);
            var destination = Path.Combine(gameDirectory, "mods", file.FileName);
            if (File.Exists(destination))
                continue;
            var fileProgress = new ProgressAdapter(update => progress?.Report(new JobProgress(
                "下载整合包文件",
                0.6 + (double)index / Math.Max(manifest.Files.Count, 1) * 0.35,
                null, null, file.FileName)));
            await CurseForgeClient.DownloadFileAsync(file, destination, fileProgress, ct)
                .ConfigureAwait(false);
            installed.Add(new InstallManifestFile(
                "mods/" + file.FileName, file.Sha1 ?? "", "CurseForge", file.DownloadUrl));
        }

        installed.AddRange(await InstallManifestStore.CollectManagedFilesAsync(
            _repository.GameDirectory, id, "merged", ct).ConfigureAwait(false));
        InstallManifestStore.Save(_repository.GameDirectory, id, new InstallManifest
        {
            Components =
            {
                ["minecraft"] = manifest.Minecraft,
                [loaderKind] = loaderVersion,
                ["curseforge-pack"] = manifest.Name + " " + manifest.Version
            },
            ManagedFiles = installed
        });
        progress?.Report(new JobProgress("整合包安装完成", 1));
        return id;
    }

    private static string ParseLoader(string loaderId, out string version)
    {
        var value = loaderId.Trim();
        foreach (var prefix in new[] { "fabric-", "forge-", "neoforge-", "neo-forge-" })
        {
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            version = value[prefix.Length..];
            if (prefix is "forge-" or "neoforge-" or "neo-forge-" &&
                version.StartsWith("1.", StringComparison.Ordinal) &&
                version.Count(c => c == '-') >= 1)
            {
                var parts = version.Split('-', 2);
                version = parts[1];
            }
            return prefix.StartsWith("fabric", StringComparison.OrdinalIgnoreCase) ? "fabric"
                : prefix.StartsWith("neo", StringComparison.OrdinalIgnoreCase) ? "neoforge" : "forge";
        }
        version = "";
        return "";
    }

    private static void ExtractOverrides(ZipArchive archive, string instanceDirectory, string prefix)
    {
        var normalizedPrefix = prefix.TrimEnd('/') + "/";
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') ||
                !entry.FullName.StartsWith(normalizedPrefix, StringComparison.Ordinal)) continue;
            var relative = entry.FullName[normalizedPrefix.Length..];
            var destination = SafePath(instanceDirectory, relative);
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
        if (string.IsNullOrWhiteSpace(name)) name = "CurseForgePack";
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
