using System.Text.Json;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft;

/// <summary>
/// 把带 inheritsFrom 的加载器版本展平成独立实例 JSON，并复制客户端 jar。
/// 对应 PCL2 MergeJson + 复制客户端 jar 的简化版：目标实例不再依赖基础加载器实例。
/// </summary>
public static class MergedInstanceBuilder
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static async Task<string> BuildAsync(
        string gameRoot,
        string baseVersionId,
        string targetId,
        CancellationToken ct = default)
    {
        InstanceManager.ValidateName(targetId);
        var repository = new VersionRepository(gameRoot);
        var targetDirectory = repository.GetVersionDirectory(targetId);
        if (Directory.Exists(targetDirectory))
            throw new IOException($"实例已存在：{targetId}");

        var merged = await repository.GetVersionAsync(baseVersionId, ct).ConfigureAwait(false);
        merged.Id = targetId;
        merged.InheritsFrom = null;

        Directory.CreateDirectory(targetDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(targetDirectory, targetId + ".json"),
            JsonSerializer.Serialize(merged, Options),
            ct).ConfigureAwait(false);

        var baseDirectory = repository.GetVersionDirectory(baseVersionId);
        var jar = Path.Combine(baseDirectory, baseVersionId + ".jar");
        if (!File.Exists(jar))
        {
            var vanilla = MinecraftVersionUtil.GetBaseGameVersion(baseVersionId, merged);
            jar = Path.Combine(repository.GetVersionDirectory(vanilla), vanilla + ".jar");
        }
        if (!File.Exists(jar))
            throw new FileNotFoundException(
                $"无法找到 {baseVersionId} 的客户端 jar，不能创建合并实例。", jar);

        File.Copy(jar, Path.Combine(targetDirectory, targetId + ".jar"), overwrite: true);
        return targetId;
    }
}
