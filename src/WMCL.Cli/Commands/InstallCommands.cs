using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Launch.Java;
using WMCL.Minecraft;
using WMCL.Modding.Fabric;
using WMCL.Modding.Forgelike;
using WMCL.Modding.OptiFine;

namespace WMCL.Cli.Commands;

/// <summary>安装与组件相关命令（原版 / Java / 加载器 / OptiFine）。</summary>
public static class InstallCommands
{
public static async Task<int> InstallAsync(
    IReadOnlyList<string> positional,
    IReadOnlyDictionary<string, string> options,
    CancellationToken ct)
{
    if (positional.Count == 0)
    {
        Console.Error.WriteLine("缺少版本号，例如：wmcl install 1.21.1");
        return 1;
    }

    var versionId = positional[0];
    var gameDirectory = CliHelpers.GetGameDirectory(options);
    var mirror = CliHelpers.ParseMirror(options.GetValueOrDefault("mirror"));
    var threads = CliHelpers.GetInt(options, "threads", 16);

    Console.WriteLine($"安装 {versionId}");
    Console.WriteLine($"游戏目录：{gameDirectory}");
    Console.WriteLine($"下载源：{mirror}，并发：{threads}");
    Console.WriteLine();

    var repository = new VersionRepository(gameDirectory);
    var installer = new VersionInstaller(repository);
    var progress = new SyncProgress<JobProgress>(p => CliHelpers.PrintProgress(p));

    var result = await installer.InstallAsync(
        versionId,
        new InstallOptions
        {
            Mirror = mirror,
            MaxConcurrency = threads,
            VerifyExisting = options.ContainsKey("verify")
        },
        progress,
        ct);

    Console.WriteLine();
    Console.WriteLine(
        $"完成：{result.VersionId}，{result.Files} 个文件，{CliHelpers.FormatBytes(result.Bytes)}，耗时 {result.Elapsed:mm\\:ss}。");
    Console.WriteLine($"版本目录：{result.VersionDirectory}");
    return 0;
}


public static async Task<int> JavaAsync(
    IReadOnlyList<string> positional,
    IReadOnlyDictionary<string, string> options,
    CancellationToken ct)
{
    if (positional.Count >= 2 &&
        positional[0].Equals("install", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(positional[1], out var major))
    {
        var provisioner = new JavaProvisioner(new JavaLocator());
        var result = await provisioner.ProvisionAsync(
            major,
            options.ContainsKey("force"),
            new SyncProgress<JobProgress>(CliHelpers.PrintProgress),
            ct);
        Console.WriteLine();
        Console.WriteLine($"Java {result.MajorVersion}: {result.Path}");
        return 0;
    }

    if (positional.Count == 0 || !positional[0].Equals("list", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("用法：wmcl java list | wmcl java install <主版本> [--force]");
        return 1;
    }

    var locator = new JavaLocator();
    if (options.TryGetValue("java", out var javaPath) && !string.IsNullOrWhiteSpace(javaPath))
    {
        var single = await locator.ProbePathAsync(javaPath, ct);
        if (single is null)
        {
            Console.Error.WriteLine($"无法识别 Java：{javaPath}");
            return 1;
        }
        Console.WriteLine(single.Display);
        return 0;
    }

    var runtimes = await locator.DiscoverAsync(forceRefresh: true, ct: ct);
    if (runtimes.Count == 0)
    {
        Console.WriteLine("未发现 Java。");
        return 0;
    }

    foreach (var runtime in runtimes)
        Console.WriteLine($"{runtime.MajorVersion,3}  {runtime.Architecture,-8}  {runtime.Path}");
    return 0;
}


public static async Task<int> FabricAsync(
    IReadOnlyList<string> positional,
    IReadOnlyDictionary<string, string> options,
    CancellationToken ct)
{
    if (positional.Count == 0)
    {
        Console.Error.WriteLine("用法：wmcl fabric loaders <MC版本> | wmcl fabric install <MC版本> [--loader <版本>]");
        return 1;
    }

    var subcommand = positional[0].ToLowerInvariant();
    var gameVersion = positional.Count > 1 ? positional[1] : options.GetValueOrDefault("game");
    if (string.IsNullOrWhiteSpace(gameVersion))
    {
        Console.Error.WriteLine("缺少 Minecraft 版本，例如：wmcl fabric install 1.21.1");
        return 1;
    }

    var repository = new VersionRepository(CliHelpers.GetGameDirectory(options));
    var installer = new FabricInstaller(repository);

    if (subcommand == "loaders")
    {
        var loaders = await installer.GetLoadersAsync(gameVersion, ct);
        if (loaders.Count == 0)
        {
            Console.WriteLine($"Fabric 没有支持 Minecraft {gameVersion} 的版本。");
            return 0;
        }

        foreach (var loader in loaders.Take(30))
            Console.WriteLine($"  {loader.Version,-16} {(loader.Stable ? "stable" : "")}");
        return 0;
    }

    if (subcommand == "install")
    {
        var progress = new SyncProgress<JobProgress>(CliHelpers.PrintProgress);
        var id = await installer.InstallAsync(
            gameVersion,
            options.GetValueOrDefault("loader"),
            progress,
            ct);
        Console.WriteLine();
        Console.WriteLine($"Fabric 安装完成：{id}");
        return 0;
    }

    Console.Error.WriteLine($"未知子命令：{subcommand}");
    return 1;
}


public static async Task<int> ForgelikeAsync(
    IReadOnlyList<string> positional,
    IReadOnlyDictionary<string, string> options,
    CancellationToken ct)
{
    if (positional.Count < 2)
    {
        Console.Error.WriteLine(
            "用法：wmcl forgelike versions <MC版本> --loader forge|neoforge；wmcl forgelike install <MC版本> --loader ...");
        return 1;
    }

    var subcommand = positional[0].ToLowerInvariant();
    var minecraftVersion = positional[1];
    var kind = string.Equals(options.GetValueOrDefault("loader"), "neoforge",
        StringComparison.OrdinalIgnoreCase)
        ? ForgelikeKind.NeoForge
        : ForgelikeKind.Forge;

    var gameDirectory = CliHelpers.GetGameDirectory(options);
    var repository = new VersionRepository(gameDirectory);
    var installer = new ForgelikeInstaller(repository);

    if (subcommand == "versions")
    {
        var versions = await installer.GetVersionsAsync(kind, minecraftVersion, ct);
        if (versions.Count == 0)
        {
            Console.WriteLine($"没有找到 {minecraftVersion} 的 {kind} 版本。");
            return 0;
        }

        foreach (var version in versions.Take(30))
            Console.WriteLine($"  {version}");
        return 0;
    }

    if (subcommand == "install")
    {
        var requested = options.GetValueOrDefault("version");
        string loaderVersion;
        if (!string.IsNullOrWhiteSpace(requested))
        {
            loaderVersion = requested;
        }
        else
        {
            var versions = await installer.GetVersionsAsync(kind, minecraftVersion, ct);
            loaderVersion = versions.FirstOrDefault()
                            ?? throw new InvalidOperationException(
                                $"没有找到 {minecraftVersion} 的 {kind} 版本。");
        }

        var javaLocator = new JavaLocator();
        JavaRuntime java;
        var javaPath = options.GetValueOrDefault("java");
        if (!string.IsNullOrWhiteSpace(javaPath))
        {
            java = await javaLocator.ProbePathAsync(javaPath, ct)
                   ?? throw new InvalidOperationException($"无法识别 Java：{javaPath}");
        }
        else
        {
            var provisioner = new JavaProvisioner(javaLocator);
            java = await provisioner.EnsureAsync(
                JavaRequirement.ForInstall(minecraftVersion, kind.ToString(), loaderVersion),
                new SyncProgress<JobProgress>(CliHelpers.PrintProgress),
                ct);
        }

        Console.WriteLine();
        Console.WriteLine($"使用 Java {java.MajorVersion}：{java.Path}");
        var progress = new SyncProgress<JobProgress>(CliHelpers.PrintProgress);
        var installerLog = new SyncProgress<string>(line =>
        {
            if (line.Contains("Downloading", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Patching", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Exception", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine("  " + line);
        });
        var id = await installer.InstallAsync(
            kind, minecraftVersion, loaderVersion, java.Path, progress, installerLog, ct);
        Console.WriteLine();
        Console.WriteLine($"{kind} 安装完成：{id}");
        return 0;
    }

    Console.Error.WriteLine($"未知子命令：{subcommand}");
    return 1;
}


public static async Task<int> OptiFineAsync(
    IReadOnlyList<string> positional,
    IReadOnlyDictionary<string, string> options,
    CancellationToken ct)
{
    if (positional.Count < 2)
    {
        Console.Error.WriteLine(
            "用法：wmcl optifine versions <MC版本>；wmcl optifine install <MC版本> [--patch I6] [--java <路径>]");
        return 1;
    }

    var subcommand = positional[0].ToLowerInvariant();
    var minecraft = positional[1];
    var gameDirectory = CliHelpers.GetGameDirectory(options);
    var repository = new VersionRepository(gameDirectory);
    var service = new OptiFineService(repository);

    if (subcommand == "versions")
    {
        var versions = await service.GetVersionsAsync(minecraft, ct);
        if (versions.Count == 0)
        {
            Console.WriteLine($"没有找到 {minecraft} 的 OptiFine。");
            return 0;
        }

        foreach (var version in versions.Take(30))
            Console.WriteLine($"  {version.Patch,-10} {(version.IsPreview ? "preview" : "release"),-8} {version.FileName}");
        return 0;
    }

    if (subcommand == "install")
    {
        var javaLocator = new JavaLocator();
        JavaRuntime java;
        var javaPath = options.GetValueOrDefault("java");
        if (!string.IsNullOrWhiteSpace(javaPath))
        {
            java = await javaLocator.ProbePathAsync(javaPath, ct)
                   ?? throw new InvalidOperationException($"无法识别 Java：{javaPath}");
        }
        else
        {
            var provisioner = new JavaProvisioner(javaLocator);
            java = await provisioner.EnsureAsync(
                JavaRequirement.ForInstall(minecraft, "OptiFine", "unknown"),
                new SyncProgress<JobProgress>(CliHelpers.PrintProgress), ct);
        }

        var progress = new SyncProgress<JobProgress>(CliHelpers.PrintProgress);
        var id = await service.InstallAsync(
            minecraft, options.GetValueOrDefault("patch"), java.Path,
            progress, null, ct);
        Console.WriteLine();
        Console.WriteLine($"OptiFine 安装完成：{id}");
        return 0;
    }

    Console.Error.WriteLine($"未知子命令：{subcommand}");
    return 1;
}
}
