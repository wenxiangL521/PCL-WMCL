using System.Collections.Immutable;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Core.Logging;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Resources;

namespace WMCL.App.Services;

public sealed record ResourceBatchTarget(string GameRoot, string InstanceId, string GameDirectory,
    string Minecraft, string Loader, string VersionHash, string? Save)
{
    public string Detail => $"{InstanceId} · Minecraft {Minecraft} · {Loader}";
}

public sealed record ResourceBatchSkipped(string Title, string Reason);
public sealed record ResourceBatchArtifact(ResourceInstallEntry Entry, string Directory, string? WorldName = null)
{
    public string Destination => WorldName is null ? Path.Combine(Directory, Entry.FileName) : Path.Combine(Directory, WorldName);
    public string Title => (Entry.IsDependency ? "前置：" : "") + Entry.Title;
    public string Detail => Entry.Detail + " → " + Destination + (WorldName is null ? "" : "（导入新存档，重名时编号）");
}

public sealed record ResourceBatchPlan(ResourceBatchTarget Target, ImmutableArray<ResourceBatchArtifact> Artifacts,
    ImmutableArray<ResourceBatchSkipped> Skipped);
public sealed record ResourceBatchOutcome(ResourceBatchArtifact Artifact, string State, string Detail);
public sealed record ResourceBatchResult(ImmutableArray<ResourceBatchOutcome> Outcomes)
{
    public string Summary => $"已安装 {Outcomes.Count(item => item.State == "installed")} 项 · " +
        $"已存在 {Outcomes.Count(item => item.State == "reused")} 项 · " +
        $"失败 {Outcomes.Count(item => item.State == "failed")} 项 · 已取消 {Outcomes.Count(item => item.State == "canceled")} 项";
}

public sealed class ResourceBatchInstallService(AppServices services, ResourcePlanInstaller installer)
{
    public async Task<ResourceBatchTarget> GetTargetAsync(string instanceId, string? save = null, CancellationToken ct = default)
    {
        var root = services.Repository.GameDirectory;
        if (FileUtil.SafeFileName(instanceId) != instanceId) throw new InvalidOperationException("无效的目标实例。");
        var path = FileUtil.SafeChildPath(Path.Combine(root, "versions"), instanceId + "/" + instanceId + ".json");
        if (!File.Exists(path)) throw new InvalidOperationException("目标实例已不存在，请重新选择。");
        var versionHash = await FileUtil.Sha1Async(path, ct).ConfigureAwait(false);
        var version = await services.Repository.GetVersionAsync(instanceId, ct).ConfigureAwait(false);
        if (root != services.Repository.GameDirectory) throw new InvalidOperationException("游戏目录已变化，请重新选择目标实例。");
        if (versionHash != await FileUtil.Sha1Async(path, ct).ConfigureAwait(false))
            throw new InvalidOperationException("目标实例正在变化，请重新选择。");
        var directory = IsolationPolicy.GetGameDirectory(root, instanceId, version, services.Settings.IsolationMode);
        if (save is not null)
        {
            if (FileUtil.SafeFileName(save) != save || !Directory.Exists(FileUtil.SafeChildPath(Path.Combine(directory, "saves"), save)))
                throw new InvalidOperationException("数据包目标存档已不存在，请重新选择。");
        }
        return new(root, instanceId, directory, MinecraftVersionUtil.GetBaseGameVersion(instanceId, version),
            MinecraftVersionUtil.GetLoader(version), versionHash, save);
    }

