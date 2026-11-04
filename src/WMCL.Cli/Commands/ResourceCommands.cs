using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Launch.Java;
using WMCL.Minecraft;
using WMCL.Modding.Modpack;
using WMCL.Modding.Modrinth;

namespace WMCL.Cli.Commands;

/// <summary>资源与整合包命令（Modrinth / 整合包 / 导出 / 导入）。</summary>
public static class ResourceCommands
{
public static async Task<int> ModrinthAsync(
    IReadOnlyList<string> positional,
    IReadOnlyDictionary<string, string> options,
    CancellationToken ct)
{
    if (positional.Count == 0)
    {
        Console.Error.WriteLine("用法：wmcl modrinth search <关键词> --game <MC版本> | wmcl modrinth install <slug> --game <MC版本>");
        return 1;
    }

    var subcommand = positional[0].ToLowerInvariant();
    var gameVersion = options.GetValueOrDefault("game");
    var gameDirectory = CliHelpers.GetGameDirectory(options);
    var modsDirectory = Path.Combine(gameDirectory, "mods");
    var instance = options.GetValueOrDefault("instance");
    var loader = options.GetValueOrDefault("loader") ?? "fabric";
    var projectType = (options.GetValueOrDefault("type") ?? "mod").ToLowerInvariant();
    var contentDirectory = gameDirectory;

    if (!string.IsNullOrWhiteSpace(instance))
    {
        var repository = new VersionRepository(gameDirectory);
        var version = await repository.GetVersionAsync(instance, ct);
        gameVersion ??= MinecraftVersionUtil.GetBaseGameVersion(instance, version);
        loader = MinecraftVersionUtil.GetLoader(version);
        contentDirectory = IsolationPolicy.GetGameDirectory(
            gameDirectory, instance, version, CliHelpers.GetIsolationMode(options));
        modsDirectory = Path.Combine(contentDirectory, "mods");
    }

    var targetDirectory = projectType switch
    {
        "resourcepack" => Path.Combine(contentDirectory, "resourcepacks"),
        "shader" => Path.Combine(contentDirectory, "shaderpacks"),
        _ => modsDirectory
    };

    if (string.IsNullOrWhiteSpace(gameVersion))
    {
        Console.Error.WriteLine("缺少 --game 或 --instance，例如：--game 1.21.1");
        return 1;
    }

    var client = new ModrinthClient();

    if (subcommand == "updates")
    {
        if (string.IsNullOrWhiteSpace(instance))
        {
            Console.Error.WriteLine("updates 需要 --instance <实例>");
            return 1;
        }

        var progress = new SyncProgress<JobProgress>(CliHelpers.PrintProgress);
        var updateService = new ModUpdateService(client);
        var check = await updateService.CheckDetailedAsync(
            modsDirectory, gameVersion, loader, progress, ct);
        var updates = check.Updates;
        Console.WriteLine();
        Console.WriteLine(check.Summary);
        if (updates.Count == 0)
        {
            return check.Items.Any(item => item.State == ModUpdateCheckState.Failed) ? 1 : 0;
        }

        foreach (var update in updates)
            Console.WriteLine($"  {Path.GetFileName(update.FilePath)}  {update.CurrentVersion} → {update.Latest.VersionNumber}");

        if (options.ContainsKey("apply"))
        {
            var downloadProgress = new SyncProgress<JobProgress>(CliHelpers.PrintProgress);
            var result = await updateService.ApplyAsync(updates, modsDirectory, gameVersion, loader, downloadProgress, ct);
            Console.WriteLine($"已更新 {updates.Count} 个所选 Mod 及其必需前置。" +
                (result.RecoveryDirectory is { } recovery ? $"{result.RecoveryWarning}；原始备份保留于：{recovery}" : "旧版 Mod 已移入回收站。"));
        }

        return check.Items.Any(item => item.State == ModUpdateCheckState.Failed) ? 1 : 0;
    }

    if (subcommand == "search")
    {
        var query = positional.Count > 1 ? string.Join(' ', positional.Skip(1)) : "";
        var result = await client.SearchAsync(
            query, gameVersion, loader, ct: ct, projectType: projectType);
        Console.WriteLine($"共 {result.TotalHits} 个结果：");
        foreach (var project in result.Projects.Take(20))
            Console.WriteLine($"  {project.Slug,-28} {project.Downloads,10}  {project.Title}");
        return 0;
    }

    if (subcommand == "install")
    {
        if (positional.Count < 2)
        {
            Console.Error.WriteLine("缺少项目 slug，例如：wmcl modrinth install sodium");
            return 1;
        }

        var slug = positional[1];
        var progress = new SyncProgress<JobProgress>(CliHelpers.PrintProgress);
        var result = projectType == "mod"
            ? await client.InstallAsync(
                slug, gameVersion, modsDirectory, loader, progress, ct)
            : await client.InstallContentAsync(
                slug, gameVersion, targetDirectory, projectType, progress, ct);
        Console.WriteLine();
        Console.WriteLine(result.Files.Count == 0
            ? "没有需要安装的文件（可能已存在）。"
            : $"安装 {(projectType switch { "resourcepack" => "资源包", "shader" => "光影", _ => "Mod" })}：" +
              string.Join("，", result.Files));
        return 0;
    }

    Console.Error.WriteLine($"未知子命令：{subcommand}");
    return 1;
}


public static async Task<int> ModpackAsync(
    IReadOnlyList<string> positional,
    IReadOnlyDictionary<string, string> options,
    CancellationToken ct)
{
    if (positional.Count < 2)
    {
        Console.Error.WriteLine(
            "用法：wmcl modpack install <本地.mrpack> [--name <实例名>] [--no-optional]；wmcl modpack install-modrinth <projectId> [--no-optional]");
        return 1;
    }

    var subcommand = positional[0].ToLowerInvariant();
    var gameDirectory = CliHelpers.GetGameDirectory(options);
    var repository = new VersionRepository(gameDirectory);

    if (subcommand == "export")
    {
        var exported = await ModpackExporter.ExportAsync(
            gameDirectory, positional[1], options.GetValueOrDefault("output"), ct);
        Console.WriteLine($"已导出 {exported.FileCount} 个文件，{exported.Bytes / 1024d / 1024d:0.0} MB");
        Console.WriteLine(exported.ArchivePath);
        return 0;
    }

    var installer = new VersionInstaller(repository);
    var javaLocator = new JavaLocator();
    var javaProvisioner = new JavaProvisioner(javaLocator);
    var service = new ModpackInstaller(
        repository, installer, null, null, null, null,
        javaRequirementResolver: async (request, token) =>
            (await javaProvisioner.EnsureAsync(
                JavaRequirement.ForInstall(request.Minecraft, request.Loader, request.LoaderVersion),
                new SyncProgress<JobProgress>(CliHelpers.PrintProgress), token)).Path);
    var name = options.GetValueOrDefault("name");
    var progress = new SyncProgress<JobProgress>(CliHelpers.PrintProgress);

    string id;
    if (subcommand == "install")
    {
        id = await service.InstallFromFileAsync(positional[1], name, progress, ct, !options.ContainsKey("no-optional"));
    }
    else if (subcommand == "install-modrinth")
    {
        id = await service.InstallFromModrinthAsync(
            positional[1], options.GetValueOrDefault("game"), name, progress, ct, !options.ContainsKey("no-optional"));
    }
    else
    {
        Console.Error.WriteLine($"未知子命令：{subcommand}");
        return 1;
    }

    Console.WriteLine();
    Console.WriteLine($"Modrinth 整合包安装完成：{id}");
    return 0;
}


public static async Task<int> ExportAsync(
    IReadOnlyList<string> positional,
    IReadOnlyDictionary<string, string> options,
    CancellationToken ct)
{
    if (positional.Count == 0)
    {
        Console.Error.WriteLine("用法：wmcl export <实例> [--output <zip 路径>] [--dir <游戏目录>]");
        return 1;
    }

    var gameDirectory = CliHelpers.GetGameDirectory(options);
    var result = await InstanceExporter.ExportAsync(
        gameDirectory, positional[0], options.GetValueOrDefault("output"), ct);
    Console.WriteLine($"已导出 {result.FileCount} 个文件，{result.Bytes / 1024d / 1024d:0.0} MB");
    Console.WriteLine(result.ArchivePath);
    return 0;
}


public static async Task<int> ImportAsync(
    IReadOnlyList<string> positional,
    IReadOnlyDictionary<string, string> options,
    CancellationToken ct)
{
    if (positional.Count == 0)
    {
        Console.Error.WriteLine("用法：wmcl import <实例导出.zip> [--name <新实例名>] [--dir <游戏目录>]");
        return 1;
    }

    var gameDirectory = CliHelpers.GetGameDirectory(options);
    var progress = new SyncProgress<JobProgress>(CliHelpers.PrintProgress);
    var result = await InstanceImporter.ImportAsync(
        gameDirectory, positional[0], options.GetValueOrDefault("name"),
        installMissing: true, progress, ct);
    Console.WriteLine();
    Console.WriteLine($"导入完成：{result.Id}");
    Console.WriteLine(result.VersionDirectory);
    return 0;
}
}
