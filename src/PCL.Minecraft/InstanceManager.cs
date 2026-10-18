using System.Text.Json.Nodes;

namespace PCL.Minecraft;

/// <summary>实例重命名/删除。对照 PCL 的 McInstance.Rename 与实例页删除逻辑。</summary>
public static class InstanceManager
{
    public static void Rename(string gameRoot, string oldId, string newId)
    {
        if (string.Equals(oldId, newId, StringComparison.Ordinal)) return;
        ValidateName(newId);

        var repository = new VersionRepository(gameRoot);
        var oldDirectory = repository.GetVersionDirectory(oldId);
        var newDirectory = repository.GetVersionDirectory(newId);
        if (!Directory.Exists(oldDirectory))
            throw new DirectoryNotFoundException(oldDirectory);
        if (Directory.Exists(newDirectory))
            throw new IOException($"实例已存在：{newId}");

        var oldJson = Path.Combine(oldDirectory, oldId + ".json");
        if (!File.Exists(oldJson))
            throw new FileNotFoundException("版本 JSON 不存在。", oldJson);

        var node = JsonNode.Parse(File.ReadAllText(oldJson))!;
        node["id"] = newId;

        Directory.Move(oldDirectory, newDirectory);

        var movedJson = Path.Combine(newDirectory, oldId + ".json");
        var newJson = Path.Combine(newDirectory, newId + ".json");
        File.WriteAllText(newJson, node.ToJsonString());
        File.Delete(movedJson);

        var oldJar = Path.Combine(newDirectory, oldId + ".jar");
        if (File.Exists(oldJar))
            File.Move(oldJar, Path.Combine(newDirectory, newId + ".jar"));
    }

    public static void Delete(string gameRoot, string id)
    {
        var repository = new VersionRepository(gameRoot);
        var directory = repository.GetVersionDirectory(id);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("实例名不能为空。", nameof(name));
        if (name is "." or "..")
            throw new ArgumentException("实例名无效。", nameof(name));
        if (name.Length > 64)
            throw new ArgumentException("实例名过长（最多 64 字符）。", nameof(name));
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('/') || name.Contains('\\'))
            throw new ArgumentException("实例名包含非法字符。", nameof(name));
    }
}
