using System.Text;
using PCL.Core;
using PCL.Core.Jobs;
using PCL.Core.Logging;
using PCL.Download;
using PCL.Launch;
using PCL.Launch.Java;
using PCL.Minecraft;
using PCL.Modding.Fabric;
using PCL.Core.Update;
using PCL.Modding.Modrinth;

Console.OutputEncoding = Encoding.UTF8;
PclPaths.EnsureCreated();

var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
if (arguments.Length == 0)
{
    PrintUsage();
    return 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    var command = arguments[0].ToLowerInvariant();
    var (options, positional) = ParseOptions(arguments.Skip(1).ToArray());

    return command switch
    {
        "install" => await InstallAsync(positional, options, cts.Token),
        "launch" => await LaunchAsync(positional, options, cts.Token),
        "java" => await JavaAsync(positional, options, cts.Token),
        "fabric" => await FabricAsync(positional, options, cts.Token),
        "modrinth" => await ModrinthAsync(positional, options, cts.Token),
        "update" => await UpdateAsync(positional, options, cts.Token),
        "versions" => await VersionsAsync(options, cts.Token),
        "help" or "-h" or "--help" => PrintUsage(),
        _ => Unknown(command)
    };
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Console.WriteLine();
    Console.WriteLine("已取消。");
    return 130;
}
catch (Exception ex)
{
    Console.WriteLine();
    Log.Error("执行失败", ex);
    return 1;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"未知命令：{command}");
    PrintUsage();
    return 1;
}

static int PrintUsage()
{
    Console.WriteLine("""
        PCL Linux CLI 0.1

        用法：
          pcl versions [--dir <游戏目录>]
          pcl install <版本> [--dir <游戏目录>] [--mirror auto|official|bmclapi] [--threads 16] [--verify]
          pcl launch <版本> --offline <玩家名> [--dir <游戏目录>] [--memory 4096] [--java <路径>]
                     [--width 854] [--height 480]
          pcl java list [--java <路径>]
          pcl java install <主版本> [--force]
          pcl fabric loaders <MC版本>
          pcl fabric install <MC版本> [--loader <Fabric版本>] [--dir <游戏目录>]
          pcl modrinth search <关键词> --game <MC版本>
          pcl modrinth install <slug> --game <MC版本> [--dir <游戏目录>]
          pcl update check <latest.json 地址>
          pcl update download <latest.json 地址>
          pcl help

        示例：
          pcl install 1.21.1
          pcl launch 1.21.1 --offline Steve
        """);
    return 0;
}

static async Task<int> InstallAsync(
    IReadOnlyList<string> positional,
    IReadOnlyDictionary<string, string> options,
    CancellationToken ct)
{
    if (positional.Count == 0)
    {
        Console.Error.WriteLine("缺少版本号，例如：pcl install 1.21.1");
        return 1;
    }

    var versionId = positional[0];
    var gameDirectory = GetGameDirectory(options);
    var mirror = ParseMirror(options.GetValueOrDefault("mirror"));
    var threads = GetInt(options, "threads", 16);

    Console.WriteLine($"安装 {versionId}");
    Console.WriteLine($"游戏目录：{gameDirectory}");
    Console.WriteLine($"下载源：{mirror}，并发：{threads}");
    Console.WriteLine();

    var repository = new VersionRepository(gameDirectory);
    var installer = new VersionInstaller(repository);
    var progress = new SyncProgress<JobProgress>(p => PrintProgress(p));

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
        $"完成：{result.VersionId}，{result.Files} 个文件，{FormatBytes(result.Bytes)}，耗时 {result.Elapsed:mm\\:ss}。");
    Console.WriteLine($"版本目录：{result.VersionDirectory}");
    return 0;
}

