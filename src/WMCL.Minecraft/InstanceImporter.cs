using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.Core.Jobs;
using WMCL.Download;

namespace WMCL.Minecraft;

public sealed record InstanceImportResult(
    string Id,
    string VersionDirectory,
    int FileCount);

/// <summary>
/// 导入 InstanceExporter 生成的 zip：改写版本 id、解压实例数据，并补全缺失的库/资源。
/// </summary>
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
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("导入包不存在。", archivePath);

        using var archive = ZipFile.OpenRead(archivePath);
        var metadataEntry = archive.GetEntry("wmcl-export.json")
                            ?? archive.GetEntry("pcl-export.json")
                            ?? throw new InvalidDataException(
                                "不是 WMCL 实例导出包（缺少 wmcl-export.json）。");
        ExportMetadata? metadata;
        using (var stream = metadataEntry.Open())
            metadata = await JsonSerializer.DeserializeAsync<ExportMetadata>(
                stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

        var sourceId = metadata?.Id;
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new InvalidDataException("导出包缺少实例 id。");

        var repository = new VersionRepository(gameRoot);
        var targetId = CreateInstanceId(gameRoot, instanceName ?? sourceId);
        var targetDirectory = repository.GetVersionDirectory(targetId);
        Directory.CreateDirectory(targetDirectory);

        progress?.Report(new JobProgress("解压实例数据", 0.02));
        var fileCount = 0;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/')) continue;
            if (!entry.FullName.StartsWith("instance/", StringComparison.Ordinal)) continue;

            var relative = entry.FullName["instance/".Length..].Replace('\\', '/');
            if (relative.Length == 0) continue;

            var destination = SafePath(targetDirectory, relative);
            if (destination is null) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (relative.Equals(sourceId + ".json", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(entry.Open());
                var json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                var node = JsonNode.Parse(json)!;
                node["id"] = targetId;
                await File.WriteAllTextAsync(
                    Path.Combine(targetDirectory, targetId + ".json"),
                    node.ToJsonString(), ct).ConfigureAwait(false);
                fileCount++;
            }
            else if (relative.Equals(sourceId + ".jar", StringComparison.OrdinalIgnoreCase))
            {
                await using var input = entry.Open();
                await using var output = File.Create(
                    Path.Combine(targetDirectory, targetId + ".jar"));
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
                fileCount++;
            }
            else
            {
                entry.ExtractToFile(destination, overwrite: true);
                fileCount++;
            }
        }

        var targetJson = Path.Combine(targetDirectory, targetId + ".json");
        if (!File.Exists(targetJson))
            throw new InvalidDataException("导入包中没有版本 JSON。");

        if (installMissing)
        {
            progress?.Report(new JobProgress("补全实例资源", 0.5));
            var adapter = new ProgressAdapter(update => progress?.Report(new JobProgress(
                update.Stage, 0.5 + (update.Percent ?? 0) * 0.5, null, null, update.Detail)));
            await new VersionInstaller(repository)
                .InstallAsync(targetId, new InstallOptions { Mirror = MirrorMode.Auto }, adapter, ct)
                .ConfigureAwait(false);
        }

        progress?.Report(new JobProgress("导入完成", 1));
        return new InstanceImportResult(targetId, targetDirectory, fileCount);
    }

    private static string CreateInstanceId(string gameRoot, string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(raw.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim('-', ' ');
        if (string.IsNullOrWhiteSpace(name)) name = "Imported";
        if (name.Length > 64) name = name[..64];

        var candidate = name;
        var suffix = 2;
        while (Directory.Exists(Path.Combine(gameRoot, "versions", candidate)))
            candidate = name + "-" + suffix++;
        return candidate;
    }

    private static string? SafePath(string root, string relative)
    {
        var normalized = relative.TrimStart('/');
        if (normalized.Split('/').Contains("..")) return null;
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        return full.StartsWith(rootFull, StringComparison.Ordinal) ? full : null;
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
