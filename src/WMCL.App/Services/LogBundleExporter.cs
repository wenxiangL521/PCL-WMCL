using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using WMCL.Core;
using WMCL.Minecraft;

namespace WMCL.App.Services;

public sealed record LogBundleResult(string Path, int FileCount, long Bytes);

/// <summary>
/// 一键导出诊断包：环境信息、脱敏后的设置、启动器日志，以及各实例的日志与崩溃报告。
/// 只读源文件；凭据与令牌在写入前统一脱敏。
/// </summary>
public static class LogBundleExporter
{
    private static readonly string[] LauncherLogPatterns = ["*.log", "*.txt"];
    private static readonly string[] InstanceLogFiles = ["logs/latest.log", "logs/debug.log", "crash-reports"];

    public static async Task<LogBundleResult> ExportAsync(
        AppServices services, string targetPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        var destination = Path.GetFullPath(targetPath);
        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, ".wmcl-logs-" + Guid.NewGuid().ToString("N") + ".tmp");
        var count = 0;
        long bytes = 0;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                bytes += await WriteTextAsync(archive, "diagnostics.txt", BuildDiagnostics(services), ct);
                count++;
                bytes += await WriteTextAsync(archive, "settings.json",
                    AppSettingsSerializer.ExportToJson(services.Settings), ct);
                count++;
                foreach (var log in EnumerateLauncherLogs())
                {
                    ct.ThrowIfCancellationRequested();
                    bytes += await CopyFileAsync(archive, log, "launcher/" + Path.GetFileName(log), ct);
                    count++;
                }
                foreach (var file in EnumerateInstanceFiles(services))
                {
                    ct.ThrowIfCancellationRequested();
                    bytes += await CopyFileAsync(archive, file.Source, file.Entry, ct);
                    count++;
                }
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
            return new LogBundleResult(destination, count, bytes);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string BuildDiagnostics(AppServices services)
    {
        var builder = new StringBuilder();
        builder.AppendLine("WMCL 诊断信息");
        builder.AppendLine("生成时间：" + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        builder.AppendLine("启动器版本：" + (typeof(LogBundleExporter).Assembly.GetName().Version?.ToString() ?? "未知"));
        builder.AppendLine("运行时：" + RuntimeInformation.FrameworkDescription);
        builder.AppendLine("操作系统：" + RuntimeInformation.OSDescription + " / " + RuntimeInformation.OSArchitecture);
        builder.AppendLine("进程架构：" + RuntimeInformation.ProcessArchitecture);
        builder.AppendLine("游戏目录：" + services.Settings.GameDirectory);
        builder.AppendLine("版本隔离：" + services.Settings.IsolationMode);
        builder.AppendLine("分配内存：" + services.Settings.MemoryMb + " MB");
        builder.AppendLine("Java 路径：" + (string.IsNullOrWhiteSpace(services.Settings.JavaPath) ? "自动" : services.Settings.JavaPath));
        builder.AppendLine("主题：" + services.Settings.Theme + "，UI 缩放：" + services.Settings.FontScale.ToString("0.00"));
        builder.AppendLine("账号类型：" + services.Settings.AccountType);
        builder.AppendLine();
        builder.AppendLine("已安装实例：");
        try
        {
            foreach (var version in new VersionRepository(services.Settings.GameDirectory).ListInstalled())
                builder.AppendLine($"- {version.Id}：{version.Type}，{version.ReleaseTime:yyyy-MM-dd}");
        }
        catch (Exception exception)
        {
            builder.AppendLine("- 读取实例列表失败：" + exception.Message);
        }
        return SensitiveTextRedactor.Redact(builder.ToString()) ?? "";
    }

    private static IEnumerable<string> EnumerateLauncherLogs()
    {
        var directory = WmclPaths.LogsDirectory;
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory)
            .Where(path => LauncherLogPatterns.Any(pattern =>
                path.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(File.GetLastWriteTime)
            .Take(20);
    }

    private static IEnumerable<(string Source, string Entry)> EnumerateInstanceFiles(AppServices services)
    {
        var root = services.Settings.GameDirectory;
        var versions = Path.Combine(root, "versions");
        if (!Directory.Exists(versions)) yield break;
        foreach (var directory in Directory.EnumerateDirectories(versions))
        {
            var id = Path.GetFileName(directory);
            var gameDirectory = directory;
            try
            {
                var version = new VersionRepository(root).GetLocalVersionAsync(id).GetAwaiter().GetResult();
                gameDirectory = IsolationPolicy.GetGameDirectory(root, id, version, services.Settings.IsolationMode);
            }
            catch (Exception)
            {
                // 版本信息读不出来时仍尝试实例目录下的日志。
            }
            // 隔离与非隔离实例的日志位置不同，两处都找一遍。
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var baseDirectory in new[] { gameDirectory, directory }.Distinct(StringComparer.Ordinal))
                foreach (var relative in InstanceLogFiles)
                {
                    var path = Path.Combine(baseDirectory, relative);
                    if (Directory.Exists(path))
                    {
                        foreach (var crash in Directory.EnumerateFiles(path).OrderByDescending(File.GetLastWriteTime).Take(10))
                            if (seen.Add(crash))
                                yield return (crash, $"instances/{id}/crash-reports/{Path.GetFileName(crash)}");
                    }
                    else if (File.Exists(path) && seen.Add(path))
                    {
                        yield return (path, $"instances/{id}/{relative.Replace('\\', '/')}");
                    }
                }
        }
    }

    private static async Task<long> WriteTextAsync(ZipArchive archive, string entryName, string text, CancellationToken ct)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var output = entry.Open();
        var data = Encoding.UTF8.GetBytes(text);
        await output.WriteAsync(data, ct);
        return data.Length;
    }

    private static async Task<long> CopyFileAsync(ZipArchive archive, string source, string entryName, CancellationToken ct)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        await using var output = entry.Open();
        await input.CopyToAsync(output, ct);
        return input.Length;
    }
}
