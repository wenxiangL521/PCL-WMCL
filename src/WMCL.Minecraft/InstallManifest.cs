using System.Text.Json;
using WMCL.Core.IO;

namespace WMCL.Minecraft;

public sealed class InstallManifest
{
    public int SchemaVersion { get; set; } = InstallManifestStore.SupportedSchemaVersion;
    public Dictionary<string, string> Components { get; set; } = [];
    public Dictionary<string, JsonElement> ComponentMetadata { get; set; } = [];
    public DateTimeOffset LastModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? SourceArchive { get; set; }
    public string? OverridesPrefix { get; set; }
    public List<string> OverridesPrefixes { get; set; } = [];
    public List<string> OverridesExcludedPaths { get; set; } = [];
    public string? OverridesRoot { get; set; }
    public string? ModpackKind { get; set; }
    public string? ModpackManifestJson { get; set; }
    public Dictionary<string, string> ModpackFileHashes { get; set; } = [];
    public int? McbbsMinMemoryMb { get; set; }
    public List<int> McbbsSupportJava { get; set; } = [];
    public string? McbbsAuthlibInjectorServer { get; set; }
    public List<InstallManifestFile> ManagedFiles { get; set; } = [];
    public Dictionary<string, InstallManifestArchive> ManagedArchives { get; set; } = [];
}

public sealed record InstallManifestFile(
    string RelativePath, string Sha1, string? Component, string? Url = null, string? ArchivePath = null,
    string? ArchiveId = null);

public sealed record InstallManifestArchive(IReadOnlyList<string> Urls, string? Sha1 = null, long? Size = null);

public sealed record InstallManifestVerification(
    int Checked,
    int Missing,
    int Corrupted,
    IReadOnlyList<string> Problems)
{
    public bool IsHealthy => Missing == 0 && Corrupted == 0;
    public int PreservedSaves { get; init; }
}

/// <summary>实例安装清单，对应 PCL2 McInstallManifest 的最小可用子集。</summary>
public static class InstallManifestStore
{
    public const int SupportedSchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void ValidateSchemaVersion(int schemaVersion)
    {
        if (schemaVersion > SupportedSchemaVersion)
            throw new InvalidDataException($"安装清单由更新版本的 WMCL 创建（清单版本：{schemaVersion}，当前支持：{SupportedSchemaVersion}），请更新 WMCL 后再修改此实例。");
    }

    public static string GetPath(string gameRoot, string versionId) =>
        Path.Combine(gameRoot, "versions", versionId, "WMCL", "install.json");

    private static string GetLegacyPath(string gameRoot, string versionId) =>
        Path.Combine(gameRoot, "versions", versionId, "PCL", "install.json");

    public static InstallManifest? Load(string gameRoot, string versionId)
    {
        var path = GetPath(gameRoot, versionId);
        if (!File.Exists(path)) path = GetLegacyPath(gameRoot, versionId);
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

    public static async Task<string> ComputeFileSha1Async(string file, CancellationToken ct = default) =>
        await Sha1Async(file, ct).ConfigureAwait(false);

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
            return new InstallManifestVerification(0, 0, 0, ["没有找到 WMCL/install.json（或旧 PCL/install.json）"]);
        var missing = 0;
        var corrupted = 0;
        var preservedSaves = 0;
        var problems = new List<string>();
        foreach (var file in manifest.ManagedFiles)
        {
            ct.ThrowIfCancellationRequested();
            string fullPath;
            try
            {
                fullPath = ManagedFilePath(gameRoot, file.RelativePath, versionId);
            }
            catch (InvalidDataException)
            {
                corrupted++;
                problems.Add("非法路径：" + file.RelativePath);
                continue;
            }
            if (!File.Exists(fullPath))
            {
                missing++;
                problems.Add("缺失：" + file.RelativePath);
                continue;
            }
            if (IsSaveFile(file.RelativePath))
            {
                preservedSaves++;
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
            manifest.ManagedFiles.Count, missing, corrupted, problems)
        { PreservedSaves = preservedSaves };
    }

    internal static bool IsSaveFile(string relativePath)
    {
        var segments = relativePath.Replace('\\', '/').Split('/');
        return segments.Length >= 3 && segments[0].Equals("saves", StringComparison.OrdinalIgnoreCase) ||
               segments.Length >= 5 && segments[0].Equals("versions", StringComparison.OrdinalIgnoreCase) &&
               segments[2].Equals("saves", StringComparison.OrdinalIgnoreCase);
    }

    public static string ManagedFilePath(string gameRoot, string relativePath, string? versionId = null)
    {
        var destination = FileUtil.SafeChildPath(gameRoot, relativePath);
        var segments = Path.GetRelativePath(gameRoot, destination).Replace('\\', '/').Split('/');
        if (versionId is not null && segments.Length > 1 && segments[0].Equals("versions", StringComparison.OrdinalIgnoreCase) &&
            !segments[1].Equals(versionId, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("安装清单指向其他实例，请核对重命名或克隆后的清单：" + relativePath);
        var root = Path.GetFullPath(gameRoot);
        for (var current = destination; current.Length > root.Length; current = Path.GetDirectoryName(current)!)
            if (new FileInfo(current).LinkTarget is not null)
                throw new InvalidDataException("受管文件路径包含符号链接：" + relativePath);
        return destination;
    }

    public static void Save(string gameRoot, string versionId, InstallManifest manifest)
    {
        var path = GetPath(gameRoot, versionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, Options));
    }
}
