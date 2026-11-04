using WMCL.Cli.Commands;
using System.Text;
using System.Text.Json;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Core.Logging;
using WMCL.Download;
using WMCL.Launch;
using WMCL.Launch.Auth;
using WMCL.Launch.Java;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Fabric;
using WMCL.Modding.Forgelike;
using WMCL.Modding.Local;
using WMCL.Core.Update;
using WMCL.Modding.Modpack;
using WMCL.Modding.OptiFine;
using WMCL.Modding.Modrinth;

Console.OutputEncoding = Encoding.UTF8;
WmclPaths.EnsureCreated();

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
        "install" => await InstallCommands.InstallAsync(positional, options, cts.Token),
        "launch" => await GameCommands.LaunchAsync(positional, options, cts.Token),
        "java" => await InstallCommands.JavaAsync(positional, options, cts.Token),
        "fabric" => await InstallCommands.FabricAsync(positional, options, cts.Token),
        "forgelike" => await InstallCommands.ForgelikeAsync(positional, options, cts.Token),
        "curseforge" => await GameCommands.CurseForgeAsync(positional, options, cts.Token),
        "modpack" => await ResourceCommands.ModpackAsync(positional, options, cts.Token),
        "export" => await ResourceCommands.ExportAsync(positional, options, cts.Token),
        "import" => await ResourceCommands.ImportAsync(positional, options, cts.Token),
        "content" => InstanceCommands.ContentImport(positional, options),
        "optifine" => await InstallCommands.OptiFineAsync(positional, options, cts.Token),
        "clone" => InstanceCommands.CloneInstance(positional, options),
        "rename" => InstanceCommands.RenameInstance(positional, options),
        "delete" => InstanceCommands.DeleteInstance(positional, options),
        "modrinth" => await ResourceCommands.ModrinthAsync(positional, options, cts.Token),
        "update" => await GameCommands.UpdateAsync(positional, options, cts.Token),
        "login" => await GameCommands.LoginAsync(positional, options, cts.Token),
        "server" => await GameCommands.ServerAsync(positional, options, cts.Token),
        "versions" => await GameCommands.VersionsAsync(options, cts.Token),
        "cleanroom" => await GameCommands.CleanroomAsync(positional, options, cts.Token),
        "labymod" => await GameCommands.LabyModAsync(positional, cts.Token),
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
        WMCL CLI 0.1

        用法：
          wmcl versions [--dir <游戏目录>]
          wmcl cleanroom versions
          wmcl cleanroom download <tag> <保存路径>
          wmcl cleanroom install <tag> [--java <路径>] [--dir <游戏目录>] [--no-complete]
          wmcl labymod versions
          wmcl labymod download <production|snapshot> <保存路径>
          wmcl install <版本> [--dir <游戏目录>] [--mirror auto|official|bmclapi] [--threads 16] [--verify]
          wmcl launch <版本> (--offline <玩家名> | --microsoft) [--dir <游戏目录>] [--isolation auto|isolated|global] [--memory 4096]
                     [--java <路径>] [--width 854] [--height 480] [--server <地址>] [--port 25565]
          wmcl java list [--java <路径>]
          wmcl java install <主版本> [--force]
          wmcl fabric loaders <MC版本>
          wmcl fabric install <MC版本> [--loader <Fabric版本>] [--dir <游戏目录>]
          wmcl forgelike versions <MC版本> --loader forge|neoforge
          wmcl forgelike install <MC版本> --loader forge|neoforge [--version <加载器版本>] [--java <路径>] [--dir <游戏目录>]
          wmcl curseforge search <关键词> --game <MC版本> --loader <加载器> [--api-key <Key>]
          wmcl curseforge install <modId> --game <MC版本> --loader <加载器> [--instance <实例>] [--api-key <Key>]
          wmcl modpack install <本地.mrpack> [--name <实例名>] [--dir <游戏目录>] [--no-optional]
          wmcl modpack install-modrinth <projectId> [--game <MC版本>] [--name <实例名>] [--no-optional]
          wmcl modpack export <实例> [--output <mrpack 路径>]
          wmcl export <实例> [--output <zip 路径>] [--dir <游戏目录>]
          wmcl import <实例导出.zip> [--name <新实例名>] [--dir <游戏目录>]
          wmcl content import <resourcepack|shader|world> <路径> --instance <实例> [--name <名称>] [--isolation auto|isolated|global]
          wmcl optifine versions <MC版本>
          wmcl optifine install <MC版本> [--patch <I6>] [--java <路径>]
          wmcl clone <实例> [--name <新实例名>] [--dir <游戏目录>]
          wmcl rename <旧实例名> <新实例名> [--dir <游戏目录>]
          wmcl delete <实例名> --yes [--dir <游戏目录>]
          wmcl modrinth search <关键词> --game <MC版本> [--loader ...] [--type mod|resourcepack|shader]
          wmcl modrinth install <slug> --game <MC版本> [--type ...] [--loader ...] [--instance <实例>] [--dir <游戏目录>]
          wmcl modrinth updates --instance <实例> [--apply]
          wmcl update check <latest.json 地址>
          wmcl update download <latest.json 地址>
          wmcl login microsoft --client-id <Azure Client ID>
          wmcl login status
          wmcl login logout
          wmcl server ping <地址:端口>
          wmcl server list <实例> [--dir <游戏目录>]
          wmcl server add <实例> <名称> <地址:端口> [--dir <游戏目录>]
          wmcl server edit <实例> <名称或地址> [--name <新名称>] [--address <新地址>]
          wmcl server remove <实例> <名称或地址> [--dir <游戏目录>]
          wmcl server list <实例> --json
          wmcl server ping <地址> --json
          wmcl help

        示例：
          wmcl install 1.21.1
          wmcl launch 1.21.1 --offline Steve
        """);
    return 0;
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
