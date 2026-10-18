using System.IO.Compression;
using System.Text;

namespace PCL.Launch.Diagnostics;

/// <summary>导出最小崩溃诊断包：报告、日志、系统信息、实例 Mod 清单。</summary>
public static class DiagnosticPackage
{
    public static string Export(
        string outputDirectory,
        string instanceId,
        string reportText,
        string? logPath,
        string? gameDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(
            outputDirectory,
            $"diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        AddText(archive, "report.txt", reportText);
        AddText(archive, "instance.txt", "实例：" + instanceId);
        AddText(archive, "system.txt", BuildSystemInfo());
        if (!string.IsNullOrWhiteSpace(logPath) && File.Exists(logPath))
            archive.CreateEntryFromFile(logPath, "latest.log", CompressionLevel.Optimal);
        if (!string.IsNullOrWhiteSpace(gameDirectory))
            AddModList(archive, gameDirectory);
        AddText(archive, "README.txt",
            "PCL2 Linux 诊断包\n"
            + "包含崩溃报告、latest.log、系统信息、实例 Mod 清单。\n"
            + "请在反馈问题时附上本文件；其中可能包含账号名/路径，分享前请检查。\n");
        return path;
    }

    private static void AddModList(ZipArchive archive, string gameDirectory)
    {
        var modsDirectory = Path.Combine(gameDirectory, "mods");
        var builder = new StringBuilder();
        if (!Directory.Exists(modsDirectory))
        {
            AddText(archive, "mods.txt", "没有找到 mods 目录。");
            return;
        }
        foreach (var file in Directory.EnumerateFiles(modsDirectory, "*.jar")
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(file);
            builder.AppendLine($"{info.Name}\t{info.Length} bytes");
        }
        AddText(archive, "mods.txt", builder.ToString());
    }

    private static string BuildSystemInfo() =>
        $"""
        OS: {Environment.OSVersion}
        Runtime: .NET {Environment.Version}
        Architecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}
        CPU: {Environment.ProcessorCount} cores
        Time: {DateTimeOffset.Now:O}
        """;

    private static void AddText(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
