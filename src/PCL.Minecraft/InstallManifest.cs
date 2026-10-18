using System.Text.Json;

namespace PCL.Minecraft;

public sealed class InstallManifest
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, string> Components { get; set; } = [];
    public DateTimeOffset LastModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<InstallManifestFile> ManagedFiles { get; set; } = [];
}

public sealed record InstallManifestFile(
    string RelativePath, string Sha1, string? Component, string? Url = null);

public sealed record InstallManifestVerification(
    int Checked,
    int Missing,
    int Corrupted,
    IReadOnlyList<string> Problems)
{
    public bool IsHealthy => Missing == 0 && Corrupted == 0;
}

/// <summary>实例安装清单，对应 PCL2 McInstallManifest 的最小可用子集。</summary>
public static class InstallManifestStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string GetPath(string gameRoot, string versionId) =>
        Path.Combine(gameRoot, "versions", versionId, "PCL", "install.json");

    public static InstallManifest? Load(string gameRoot, string versionId)
    {
        var path = GetPath(gameRoot, versionId);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(path), Options);
    }

    /// <summary>收集实例 JSON、客户端 jar 与 natives 作为受管文件。</summary>
    public static async Task<List<InstallManifestFile>> CollectManagedFilesAsync(
        string gameRoot,
        string versionId,
        string component = "merged",
        CancellationToken ct = default)
    {
        var versionDirectory = Path.Combine(gameRoot, "versions", versionId);
        var result = new List<InstallManifestFile>();
        var candidates = new List<string>
        {
            Path.Combine(versionDirectory, versionId + ".json"),
            Path.Combine(versionDirectory, versionId + ".jar")
        };
        var natives = Path.Combine(versionDirectory, "natives");
        if (Directory.Exists(natives))
            candidates.AddRange(Directory.EnumerateFiles(natives, "*", SearchOption.AllDirectories));
        foreach (var file in candidates.Where(File.Exists))
        {
            var relative = Path.GetRelativePath(gameRoot, file).Replace('\\', '/');
            var hash = await Sha1Async(file, ct).ConfigureAwait(false);
            result.Add(new InstallManifestFile(relative, hash, component));
        }
        return result;
    }

    private static async Task<string> Sha1Async(string file, CancellationToken ct)
    {
        await using var stream = File.OpenRead(file);
        var hash = await System.Security.Cryptography.SHA1.HashDataAsync(stream, ct)
            .ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    public static async Task<InstallManifestVerification> VerifyAsync(
        string gameRoot,
        string versionId,
        CancellationToken ct = default)
    {
        var manifest = Load(gameRoot, versionId);
        if (manifest is null)
            return new InstallManifestVerification(0, 0, 0, ["没有找到 PCL/install.json"]);
        var missing = 0;
        var corrupted = 0;
        var problems = new List<string>();
        foreach (var file in manifest.ManagedFiles)
        {
            var fullPath = Path.Combine(gameRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                missing++;
                problems.Add("缺失：" + file.RelativePath);
                continue;
            }
            var hash = await Sha1Async(fullPath, ct).ConfigureAwait(false);
            if (!string.Equals(hash, file.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                corrupted++;
                problems.Add("哈希不匹配：" + file.RelativePath);
            }
        }
        return new InstallManifestVerification(
            manifest.ManagedFiles.Count, missing, corrupted, problems);
    }

    public static void Save(string gameRoot, string versionId, InstallManifest manifest)
    {
        var path = GetPath(gameRoot, versionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, Options));
    }
}
