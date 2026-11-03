using System.IO.Compression;
using System.Text.Json;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Download;

namespace WMCL.Minecraft;

public sealed record InstanceImportResult(
    string Id,
    string VersionDirectory,
    int FileCount);

/// <summary>导入实例导出包，在独立暂存游戏根目录中迁移元数据并补全资源。</summary>
public static class InstanceImporter
{
    public static async Task<InstanceImportResult> ImportAsync(
        string gameRoot,
        string archivePath,
        string? instanceName = null,
        bool installMissing = true,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("导入包不存在。", archivePath);

        using var archive = ZipFile.OpenRead(archivePath);
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/')) name = name[..^1];
            InstanceImportMigration.ValidateRelative(name);
            if (!entries.Add(name) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException("导入包包含重复路径或符号链接：" + entry.FullName);
        }
        var metadataEntry = archive.GetEntry("wmcl-export.json")
                            ?? archive.GetEntry("pcl-export.json")
                            ?? throw new InvalidDataException("不是 WMCL 实例导出包（缺少 wmcl-export.json）。");
        ExportMetadata? metadata;
        using (var stream = metadataEntry.Open())
            metadata = await JsonSerializer.DeserializeAsync<ExportMetadata>(
                stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        var sourceId = metadata?.Id;
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new InvalidDataException("导出包缺少实例 id。");
        InstanceManager.ValidateName(sourceId);

        var root = Path.GetFullPath(gameRoot);
        var targetId = CreateInstanceId(root, instanceName ?? sourceId);
        var targetDirectory = FileUtil.SafeChildPath(root, "versions/" + targetId);
        EnsureRegularPath(targetDirectory);
        var temporary = Path.Combine(root, ".wmcl-import-" + Guid.NewGuid().ToString("N"));
        var stagedDirectory = Path.Combine(temporary, "versions", targetId);
        var publishedFiles = new List<string>();
        var publishedInstance = false;
        var fileCount = 0;
        try
        {
            Directory.CreateDirectory(stagedDirectory);
            progress?.Report(new JobProgress("解压实例数据", 0.02));
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith('/') || !name.StartsWith("instance/", StringComparison.Ordinal)) continue;
                var relative = InstanceImportMigration.MapLocal(name["instance/".Length..], sourceId, targetId);
                var destination = FileUtil.SafeChildPath(stagedDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var input = entry.Open();
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
                fileCount++;
            }

            await InstanceImportMigration.RewriteAsync(stagedDirectory, targetDirectory, sourceId, targetId,
                metadata?.Minecraft, ct).ConfigureAwait(false);
            if (installMissing)
            {
                progress?.Report(new JobProgress("补全实例资源", 0.5));
                var adapter = new ProgressAdapter(update => progress?.Report(new JobProgress(
                    update.Stage, 0.5 + (update.Percent ?? 0) * 0.45, null, null, update.Detail)));
                await new VersionInstaller(new VersionRepository(temporary))
                    .InstallAsync(targetId, new InstallOptions { Mirror = MirrorMode.Auto }, adapter, ct)
                    .ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            var targetLibraries = InstanceRelocation.LibraryDirectory(root, targetId);
            EnsureRegularPath(targetLibraries);
            if (Path.Exists(targetLibraries)) throw new IOException("实例专属库目录已存在：" + targetId);
            foreach (var category in new[] { "libraries", "assets" })
            {
                var staged = Path.Combine(temporary, category);
                if (!Directory.Exists(staged)) continue;
                foreach (var file in Directory.EnumerateFiles(staged, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var destination = FileUtil.SafeChildPath(root, Path.GetRelativePath(temporary, file));
                    EnsureRegularPath(destination);
                    if (Path.Exists(destination))
                    {
                        if (!File.Exists(destination) || !string.Equals(
                                await InstallManifestStore.ComputeFileSha1Async(file, ct).ConfigureAwait(false),
                                await InstallManifestStore.ComputeFileSha1Async(destination, ct).ConfigureAwait(false),
                                StringComparison.OrdinalIgnoreCase))
                            throw new IOException("已有共享资源与导入资源内容不同：" + destination);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(file, destination);
                    publishedFiles.Add(destination);
                }
            }
            EnsureRegularPath(targetDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(targetDirectory)!);
            Directory.Move(stagedDirectory, targetDirectory);
            publishedInstance = true;
            progress?.Report(new JobProgress(installMissing ? "导入完成" : "导入完成（未补全库与资源）", 1));
            ct.ThrowIfCancellationRequested();
            return new InstanceImportResult(targetId, targetDirectory, fileCount);
        }
        catch
        {
            if (publishedInstance) Directory.Delete(targetDirectory, recursive: true);
            foreach (var path in publishedFiles) File.Delete(path);
            var libraries = InstanceRelocation.LibraryDirectory(root, targetId);
            if (publishedFiles.Any(path => path.StartsWith(libraries + Path.DirectorySeparatorChar, StringComparison.Ordinal)) &&
                Directory.Exists(libraries) && !Directory.EnumerateFiles(libraries, "*", SearchOption.AllDirectories).Any())
                Directory.Delete(libraries, recursive: true);
            throw;
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    private static string CreateInstanceId(string gameRoot, string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(raw.Select(character => invalid.Contains(character) || character is '/' or '\\' or ':' ? '-' : character).ToArray()).Trim('-', ' ');
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") name = "Imported";
        if (name.Length > 64) name = name[..64];
        var candidate = name;
        var suffix = 2;
        while (Path.Exists(Path.Combine(gameRoot, "versions", candidate)) ||
               Path.Exists(InstanceRelocation.LibraryDirectory(gameRoot, candidate)))
        {
            var ending = "-" + suffix++;
            candidate = name[..Math.Min(name.Length, 64 - ending.Length)] + ending;
        }
        InstanceManager.ValidateName(candidate);
        return candidate;
    }

    private static void EnsureRegularPath(string path)
    {
        ExportSafety.ValidatePath(path);
    }

    private sealed class ExportMetadata
    {
        public string? Id { get; set; }
        public string? Minecraft { get; set; }
        public string? Loader { get; set; }
    }

    private sealed class ProgressAdapter(Action<JobProgress> action) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => action(value);
    }
}
