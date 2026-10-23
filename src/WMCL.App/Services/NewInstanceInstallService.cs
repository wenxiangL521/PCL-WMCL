using WMCL.Core;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Core.Logging;
using WMCL.Minecraft;

namespace WMCL.App.Services;

internal sealed class NewInstanceInstallService(
    string gameRoot, string isolationMode, Func<string, ICombinedInstallBackend> backendFactory)
{
    private sealed record SharedFile(string Source, string Destination, string Hash);

    public async Task<CombinedInstallResult> InstallAsync(CombinedInstallPlan plan,
        Action<CombinedInstallStep, CombinedInstallStepState, JobProgress> report, CancellationToken ct = default)
    {
        InstanceManager.ValidateName(plan.Minecraft);
        InstanceManager.ValidateName(plan.InstanceId);
        var root = Path.GetFullPath(WmclPaths.Expand(gameRoot));
        var target = FileUtil.SafeChildPath(root, "versions/" + plan.InstanceId);
        EnsureAvailable();
        ct.ThrowIfCancellationRequested();
        var temporary = Path.Combine(root, ".wmcl-instance-install-" + Guid.NewGuid().ToString("N"));
        var stageRoot = Path.Combine(temporary, "game");
        var createdDirectories = new Stack<string>();
        var publishedFiles = new Stack<SharedFile>();
        var rollbackComplete = true;
        var applying = false;
        CombinedInstallResult result;
        try
        {
            Directory.CreateDirectory(stageRoot);
            var stagedPlan = plan with { VanillaInstalled = false };
            result = await new CombinedInstallService(stageRoot, isolationMode, backendFactory(stageRoot))
                .InstallAsync(stagedPlan, report, ct);
            applying = true;
            ReportApply("校验安装结果", 0);
            var stagedInstance = Path.Combine(stageRoot, "versions", plan.InstanceId);
            EnsureRegularPath(stagedInstance);
            var instanceFiles = new List<SharedFile>();
            foreach (var source in EnumerateRegularFiles(stagedInstance))
                instanceFiles.Add(new(source, source, await FileUtil.Sha1Async(source, ct)));
            var verification = await InstallManifestStore.VerifyAsync(stageRoot, plan.InstanceId, ct);
            if (!verification.IsHealthy || verification.Checked == 0 || verification.Problems.Count > 0)
                throw new InvalidDataException("暂存实例校验失败：" + string.Join("；", verification.Problems));
            var version = await new VersionRepository(stageRoot).GetVersionAsync(plan.InstanceId, ct);
            var expectedLoader = plan.BaseLoader switch
            {
                "无" or "OptiFine" when plan.LiteLoader is not null => "liteloader",
                "无" => "vanilla",
                "LegacyFabric" => "fabric",
                _ => plan.BaseLoader.ToLowerInvariant()
            };
            if (version.BaseGameVersion != plan.Minecraft || MinecraftVersionUtil.GetLoader(version) != expectedLoader)
                throw new InvalidDataException("暂存实例的 Minecraft 或加载器与固定请求不一致");
            if (MinecraftVersionUtil.IsModable(version))
                Directory.CreateDirectory(Path.Combine(IsolationPolicy.GetGameDirectory(stageRoot, plan.InstanceId, version, isolationMode), "mods"));
            var manifest = InstallManifestStore.Load(stageRoot, plan.InstanceId)!;
            var modsDirectory = Path.Combine(IsolationPolicy.GetGameDirectory(stageRoot, plan.InstanceId, version, isolationMode), "mods");
            foreach (var source in EnumerateRegularFiles(modsDirectory))
            {
                var relative = Path.GetRelativePath(stageRoot, source).Replace('\\', '/');
                if (!manifest.ManagedFiles.Any(file => file.RelativePath == relative))
                    throw new InvalidDataException("暂存 Mod 文件未记录于安装清单：" + relative);
            }
            var files = new List<SharedFile>();
            foreach (var folder in new[] { "libraries", "assets", "resources", "mods" })
            {
                foreach (var source in EnumerateRegularFiles(Path.Combine(stageRoot, folder)))
                {
                    ct.ThrowIfCancellationRequested();
                    EnsureRegularPath(source);
                    var relative = Path.GetRelativePath(stageRoot, source).Replace('\\', '/');
                    var destination = FileUtil.SafeChildPath(root, relative);
                    EnsureRegularPath(destination);
                    if (Directory.Exists(destination)) throw new IOException("共享依赖目标是目录：" + relative);
                    var hash = await FileUtil.Sha1Async(source, ct);
                    if (File.Exists(destination) && await FileUtil.Sha1Async(destination, ct) != hash)
                        throw new IOException("共享依赖已存在且内容不同，未覆盖：" + relative);
                    files.Add(new(source, destination, hash));
                }
            }
            EnsureAvailable();
            ReportApply("应用安装结果", 0);
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                EnsureRegularPath(file.Source);
                EnsureRegularPath(file.Destination);
                if (await FileUtil.Sha1Async(file.Source, ct) != file.Hash)
                    throw new IOException("安装期间暂存文件发生变化：" + file.Source);
                if (File.Exists(file.Destination))
                {
                    if (await FileUtil.Sha1Async(file.Destination, ct) != file.Hash)
                        throw new IOException("安装期间共享依赖发生变化：" + file.Destination);
                    continue;
                }
                CreateDirectories(Path.GetDirectoryName(file.Destination)!);
                File.Move(file.Source, file.Destination);
                publishedFiles.Push(file);
                ReportApply("应用共享依赖", 0.95 * publishedFiles.Count / Math.Max(1, files.Count));
            }
            ReportApply("发布实例", 0.99);
            foreach (var file in instanceFiles.Concat(files))
            {
                ct.ThrowIfCancellationRequested();
                EnsureRegularPath(file.Destination);
                if (await FileUtil.Sha1Async(file.Destination, ct) != file.Hash)
                    throw new IOException("发布前安装文件发生变化：" + file.Destination);
            }
            EnsureAvailable();
            ct.ThrowIfCancellationRequested();
            CreateDirectories(Path.GetDirectoryName(target)!);
            Directory.Move(stagedInstance, target);
        }
        catch (Exception failure)
        {
            try
            {
                while (publishedFiles.TryPop(out var file))
                {
                    EnsureRegularPath(file.Destination);
                    if (!File.Exists(file.Destination)) continue;
                    if (await FileUtil.Sha1Async(file.Destination, CancellationToken.None) != file.Hash)
                        throw new IOException("回滚期间共享依赖已被外部修改：" + file.Destination);
                    File.Delete(file.Destination);
                }
                while (createdDirectories.TryPop(out var directory))
                {
                    EnsureRegularPath(directory);
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                }
            }
            catch (Exception rollback)
            {
                rollbackComplete = false;
                throw new AggregateException("实例安装失败且回滚未完成，暂存资料保留于：" + temporary, failure, rollback);
            }
            if (applying)
                report(CombinedInstallStep.Apply, failure is OperationCanceledException ? CombinedInstallStepState.Cancelled : CombinedInstallStepState.Failed,
                    new JobProgress("应用安装结果", Detail: failure.Message));
            throw;
        }
        finally
        {
            if (rollbackComplete && Directory.Exists(temporary))
            {
                try { Directory.Delete(temporary, true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Log.Warn("无法清理实例暂存目录：" + exception.Message);
                }
            }
        }
        report(CombinedInstallStep.Apply, CombinedInstallStepState.Completed, new JobProgress("应用安装结果", 1));
        return result;

        void ReportApply(string stage, double percent) =>
            report(CombinedInstallStep.Apply, CombinedInstallStepState.Running, new JobProgress(stage, percent, Detail: plan.InstanceId));

        void EnsureAvailable()
        {
            EnsureRegularPath(target);
            if (Path.Exists(target)) throw new IOException("实例已存在：" + plan.InstanceId + "。请在实例页修改组件或选择其他名称。");
        }

        void EnsureRegularPath(string path)
        {
            for (var current = path; current.Length > root.Length; current = Path.GetDirectoryName(current)!)
                if (new FileInfo(current).LinkTarget is not null) throw new InvalidDataException("安装路径包含符号链接：" + path);
        }

        void CreateDirectories(string directory)
        {
            if (Directory.Exists(directory)) return;
            CreateDirectories(Path.GetDirectoryName(directory)!);
            Directory.CreateDirectory(directory);
            createdDirectories.Push(directory);
        }
    }

    private static IEnumerable<string> EnumerateRegularFiles(string directory)
    {
        if (new FileInfo(directory).LinkTarget is not null) throw new InvalidDataException("暂存内容包含符号链接：" + directory);
        if (!Directory.Exists(directory)) yield break;
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            if (new FileInfo(path).LinkTarget is not null) throw new InvalidDataException("暂存内容包含符号链接：" + path);
            if (Directory.Exists(path))
            {
                foreach (var file in EnumerateRegularFiles(path)) yield return file;
            }
            else yield return path;
        }
    }
}
