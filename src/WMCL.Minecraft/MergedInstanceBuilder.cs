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
        => await BuildAsync(gameRoot, baseVersionId, gameRoot, targetId, ct).ConfigureAwait(false);

    public static async Task<string> BuildAsync(
        string sourceGameRoot,
        string baseVersionId,
        string targetGameRoot,
        string targetId,
        CancellationToken ct = default)
    {
        InstanceManager.ValidateName(targetId);
        var repository = new VersionRepository(sourceGameRoot);
        var targetDirectory = new VersionRepository(targetGameRoot).GetVersionDirectory(targetId);
        if (Directory.Exists(targetDirectory))
            throw new IOException($"实例已存在：{targetId}");

        var merged = await repository.GetVersionAsync(baseVersionId, ct).ConfigureAwait(false);
        var clientId = baseVersionId;
        var clientVersion = merged;
        var jar = Path.Combine(repository.GetVersionDirectory(clientId), clientId + ".jar");
        while (!File.Exists(jar) && !string.IsNullOrWhiteSpace(clientVersion.InheritsFrom))
        {
            clientId = clientVersion.InheritsFrom;
            clientVersion = await repository.GetVersionAsync(clientId, ct).ConfigureAwait(false);
            jar = Path.Combine(repository.GetVersionDirectory(clientId), clientId + ".jar");
        }
        if (!File.Exists(jar))
            throw new FileNotFoundException(
                $"无法找到 {baseVersionId} 的客户端 jar，不能创建合并实例。", jar);
        if (merged.Downloads?.TryGetValue("client", out var client) == true && client.Path == merged.Id + ".jar")
            client.Path = targetId + ".jar";
        merged.Id = targetId;
        merged.InheritsFrom = null;

        Directory.CreateDirectory(targetDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(targetDirectory, targetId + ".json"),
            JsonSerializer.Serialize(merged, Options),
            ct).ConfigureAwait(false);

        File.Copy(jar, Path.Combine(targetDirectory, targetId + ".jar"), overwrite: true);
        return targetId;
    }
}
