using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.Local;

namespace WMCL.Cli.Commands;

/// <summary>实例相关命令（克隆 / 重命名 / 删除 / 内容导入）。</summary>
public static class InstanceCommands
{
    public static int ContentImport(
        IReadOnlyList<string> positional,
        IReadOnlyDictionary<string, string> options)
    {
        if (positional.Count < 3 ||
            !string.Equals(positional[0], "import", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                "用法：wmcl content import <resourcepack|shader|world> <路径> --instance <实例> [--name <名称>]");
            return 1;
        }

        var kind = positional[1].ToLowerInvariant() switch
        {
            "resourcepack" => InstanceContentKind.ResourcePack,
            "shader" => InstanceContentKind.ShaderPack,
            "world" => InstanceContentKind.World,
            _ => throw new ArgumentException("类型必须是 resourcepack/shader/world")
        };

        var gameDirectory = CliHelpers.GetGameDirectory(options);
        var instance = options.GetValueOrDefault("instance");
        if (string.IsNullOrWhiteSpace(instance))
        {
            Console.Error.WriteLine("需要 --instance <实例>");
            return 1;
        }

        var repository = new VersionRepository(gameDirectory);
        var version = repository.GetVersionAsync(instance).GetAwaiter().GetResult();
        var contentDirectory = IsolationPolicy.GetGameDirectory(
            gameDirectory, instance, version, CliHelpers.GetIsolationMode(options));
        var item = InstanceContentService.Import(
            contentDirectory, kind, positional[2], options.GetValueOrDefault("name"));
        Console.WriteLine($"已导入：{item.Name}");
        Console.WriteLine(item.FullPath);
        return 0;
    }


    public static int CloneInstance(
        IReadOnlyList<string> positional,
        IReadOnlyDictionary<string, string> options)
    {
        if (positional.Count == 0)
        {
            Console.Error.WriteLine("用法：wmcl clone <实例> [--name <新实例名>] [--dir <游戏目录>]");
            return 1;
        }

        var id = InstanceManager.Clone(
            CliHelpers.GetGameDirectory(options), positional[0], options.GetValueOrDefault("name"));
        Console.WriteLine($"已克隆为：{id}");
        return 0;
    }


    public static int RenameInstance(
        IReadOnlyList<string> positional,
        IReadOnlyDictionary<string, string> options)
    {
        if (positional.Count < 2)
        {
            Console.Error.WriteLine("用法：wmcl rename <旧实例名> <新实例名> [--dir <游戏目录>]");
            return 1;
        }

        InstanceManager.Rename(CliHelpers.GetGameDirectory(options), positional[0], positional[1]);
        Console.WriteLine($"已重命名：{positional[0]} → {positional[1]}");
        return 0;
    }


    public static int DeleteInstance(
        IReadOnlyList<string> positional,
        IReadOnlyDictionary<string, string> options)
    {
        if (positional.Count < 1)
        {
            Console.Error.WriteLine("用法：wmcl delete <实例名> --yes [--dir <游戏目录>]");
            return 1;
        }

        if (!options.ContainsKey("yes"))
        {
            Console.Error.WriteLine("删除实例不可恢复；确认请加 --yes。");
            return 1;
        }

        InstanceManager.Delete(CliHelpers.GetGameDirectory(options), positional[0]);
        Console.WriteLine($"已删除实例：{positional[0]}");
        return 0;
    }
}
