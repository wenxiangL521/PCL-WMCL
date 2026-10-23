namespace WMCL.Minecraft;

/// <summary>实例重命名/删除。对照 PCL2 的 McInstance.Rename 与实例页删除逻辑。</summary>
public static class InstanceManager
{
    public static void Rename(string gameRoot, string oldId, string newId) =>
        Rename(gameRoot, oldId, newId, null);

    public static void Rename(string gameRoot, string oldId, string newId, Action? beforeCommit)
    {
        if (string.Equals(oldId, newId, StringComparison.Ordinal))
        {
            beforeCommit?.Invoke();
            return;
        }
        ValidateName(oldId);
        ValidateName(newId);
        InstanceRelocation.Relocate(gameRoot, oldId, newId, clone: false, beforeCommit);
    }

    public static string Clone(string gameRoot, string sourceId, string? targetName = null)
    {
        ValidateName(sourceId);
        var repository = new VersionRepository(gameRoot);
        var sourceDirectory = repository.GetVersionDirectory(sourceId);
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException(sourceDirectory);

        var sourceJson = Path.Combine(sourceDirectory, sourceId + ".json");
        if (!File.Exists(sourceJson))
            throw new FileNotFoundException("版本 JSON 不存在。", sourceJson);

        var baseName = string.IsNullOrWhiteSpace(targetName) ? sourceId + "-copy" : targetName.Trim();
        ValidateName(baseName);
        var targetId = baseName;
        var suffix = 2;
        while (Path.Exists(repository.GetVersionDirectory(targetId)) ||
               Path.Exists(InstanceRelocation.LibraryDirectory(gameRoot, targetId)))
            targetId = baseName + "-" + suffix++;
        InstanceRelocation.Relocate(gameRoot, sourceId, targetId, clone: true);
        return targetId;
    }

    public static void Delete(string gameRoot, string id)
    {
        var repository = new VersionRepository(gameRoot);
        var directory = repository.GetVersionDirectory(id);
        InstanceDependencies.EnsureCanDelete(gameRoot, id);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        InstanceRelocation.CleanUnusedLibraries(gameRoot, id);
    }

    public static void ValidateName(string name)
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
