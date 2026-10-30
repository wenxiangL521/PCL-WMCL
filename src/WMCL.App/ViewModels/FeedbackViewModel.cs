using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;

namespace WMCL.App.ViewModels;

/// <summary>
/// 反馈页 ViewModel：生成诊断信息、复制到剪贴板、导出崩溃报告、打开日志目录。
/// 与 PCL-ME 的 SetupFeedbackRightPaneView 对齐。
/// </summary>
public partial class FeedbackViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public FeedbackViewModel(AppServices services)
    {
        _services = services;
        Diagnostics = BuildDiagnostics();
        Status = "诊断信息已生成";
    }

    [ObservableProperty]
    private string _diagnostics = "";

    [ObservableProperty]
    private string _status = "";

    public string ReportUrl => "https://github.com/wenxiangL521/PCL-WMCL/issues/new";

    [RelayCommand]
    private async Task GenerateDiagnosticsAsync()
    {
        Diagnostics = BuildDiagnostics();
        Status = "诊断信息已生成";
    }

    [RelayCommand]
    private async Task CopyDiagnosticsAsync()
    {
        try
        {
            var topLevel = Application.Current is not null
                ? TopLevel.GetTopLevel(Application.Current.ApplicationLifetime is
                    IClassicDesktopStyleApplicationLifetime { MainWindow: { } win } ? win : null)
                : null;
            var clipboard = topLevel?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(Diagnostics);
                Status = "诊断信息已复制到剪贴板";
            }
            else
            {
                Status = "剪贴板不可用，请手动选择文本复制";
            }
        }
        catch (Exception ex)
        {
            Status = "复制失败：" + ex.Message;
        }
    }

    /// <summary>导出崩溃报告为 zip 文件（诊断信息 + 环境快照 + 日志）。</summary>
    [RelayCommand]
    private async Task ExportCrashReportAsync()
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var outputDir = Path.Combine(WmclPaths.DataDirectory, "exports");
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, $"WMCL-Diagnostics-{timestamp}.zip");

            using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);

            var diagEntry = archive.CreateEntry("diagnostics.txt");
            await using (var stream = diagEntry.Open())
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(BuildDiagnostics()));
            }

            var envEntry = archive.CreateEntry("environment.json");
            await using (var stream = envEntry.Open())
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(BuildEnvironmentSnapshot()));
            }

            var logsDir = WmclPaths.LogsDirectory;
            if (Directory.Exists(logsDir))
            {
                foreach (var logFile in Directory.EnumerateFiles(logsDir, "*.log")
                             .OrderByDescending(f => File.GetLastWriteTime(f))
                             .Take(10))
                {
                    var entryName = "logs/" + Path.GetFileName(logFile);
                    var entry = archive.CreateEntry(entryName);
                    await using (var stream = entry.Open())
                    {
                        await using var input = File.OpenRead(logFile);
                        await input.CopyToAsync(stream);
                    }
                }
            }

            Status = $"已导出崩溃报告：{outputPath}";
        }
        catch (Exception ex)
        {
            Status = "导出崩溃报告失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(WmclPaths.LogsDirectory);
            ExternalLauncher.Open(WmclPaths.LogsDirectory);
            Status = "已打开日志目录";
        }
        catch (Exception ex)
        {
            Status = "打开日志目录失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenReport()
    {
        try
        {
            ExternalLauncher.Open(ReportUrl);
            Status = "已在浏览器中打开问题反馈页面";
        }
        catch (Exception ex)
        {
            Status = "打开反馈页面失败：" + ex.Message;
        }
    }

    private string BuildDiagnostics()
    {
        var sb = new StringBuilder();
        var settings = _services.Settings;
        sb.AppendLine("=== PCL WMCL 诊断信息 ===");
        sb.AppendLine($"版本：{GetVersion()}");
        sb.AppendLine($"操作系统：{RuntimeInformation.OSDescription}");
        sb.AppendLine($"架构：{RuntimeInformation.OSArchitecture}");
        sb.AppendLine($"游戏目录：{settings.GameDirectory}");
        sb.AppendLine($"Java 路径：{settings.JavaPath ?? "自动"}");
        sb.AppendLine($"进程优先级：{settings.ProcessPriority}");
        sb.AppendLine($"高性能显卡：{settings.HighPerformanceGpu}");
        sb.AppendLine($"版本隔离：{settings.IsolationMode}");
        sb.AppendLine($"下载线程数：{settings.DownloadThreads}");
        sb.AppendLine($"下载限速：{settings.DownloadSpeedLimitMbps} Mbps");
        sb.AppendLine($"镜像：{settings.Mirror}");
        sb.AppendLine($"账号类型：{settings.AccountType}");
        sb.AppendLine($"玩家名：{settings.PlayerName}");
        sb.AppendLine($"主题：{settings.Theme}");
        sb.AppendLine();
        sb.AppendLine("=== 日志文件 ===");
        if (!Directory.Exists(WmclPaths.LogsDirectory))
        {
            sb.AppendLine("（日志目录不存在）");
        }
        else
        {
            foreach (var file in Directory.EnumerateFiles(WmclPaths.LogsDirectory, "*.log")
                         .OrderByDescending(f => File.GetLastWriteTime(f))
                         .Take(5))
            {
                sb.AppendLine($"{file} ({new FileInfo(file).Length} bytes)");
            }
        }
        return sb.ToString();
    }

    private string BuildEnvironmentSnapshot()
    {
        var snapshot = new
        {
            version = GetVersion(),
            os = RuntimeInformation.OSDescription,
            arch = RuntimeInformation.OSArchitecture.ToString(),
            runtime = RuntimeInformation.FrameworkDescription,
            managed = Environment.Version.ToString(),
            processor = $"{Environment.ProcessorCount} cores",
            totalMemory = $"{GC.GetTotalMemory(false) / 1024 / 1024} MB",
            gcServer = System.Runtime.GCSettings.IsServerGC,
            account = new
            {
                type = _services.Settings.AccountType,
                name = _services.Settings.PlayerName
            },
            settings = new
            {
                gameDirectory = _services.Settings.GameDirectory,
                javaPath = _services.Settings.JavaPath ?? "auto",
                memory = _services.Settings.MemoryMb,
                downloadThreads = _services.Settings.DownloadThreads,
                mirror = _services.Settings.Mirror
            }
        };
        return JsonSerializer.Serialize(snapshot,
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static string GetVersion()
    {
        var assembly = typeof(FeedbackViewModel).Assembly;
        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