    public async Task<ResourceBatchPlan> BuildAsync(IReadOnlyList<FavoriteResource> resources, string instanceId,
        string? save, CancellationToken ct = default)
    {
        if (resources.Count == 0) throw new InvalidOperationException("请先选择要安装的收藏。");
        if (resources.Any(resource => resource.ResourceType == "datapack") && save is null)
            throw new InvalidOperationException("请选择数据包要安装到的存档。");
        var target = await GetTargetAsync(instanceId, save, ct).ConfigureAwait(false);
        var skipped = new List<ResourceBatchSkipped>();
        var requests = new List<ResourceInstallRequest>();
        foreach (var resource in resources.DistinctBy(resource => resource.Key))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!resource.IsValid()) throw new InvalidOperationException("收藏项目身份无效。");
                if (resource.ResourceType == "modpack") throw new InvalidOperationException("整合包需要创建独立实例，请打开项目单独安装。");
                if (resource.ResourceType == "mod" && target.Loader is not ("fabric" or "quilt" or "forge" or "neoforge"))
                    throw new InvalidOperationException("此实例没有可安装 Mod 的加载器。");
                var request = new ResourceInstallRequest(resource.Platform, resource.ProjectId, resource.Title, resource.ResourceType);
                if (resource.Platform == "Modrinth")
                {
                    var version = await services.Modrinth.FindCompatibleVersionAsync(resource.ProjectId, target.Minecraft,
                        target.Loader, ct, resource.ResourceType).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("没有适用于此实例的版本。");
                    request = request with { VersionId = version.Id, Modrinth = version };
                }
                else
                {
                    var id = long.Parse(resource.ProjectId, System.Globalization.CultureInfo.InvariantCulture);
                    var file = await services.CurseForge.FindFileAsync(id, target.Minecraft,
                        resource.ResourceType == "mod" ? target.Loader : null, ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("没有适用于此实例的文件。");
                    request = request with { VersionId = file.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), CurseForge = file };
                }
                await services.ResourcePlanner.ResolveAsync([request], target.Minecraft, target.Loader, ct).ConfigureAwait(false);
                requests.Add(request);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                skipped.Add(new(resource.Title, exception.Message));
            }
        }
        var plan = await services.ResourcePlanner.ResolveAsync(requests, target.Minecraft, target.Loader, ct).ConfigureAwait(false);
        var artifacts = plan.Entries.Select(entry => new ResourceBatchArtifact(entry, entry.ResourceType switch
        {
            "mod" => Path.Combine(target.GameDirectory, "mods"),
            "resourcepack" => Path.Combine(target.GameDirectory, "resourcepacks"),
            "shader" => Path.Combine(target.GameDirectory, "shaderpacks"),
            "plugin" => Path.Combine(target.GameDirectory, "plugins"),
            "datapack" => Path.Combine(target.GameDirectory, "saves", target.Save!, "datapacks"),
            "world" => Path.Combine(target.GameDirectory, "saves"),
            _ => throw new InvalidOperationException("未知的资源类型。")
        }, entry.ResourceType == "world" ? FileUtil.SafeFileName(WorldInstaller.Sanitize(
            resources.First(resource => resource.Key == entry.Key).Title).Replace('\\', '_')) : null)).ToImmutableArray();
        return new(target, artifacts, skipped.ToImmutableArray());
    }

    public async Task<ResourceBatchResult> InstallAsync(ResourceBatchPlan plan, IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        using var operation = services.GameOperations.Acquire(plan.Target.GameRoot, "批量安装收藏资源");
        await ValidateTargetAsync(plan.Target, ct).ConfigureAwait(false);
        var outcomes = new List<ResourceBatchOutcome>();
        var files = plan.Artifacts.Where(item => item.WorldName is null).ToArray();
        if (files.Length > 0)
        {
            try
            {
                var result = await installer.InstallAsync(files.Select(item => new ResourceInstallTarget(item.Entry, item.Directory)).ToArray(),
                    progress, ct, token => ValidateTargetAsync(plan.Target, token)).ConfigureAwait(false);
                foreach (var item in files)
                    outcomes.Add(new(item, result.Files.Contains(item.Destination) ? "installed" : "reused", item.Destination));
            }
            catch (Exception exception)
            {
                foreach (var item in files)
                    outcomes.Add(new(item, exception is OperationCanceledException ? "canceled" : "failed", exception.Message));
            }
        }
        foreach (var world in plan.Artifacts.Where(item => item.WorldName is not null))
        {
            var staging = Path.Combine(Path.GetTempPath(), "wmcl-batch-world-" + Guid.NewGuid().ToString("N"));
            try
            {
                await ValidateTargetAsync(plan.Target, ct).ConfigureAwait(false);
                var result = await installer.InstallAsync([new(world.Entry, staging)], progress, ct).ConfigureAwait(false);
                await ValidateTargetAsync(plan.Target, ct).ConfigureAwait(false);
                var destination = await Task.Run(() => WorldInstaller.Install(result.ResolvedFiles.Single(), world.Directory, world.WorldName!, ct), ct)
                    .ConfigureAwait(false);
                outcomes.Add(new(world, "installed", destination));
            }
            catch (Exception exception)
            {
                outcomes.Add(new(world, exception is OperationCanceledException ? "canceled" : "failed", exception.Message));
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Log.Warn("回收批量地图暂存目录失败：" + exception.Message);
                }
            }
        }
        return new(outcomes.ToImmutableArray());
    }

    private async Task ValidateTargetAsync(ResourceBatchTarget target, CancellationToken ct)
    {
        var current = await GetTargetAsync(target.InstanceId, target.Save, ct).ConfigureAwait(false);
        if (current != target) throw new InvalidOperationException("目标实例、版本或隔离设置已变化，请重新检查安装计划。");
    }
}