static async Task<int> LaunchAsync(
    IReadOnlyList<string> positional,
    IReadOnlyDictionary<string, string> options,
    CancellationToken ct)
{
    if (positional.Count == 0)
    {
        Console.Error.WriteLine("缺少版本号，例如：pcl launch 1.21.1 --offline Steve");
        return 1;
    }

    var versionId = positional[0];
    var gameDirectory = GetGameDirectory(options);
    var repository = new VersionRepository(gameDirectory);
    var version = await repository.GetVersionAsync(versionId, ct);

    var requiredJava = version.JavaVersion?.MajorVersion
                       ?? JavaLocator.InferMajorVersion(versionId);
    var javaLocator = new JavaLocator();

    JavaRuntime java;
    if (options.TryGetValue("java", out var javaPath) && !string.IsNullOrWhiteSpace(javaPath))
    {
        java = await javaLocator.ProbePathAsync(javaPath, ct)
               ?? throw new InvalidOperationException($"无法识别指定的 Java：{javaPath}");
    }
    else
    {
        var provisioner = new JavaProvisioner(javaLocator);
        java = await provisioner.EnsureAsync(
            requiredJava, new SyncProgress<JobProgress>(PrintProgress), ct);
    }

    var playerName = options.GetValueOrDefault("offline", "Player");
    var memory = GetInt(options, "memory", 4096);
    int? width = options.ContainsKey("width") ? GetInt(options, "width", 854) : null;
    int? height = options.ContainsKey("height") ? GetInt(options, "height", 480) : null;

    var logPath = Path.Combine(PclPaths.LogsDirectory,
        $"game-{versionId}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    await using var writer = new StreamWriter(logPath, append: false, Encoding.UTF8) { AutoFlush = true };

    Console.WriteLine($"启动 {versionId}");
    Console.WriteLine($"游戏目录：{gameDirectory}");
    Console.WriteLine($"Java：{java.Display}");
    Console.WriteLine($"玩家：{playerName}（离线）");
    Console.WriteLine($"日志：{logPath}");
    Console.WriteLine();

    var request = new LaunchRequest
    {
        Version = version,
        VersionId = versionId,
        GameDirectory = gameDirectory,
        Java = java,
        Account = AccountInfo.Offline(playerName),
        MemoryMb = memory,
        Width = width,
        Height = height
    };

    var launcher = new GameLauncher();

    if (options.ContainsKey("dry-run"))
    {
        var plan = LaunchPlanBuilder.Build(request);
        Console.WriteLine(plan.Preview);
        return 0;
    }

    var exitCode = await launcher.LaunchAsync(request, new SyncProgress<string>(line =>
    {
        Console.WriteLine(line);
        writer.WriteLine(line);
    }), ct);

    Console.WriteLine();
    Console.WriteLine($"游戏已退出，退出码：{exitCode}");
    return exitCode;
}

static async Task<int> JavaAsync(
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
            new SyncProgress<JobProgress>(PrintProgress),
            ct);
        Console.WriteLine();
        Console.WriteLine($"Java {result.MajorVersion}: {result.Path}");
        return 0;
    }

    if (positional.Count == 0 || !positional[0].Equals("list", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("用法：pcl java list | pcl java install <主版本> [--force]");
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

static async Task<int> FabricAsync(
    IReadOnlyList<string> positional,
    IReadOnlyDictionary<string, string> options,
    CancellationToken ct)
{
    if (positional.Count == 0)
    {
        Console.Error.WriteLine("用法：pcl fabric loaders <MC版本> | pcl fabric install <MC版本> [--loader <版本>]");
        return 1;
    }

    var subcommand = positional[0].ToLowerInvariant();
    var gameVersion = positional.Count > 1 ? positional[1] : options.GetValueOrDefault("game");
    if (string.IsNullOrWhiteSpace(gameVersion))
    {
        Console.Error.WriteLine("缺少 Minecraft 版本，例如：pcl fabric install 1.21.1");
        return 1;
    }

    var repository = new VersionRepository(GetGameDirectory(options));
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
        var progress = new SyncProgress<JobProgress>(PrintProgress);
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

static async Task<int> ModrinthAsync(
    IReadOnlyList<string> positional,
    IReadOnlyDictionary<string, string> options,
    CancellationToken ct)
{
    if (positional.Count == 0)
    {
        Console.Error.WriteLine("用法：pcl modrinth search <关键词> --game <MC版本> | pcl modrinth install <slug> --game <MC版本>");
        return 1;
    }

    var subcommand = positional[0].ToLowerInvariant();
    var gameVersion = options.GetValueOrDefault("game");
    if (string.IsNullOrWhiteSpace(gameVersion))
    {
        Console.Error.WriteLine("缺少 --game，例如：--game 1.21.1");
        return 1;
    }

    var client = new ModrinthClient();

    if (subcommand == "search")
    {
        var query = positional.Count > 1 ? string.Join(' ', positional.Skip(1)) : "";
        var result = await client.SearchAsync(query, gameVersion, ct: ct);
        Console.WriteLine($"共 {result.TotalHits} 个结果：");
        foreach (var project in result.Projects.Take(20))
            Console.WriteLine($"  {project.Slug,-28} {project.Downloads,10}  {project.Title}");
        return 0;
    }

    if (subcommand == "install")
    {
        if (positional.Count < 2)
        {
            Console.Error.WriteLine("缺少项目 slug，例如：pcl modrinth install sodium");
            return 1;
        }

        var slug = positional[1];
        var gameDirectory = GetGameDirectory(options);
        var modsDirectory = Path.Combine(gameDirectory, "mods");
        var progress = new SyncProgress<JobProgress>(PrintProgress);
        var result = await client.InstallAsync(
            slug, gameVersion, modsDirectory, "fabric", progress, ct);
        Console.WriteLine();
        Console.WriteLine(result.Files.Count == 0
            ? "没有需要安装的文件（可能已存在）。"
            : $"安装 Mod：{string.Join("，", result.Files)}");
        return 0;
    }

    Console.Error.WriteLine($"未知子命令：{subcommand}");
    return 1;
}

static async Task<int> UpdateAsync(
    IReadOnlyList<string> positional,
    IReadOnlyDictionary<string, string> options,
    CancellationToken ct)
{
    if (positional.Count < 2)
    {
        Console.Error.WriteLine("用法：pcl update check|download <latest.json 地址>");
        return 1;
    }

    var subcommand = positional[0].ToLowerInvariant();
    var url = positional[1];

    if (subcommand == "check")
    {
        var info = await UpdateService.CheckAsync(url, AppInfo.Version, ct);
        if (info is null)
        {
            Console.WriteLine($"已是最新版本（v{AppInfo.Version}）。");
            return 0;
        }

        Console.WriteLine($"发现新版本 v{info.Version}");
        if (!string.IsNullOrWhiteSpace(info.Notes)) Console.WriteLine(info.Notes);
        return 0;
    }

    if (subcommand == "download")
    {
        var info = await UpdateService.CheckAsync(url, AppInfo.Version, ct);
        if (info is null)
        {
            Console.WriteLine($"已是最新版本（v{AppInfo.Version}）。");
            return 0;
        }

        var progress = new SyncProgress<JobProgress>(PrintProgress);
        var path = await UpdateService.DownloadAsync(info, progress, ct);
        Console.WriteLine();
        Console.WriteLine($"更新包已下载并校验：{path}");
        return 0;
    }

    Console.Error.WriteLine($"未知子命令：{subcommand}");
    return 1;
}

static async Task<int> VersionsAsync(
    IReadOnlyDictionary<string, string> options,
    CancellationToken ct)
{
    var repository = new VersionRepository(GetGameDirectory(options));
    var installed = repository.ListInstalled();

    Console.WriteLine("已安装：");
    if (installed.Count == 0)
    {
        Console.WriteLine("  （无）");
    }
    else
    {
        foreach (var version in installed)
            Console.WriteLine($"  {version.Id,-24} {version.Type,-10} {version.ReleaseTime}");
    }

    Console.WriteLine();
    Console.WriteLine("远程最新：");
    try
    {
        var manifest = await repository.GetManifestAsync(forceRefresh: true, ct);
        Console.WriteLine($"  正式版：{manifest.Latest.Release}");
        Console.WriteLine($"  快照：  {manifest.Latest.Snapshot}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  获取失败：{ex.Message}");
    }

    return 0;
}

static string GetGameDirectory(IReadOnlyDictionary<string, string> options)
{
    var configured = options.GetValueOrDefault("dir");
    return PclPaths.Expand(string.IsNullOrWhiteSpace(configured)
        ? PclPaths.DefaultGameDirectory
        : configured);
}

static MirrorMode ParseMirror(string? value) => value?.ToLowerInvariant() switch
{
    "official" => MirrorMode.Official,
    "bmclapi" => MirrorMode.Bmclapi,
    _ => MirrorMode.Auto
};

static int GetInt(IReadOnlyDictionary<string, string> options, string key, int fallback)
    => options.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
        ? parsed
        : fallback;

static void PrintProgress(JobProgress progress)
{
    var percent = progress.Percent.HasValue
        ? $"{progress.Percent.Value * 100,5:0.0}%"
        : "     ";
    var speed = progress.BytesPerSecond is > 0
        ? $"{progress.BytesPerSecond.Value / 1048576.0,6:0.0} MB/s"
        : "             ";
    var eta = progress.Eta.HasValue
        ? $" ETA {progress.Eta.Value:mm\\:ss}"
        : "";
    var detail = progress.Detail is null
        ? ""
        : progress.Detail.Length > 60 ? progress.Detail[..60] : progress.Detail;

    var text = $"{progress.Stage,-12} {percent} {speed}{eta}  {detail}";
    Console.Write('\r');
    Console.Write(text.PadRight(118));
}

static string FormatBytes(long bytes)
{
    string[] units = ["B", "KB", "MB", "GB"];
    double value = bytes;
    var unit = 0;
    while (value >= 1024 && unit < units.Length - 1)
    {
        value /= 1024;
        unit++;
    }
    return $"{value:0.0} {units[unit]}";
}

static (Dictionary<string, string> Options, List<string> Positionals) ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var positionals = new List<string>();

    for (var i = 0; i < args.Length; i++)
    {
        var argument = args[i];
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            positionals.Add(argument);
            continue;
        }

        var key = argument[2..];
        var equals = key.IndexOf('=');
        if (equals >= 0)
        {
            options[key[..equals]] = key[(equals + 1)..];
            continue;
        }

        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            options[key] = args[++i];
        }
        else
        {
            options[key] = "true";
        }
    }

    return (options, positionals);
}

internal sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}
