using System.Text.Json;

namespace PCL.Minecraft;

public sealed class InstallManifest
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, string> Components { get; set; } = [];
    public DateTimeOffset LastModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<InstallManifestFile> ManagedFiles { get; set; } = [];
}

public sealed record InstallManifestFile(string RelativePath, string Sha1, string? Component);

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

    public static void Save(string gameRoot, string versionId, InstallManifest manifest)
    {
        var path = GetPath(gameRoot, versionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, Options));
    }
}
