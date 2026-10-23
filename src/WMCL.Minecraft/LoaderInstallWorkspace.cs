using System.Text.Json;
using WMCL.Core.IO;
using WMCL.Core.Logging;

namespace WMCL.Minecraft;

public sealed class LoaderInstallWorkspace : IDisposable
{
    private readonly VersionRepository _target;

    private LoaderInstallWorkspace(VersionRepository target)
    {
        _target = target;
        Repository = new VersionRepository(Path.Combine(Path.GetTempPath(), "wmcl-loader-" + Guid.NewGuid().ToString("N")));
    }

    public VersionRepository Repository { get; }

    public static async Task<LoaderInstallWorkspace> CreateAsync(
        VersionRepository target, string minecraft, CancellationToken ct = default)
    {
        InstanceManager.ValidateName(minecraft);
        ct.ThrowIfCancellationRequested();
        var workspace = new LoaderInstallWorkspace(target);
        try
        {
            var version = await target.GetVersionAsync(minecraft, ct).ConfigureAwait(false);
            var client = Path.Combine(target.GetVersionDirectory(minecraft), minecraft + ".jar");
            if (!File.Exists(client)) throw new FileNotFoundException("临时加载器安装缺少原版客户端。", client);
            var vanillaDirectory = workspace.Repository.GetVersionDirectory(minecraft);
            Directory.CreateDirectory(vanillaDirectory);
            version.Id = minecraft;
            version.InheritsFrom = null;
            await File.WriteAllTextAsync(Path.Combine(vanillaDirectory, minecraft + ".json"),
                JsonSerializer.Serialize(version), ct).ConfigureAwait(false);
            File.Copy(client, Path.Combine(vanillaDirectory, minecraft + ".jar"));
            foreach (var library in LibraryResolver.Resolve(version, RuleContext.CreateDefault()))
            {
                ct.ThrowIfCancellationRequested();
                var source = FileUtil.SafeChildPath(Path.Combine(target.GameDirectory, "libraries"), library.RelativePath);
                if (!File.Exists(source)) continue;
                var destination = FileUtil.SafeChildPath(Path.Combine(workspace.Repository.GameDirectory, "libraries"), library.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }
            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    public async Task MergeAsync(string loaderId, string instanceId, CancellationToken ct = default)
    {
        InstanceManager.ValidateName(loaderId);
        InstanceManager.ValidateName(instanceId);
        if (Directory.Exists(_target.GetVersionDirectory(instanceId))) throw new IOException("实例已存在：" + instanceId);
        ct.ThrowIfCancellationRequested();
        var profile = Path.Combine(Repository.GetVersionDirectory(loaderId), loaderId + ".json");
        if (!File.Exists(profile)) throw new FileNotFoundException("临时加载器配置不存在。", profile);
        var libraryRoot = Path.Combine(Repository.GameDirectory, "libraries");
        var pending = new List<(string Source, string Destination)>();
        if (Directory.Exists(libraryRoot))
        {
            foreach (var source in Directory.EnumerateFiles(libraryRoot, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(libraryRoot, source);
                var destination = FileUtil.SafeChildPath(Path.Combine(_target.GameDirectory, "libraries"), relative);
                if (File.Exists(destination))
                {
                    if (!string.Equals(await FileUtil.Sha1Async(source, ct).ConfigureAwait(false),
                            await FileUtil.Sha1Async(destination, ct).ConfigureAwait(false), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("共享运行库已存在且内容不同，未覆盖：" + relative);
                }
                else pending.Add((source, destination));
            }
        }
        foreach (var file in pending)
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(file.Destination)!);
            File.Copy(file.Source, file.Destination);
        }
        await MergedInstanceBuilder.BuildAsync(Repository.GameDirectory, loaderId, _target.GameDirectory, instanceId, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Repository.GameDirectory)) Directory.Delete(Repository.GameDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warn("无法清理临时加载器目录：" + exception.Message);
        }
    }
}
