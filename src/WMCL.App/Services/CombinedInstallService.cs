using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.LiteLoader;
using WMCL.Modding.Fabric;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Services;

internal enum CombinedInstallStepState { Running, Completed, Failed, Cancelled }

internal sealed record CombinedInstallResult(string InstanceId, IReadOnlyList<string> Errors)
{
    public OptiFabricFile? InstalledOptiFabric { get; init; }
    public string Status => Errors.Count == 0 ? $"安装完成：{InstanceId}"
        : $"实例 {InstanceId} 已创建，但附加组件安装失败：{string.Join("；", Errors)}";
}

internal interface ICombinedInstallBackend
{
    Task InstallVanillaAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct);
    Task<string> InstallLoaderAsync(CombinedInstallPlan plan, string stagingRoot, IProgress<JobProgress> progress, CancellationToken ct);
    Task CompleteInstanceAsync(string instanceId, IProgress<JobProgress> progress, CancellationToken ct);
    Task<string> DownloadOptiFineAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct);
    Task<IReadOnlyList<string>> InstallModAsync(string project, CombinedInstallPlan plan, string modsDirectory, IProgress<JobProgress> progress, CancellationToken ct);
}

internal sealed class CombinedInstallService(string gameRoot, string isolationMode, ICombinedInstallBackend backend)
{
    public async Task<CombinedInstallResult> InstallAsync(
        CombinedInstallPlan plan,
        Action<CombinedInstallStep, CombinedInstallStepState, JobProgress> report,
        CancellationToken ct = default)
    {
        var errors = new List<string>();
        var manifest = new InstallManifest();
        manifest.Components["minecraft"] = plan.Minecraft;
        var stagingRoot = Path.Combine(Path.GetTempPath(), "wmcl-install-" + Guid.NewGuid().ToString("N"));
        string? loaderId = null;
        string? modsDirectory = null;
        OptiFabricFile? installedOptiFabric = null;
        try
        {
            foreach (var step in plan.Steps)
            {
                ct.ThrowIfCancellationRequested();
                report(step, CombinedInstallStepState.Running, new JobProgress(plan.Title(step), 0));
                var finished = false;
                var progress = new Progress<JobProgress>(update =>
                {
                    if (!finished) report(step, CombinedInstallStepState.Running, update);
                });
                try
                {
                    switch (step)
                    {
                        case CombinedInstallStep.Vanilla:
                            if (!plan.VanillaInstalled) await backend.InstallVanillaAsync(plan, progress, ct);
                            break;
                        case CombinedInstallStep.Loader:
                            StageVanilla(plan, stagingRoot, ct);
                            if (plan.BaseLoader is "Forge" or "NeoForge")
                                await CopyVanillaLibrariesAsync(plan, stagingRoot, ct);
                            loaderId = await backend.InstallLoaderAsync(plan, stagingRoot, progress, ct);
                            manifest.Components[plan.BaseLoader.ToLowerInvariant()] =
                                plan.BaseLoader == "OptiFine" ? plan.OptiFine!.Patch : plan.LoaderVersion!;
                            if (plan.BaseLoader == "OptiFine")
                                manifest.ComponentMetadata["optifine"] = JsonSerializer.SerializeToElement(plan.OptiFine);
                            break;
                        case CombinedInstallStep.LiteLoader:
                            if (loaderId is null) StageVanilla(plan, stagingRoot, ct);
                            loaderId = await LiteLoaderService.InstallProfileAsync(stagingRoot, plan.LiteLoader!, ct, loaderId);
                            manifest.Components["liteloader"] = plan.LiteLoader!.Version;
                            manifest.ComponentMetadata["liteloader"] = JsonSerializer.SerializeToElement(plan.LiteLoader);
                            break;
                        case CombinedInstallStep.Merge:
                            if (plan.HasLoader)
                            {
                                CopyDirectory(Path.Combine(stagingRoot, "libraries"), Path.Combine(gameRoot, "libraries"), ct);
                                await MergedInstanceBuilder.BuildAsync(stagingRoot, loaderId!, gameRoot, plan.InstanceId, ct);
                            }
                            else
                                await MergedInstanceBuilder.BuildAsync(gameRoot, plan.Minecraft, plan.InstanceId, ct);
                            if (plan.LiteLoader is not null) await PreserveVanillaDatesAsync(plan, ct);
                            await backend.CompleteInstanceAsync(plan.InstanceId, progress, ct);
                            break;
                        case CombinedInstallStep.OptiFineMod:
                            modsDirectory ??= await GetModsDirectoryAsync(plan, ct);
                            var installer = await backend.DownloadOptiFineAsync(plan, progress, ct);
                            var optifineDirectory = plan.LiteLoader is null ? modsDirectory : FileUtil.SafeChildPath(modsDirectory, plan.Minecraft);
                            var destination = FileUtil.SafeChildPath(optifineDirectory, Path.GetFileName(installer));
                            Directory.CreateDirectory(optifineDirectory);
                            if (File.Exists(destination))
                            {
                                var existingHash = await InstallManifestStore.ComputeFileSha1Async(destination, ct);
                                var installerHash = await InstallManifestStore.ComputeFileSha1Async(installer, ct);
                                if (existingHash != installerHash)
                                    throw new IOException($"Mod 文件已存在且内容不同：{Path.GetFileName(destination)}");
                            }
                            else File.Copy(installer, destination);
                            await RecordFilesAsync(manifest, [destination], "optifine", ct);
                            manifest.Components["optifine"] = plan.OptiFine!.Patch;
                            manifest.ComponentMetadata["optifine"] = JsonSerializer.SerializeToElement(plan.OptiFine);
                            break;
                        case CombinedInstallStep.Api:
                        case CombinedInstallStep.OptiFabric:
                            modsDirectory ??= await GetModsDirectoryAsync(plan, ct);
                            var project = step == CombinedInstallStep.Api ? plan.ApiProject : "optifabric";
                            var files = await backend.InstallModAsync(project, plan, modsDirectory, progress, ct);
                            await RecordFilesAsync(manifest, files.Select(file => FileUtil.SafeChildPath(modsDirectory, file)), project, ct,
                                path => step == CombinedInstallStep.OptiFabric ? plan.OptiFabric!.DownloadUrl
                                    : plan.ApiVersion!.Files.FirstOrDefault(file => file.Filename == Path.GetFileName(path))?.Url);
                            manifest.Components[project] = step == CombinedInstallStep.OptiFabric ? plan.OptiFabric!.Version : plan.ApiVersion!.VersionNumber;
                            if (step == CombinedInstallStep.OptiFabric)
                                installedOptiFabric = plan.OptiFabric! with
                                {
                                    Sha1 = manifest.ManagedFiles.First(file => file.Component == project && file.Url == plan.OptiFabric!.DownloadUrl).Sha1
                                };
                            manifest.ComponentMetadata[project] = step == CombinedInstallStep.OptiFabric
                                ? JsonSerializer.SerializeToElement(installedOptiFabric)
                                : JsonSerializer.SerializeToElement(ApiComponentService.Snapshot(plan.ApiVersion!));
                            break;
                        case CombinedInstallStep.Manifest:
                            manifest.ManagedFiles.AddRange(await InstallManifestStore.CollectManagedFilesAsync(gameRoot, plan.InstanceId, "merged", ct));
                            InstallManifestStore.Save(gameRoot, plan.InstanceId, manifest);
                            break;
                    }
                    finished = true;
                    report(step, CombinedInstallStepState.Completed, new JobProgress(plan.Title(step), 1,
                        Detail: step == CombinedInstallStep.Vanilla && plan.VanillaInstalled ? "已安装" : "已完成"));
                }
                catch (Exception exception)
                {
                    finished = true;
                    report(step, exception is OperationCanceledException ? CombinedInstallStepState.Cancelled : CombinedInstallStepState.Failed,
                        new JobProgress(plan.Title(step), Detail: exception.Message));
                    if (exception is OperationCanceledException ||
                        step is not (CombinedInstallStep.OptiFineMod or CombinedInstallStep.Api or CombinedInstallStep.OptiFabric))
                        throw;
                    errors.Add($"{plan.Title(step)}：{exception.Message}");
                }
            }
            return new CombinedInstallResult(plan.InstanceId, errors) { InstalledOptiFabric = installedOptiFabric };
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                try { Directory.Delete(stagingRoot, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private async Task PreserveVanillaDatesAsync(CombinedInstallPlan plan, CancellationToken ct)
    {
        var vanilla = await new VersionRepository(gameRoot).GetLocalVersionAsync(plan.Minecraft, ct);
        var path = Path.Combine(gameRoot, "versions", plan.InstanceId, plan.InstanceId + ".json");
        var profile = JsonNode.Parse(await File.ReadAllTextAsync(path, ct)) as JsonObject
            ?? throw new InvalidDataException("合并实例 profile 无效。");
        profile["time"] = vanilla.Time;
        profile["releaseTime"] = vanilla.ReleaseTime;
        await File.WriteAllTextAsync(path, profile.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
    }

    private void StageVanilla(CombinedInstallPlan plan, string stagingRoot, CancellationToken ct)
    {
        var directory = Path.Combine(stagingRoot, "versions", plan.Minecraft);
        Directory.CreateDirectory(directory);
        foreach (var extension in new[] { ".json", ".jar" })
        {
            ct.ThrowIfCancellationRequested();
            var filename = plan.Minecraft + extension;
            File.Copy(Path.Combine(gameRoot, "versions", plan.Minecraft, filename), Path.Combine(directory, filename));
        }
    }

    private async Task<string> GetModsDirectoryAsync(CombinedInstallPlan plan, CancellationToken ct)
    {
        var version = await new VersionRepository(gameRoot).GetVersionAsync(plan.InstanceId, ct);
        return Path.Combine(IsolationPolicy.GetGameDirectory(gameRoot, plan.InstanceId, version, isolationMode), "mods");
    }

    private async Task CopyVanillaLibrariesAsync(CombinedInstallPlan plan, string stagingRoot, CancellationToken ct)
    {
        var version = await new VersionRepository(gameRoot).GetVersionAsync(plan.Minecraft, ct);
        foreach (var library in LibraryResolver.Resolve(version, RuleContext.CreateDefault()))
        {
            ct.ThrowIfCancellationRequested();
            var source = FileUtil.SafeChildPath(Path.Combine(gameRoot, "libraries"), library.RelativePath);
            if (!File.Exists(source)) continue;
            var target = FileUtil.SafeChildPath(Path.Combine(stagingRoot, "libraries"), library.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
    }

    private async Task RecordFilesAsync(InstallManifest manifest, IEnumerable<string> files, string component, CancellationToken ct, Func<string, string?>? sourceUrl = null)
    {
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(gameRoot, file).Replace('\\', '/');
            FileUtil.SafeChildPath(gameRoot, relative);
            var sha1 = await InstallManifestStore.ComputeFileSha1Async(file, ct);
            if (!manifest.ManagedFiles.Any(existing => existing.RelativePath == relative))
                manifest.ManagedFiles.Add(new InstallManifestFile(relative, sha1, component, sourceUrl?.Invoke(file)));
        }
    }

    private static void CopyDirectory(string source, string destination, CancellationToken ct)
    {
        if (!Directory.Exists(source)) return;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var target = FileUtil.SafeChildPath(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.Copy(file, target);
        }
    }
}
