using WMCL.App.ViewModels.Accounts;
using WMCL.App.ViewModels.Launching;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.App.Services.Infrastructure;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Core.Logging;
using WMCL.Launch;
using WMCL.Launch.Auth;
using WMCL.Launch.Crash;
using WMCL.Launch.Diagnostics;
using WMCL.Minecraft;
using WMCL.Modding.Modpack;

namespace WMCL.App.Features.Launch;

public partial class LaunchViewModel
{
    [RelayCommand]
    private void SaveDiagnosticPackage()
    {
        if (!HasCrashReport || _lastCrashSnapshot is null) return;
        try
        {
            var path = WriteDiagnosticPackage(WmclPaths.LogsDirectory);
            Status = "诊断包已保存：" + path;
            OpenPath(path);
        }
        catch (Exception ex)
        {
            Status = "保存诊断包失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenCrashReport()
    {
        var path = _lastCrashReport?.CrashReportPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Status = "没有找到 Minecraft crash-report 文件";
            return;
        }
        OpenPath(path);
    }

    [RelayCommand]
    private void OpenCrashLog()
    {
        var path = _lastCrashReport?.LogPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Status = "没有找到崩溃日志文件";
            return;
        }
        OpenPath(path);
    }

    [RelayCommand]
    private void OpenCrashLogFolder()
    {
        var path = _lastCrashReport?.LogPath is { } log && File.Exists(log)
            ? Path.GetDirectoryName(log)!
            : Path.Combine(_lastCrashSnapshot?.Session.GameDirectory ?? _services.Settings.GameDirectory, "logs");
        if (Directory.Exists(path)) OpenPath(path);
        else Status = "日志目录不存在：" + path;
    }

    [RelayCommand]
    private void SaveCrashReport()
    {
        if (!HasCrashReport || _lastCrashSnapshot is null) return;
        try
        {
            var path = WriteCrashReport(WmclPaths.LogsDirectory);
            Status = "崩溃报告已保存：" + path;
            OpenPath(path);
        }
        catch (Exception ex)
        {
            Status = "保存崩溃报告失败：" + ex.Message;
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            ExternalLauncher.Open(path);
        }
        catch
        {
            // 忽略
        }
    }
}
