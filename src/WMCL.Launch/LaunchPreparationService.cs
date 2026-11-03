using WMCL.Core.Jobs;
using WMCL.Launch.Java;
using WMCL.Minecraft;
using WMCL.Minecraft.Models;

namespace WMCL.Launch;

public sealed record LaunchPreferences
{
    public string IsolationMode { get; init; } = "auto";
    public string? JavaPath { get; init; }
    public int MemoryMb { get; init; } = 4096;
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string ExtraJvmArgs { get; init; } = "";
    public string ExtraGameArgs { get; init; } = "";
    public string ProcessPriority { get; init; } = "normal";
    public bool HighPerformanceGpu { get; init; }
    public string GpuIndex { get; init; } = "";
    public bool IncludeInstanceJvmTuning { get; init; } = true;
}

public sealed record LaunchOverrides
{
    public string? JavaPath { get; init; }
    public int? MemoryMb { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Server { get; init; }
    public int? Port { get; init; }
}

public sealed record PreparedLaunch(
    string GameRoot,
    string VersionId,
    string GameDirectory,
    VersionJson Version,
    InstanceSettings InstanceSettings,
    InstallManifest? Manifest,
    JavaRuntime Java,
    string? JavaWarning)
{
    public LaunchRequest CreateRequest(AccountInfo account, LaunchPreferences preferences,
        LaunchOverrides? overrides = null, IReadOnlyList<string>? jvmPrefix = null)
    {
        overrides ??= new LaunchOverrides();
        var gameArguments = CommandLineSplitter.Split(preferences.ExtraGameArgs)
            .Concat(CommandLineSplitter.Split(InstanceSettings.ExtraGameArgs)).ToList();
        var server = string.IsNullOrWhiteSpace(overrides.Server) ? InstanceSettings.Server : overrides.Server;
        if (!string.IsNullOrWhiteSpace(server))
        {
            gameArguments.Add("--server");
            gameArguments.Add(server.Trim());
            gameArguments.Add("--port");
            gameArguments.Add((overrides.Port ?? InstanceSettings.Port ?? 25565).ToString());
        }
        return new LaunchRequest
        {
            Version = Version,
            VersionId = VersionId,
            GameDirectory = GameDirectory,
            MinecraftDirectory = GameRoot,
            Java = Java,
            Account = account,
            MemoryMb = Math.Max(overrides.MemoryMb ?? InstanceSettings.MemoryMb ?? preferences.MemoryMb,
                Manifest?.McbbsMinMemoryMb ?? 0),
            Width = overrides.Width ?? InstanceSettings.Width ?? preferences.Width,
            Height = overrides.Height ?? InstanceSettings.Height ?? preferences.Height,
            ExtraJvmArguments = (jvmPrefix ?? []).Concat(CommandLineSplitter.Split(preferences.ExtraJvmArgs))
                .Concat(CommandLineSplitter.Split(InstanceSettings.ExtraJvmArgs)).ToList(),
            ExtraGameArguments = gameArguments,
            ProcessPriority = preferences.ProcessPriority,
            HighPerformanceGpu = preferences.HighPerformanceGpu,
            GpuIndex = preferences.GpuIndex,
            InitialHeapMb = preferences.IncludeInstanceJvmTuning ? InstanceSettings.InitialHeapMb : 0,
            GcStrategy = preferences.IncludeInstanceJvmTuning ? InstanceSettings.GcStrategy : ""
        };
    }
}

public sealed class LaunchPreparationService
{
    private readonly Func<string, CancellationToken, Task<JavaRuntime?>> _probeJava;
    private readonly Func<JavaRequirement, IProgress<JobProgress>?, CancellationToken, Task<JavaRuntime>> _ensureJava;

    public LaunchPreparationService(JavaLocator locator, JavaProvisioner provisioner)
        : this(locator.ProbePathAsync, (requirement, progress, ct) => provisioner.EnsureAsync(requirement, progress, ct))
    {
    }

    internal LaunchPreparationService(Func<string, CancellationToken, Task<JavaRuntime?>> probeJava,
        Func<JavaRequirement, IProgress<JobProgress>?, CancellationToken, Task<JavaRuntime>> ensureJava)
    {
        _probeJava = probeJava;
        _ensureJava = ensureJava;
    }

    public async Task<PreparedLaunch> PrepareAsync(string gameRoot, string versionId,
        LaunchPreferences preferences, LaunchOverrides? overrides = null,
        IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var version = await new VersionRepository(gameRoot).GetVersionAsync(versionId, ct).ConfigureAwait(false);
        var manifest = InstallManifestStore.Load(gameRoot, versionId);
        var instanceSettings = InstanceSettingsStore.Load(Path.Combine(gameRoot, "versions", versionId));
        var requirement = JavaRequirement.ForVersion(version, manifest);
        var javaPath = !string.IsNullOrWhiteSpace(overrides?.JavaPath) ? overrides.JavaPath
            : !string.IsNullOrWhiteSpace(instanceSettings.JavaPath) ? instanceSettings.JavaPath : preferences.JavaPath;
        var java = !string.IsNullOrWhiteSpace(javaPath)
            ? await _probeJava(javaPath, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"无法识别指定的 Java：{javaPath}，请重新选择 Java 后再启动。")
            : await _ensureJava(requirement, progress, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (manifest?.McbbsSupportJava is { Count: > 0 } supportedJava && !supportedJava.Contains(java.MajorVersion))
            throw new InvalidOperationException(
                $"该整合包不支持 Java {java.MajorVersion}，仅支持 Java {string.Join("、", supportedJava.OrderBy(major => major))}。");
        var directory = IsolationPolicy.GetGameDirectory(gameRoot, versionId, version, preferences.IsolationMode);
        Directory.CreateDirectory(directory);
        return new PreparedLaunch(gameRoot, versionId, directory, version, instanceSettings, manifest, java,
            requirement.WarningFor(java));
    }
}
