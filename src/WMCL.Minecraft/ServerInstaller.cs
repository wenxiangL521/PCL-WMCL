using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft;

public sealed record ServerInstallResult(
    string ServerJar,
    string EulaFile,
    string PropertiesFile,
    bool Downloaded);

/// <summary>
/// 准备 Java 版服务端文件：downloads.server → server.jar，并生成 eula=false /
/// server.properties 模板。不会替用户接受 EULA，也不会启动服务端。
/// </summary>
public static class ServerInstaller
{
    public static async Task<ServerInstallResult> EnsureAsync(
        string gameRoot,
        string versionId,
        DownloadManager? downloads = null,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        var repository = new VersionRepository(gameRoot);
        var directory = repository.GetVersionDirectory(versionId);
        var version = await repository.GetVersionAsync(versionId, ct).ConfigureAwait(false);
        if (version.Downloads is null ||
            !version.Downloads.TryGetValue("server", out var server) ||
            string.IsNullOrWhiteSpace(server.Url))
            throw new InvalidOperationException(
                $"版本 {versionId} 没有可用的服务端下载（可能是加载器实例或旧版本）。");

        var jar = FileUtil.SafeChildPath(
            directory, string.IsNullOrWhiteSpace(server.Path) ? "server.jar" : server.Path);
        var downloaded = false;
        if (!File.Exists(jar))
        {
            progress?.Report(new JobProgress("下载服务端", 0.1, Detail: versionId));
            var manager = downloads ?? new DownloadManager();
            var item = DownloadItem.Create(
                server.Url, jar, server.Sha1, server.Size);
            await manager.DownloadAsync([item], ct: ct).ConfigureAwait(false);
            downloaded = true;
        }

        var eula = Path.Combine(directory, "eula.txt");
        if (!File.Exists(eula))
            await File.WriteAllTextAsync(eula,
                "# 将 eula=false 改为 true 表示你同意 Minecraft EULA\n"
                + "eula=false\n", ct).ConfigureAwait(false);
        var properties = Path.Combine(directory, "server.properties");
        if (!File.Exists(properties))
            await File.WriteAllTextAsync(properties,
                "server-port=25565\nonline-mode=true\nmotd=WMCL Server\n", ct)
                .ConfigureAwait(false);
        progress?.Report(new JobProgress("服务端文件就绪", 1, Detail: jar));
        return new ServerInstallResult(jar, eula, properties, downloaded);
    }
}
