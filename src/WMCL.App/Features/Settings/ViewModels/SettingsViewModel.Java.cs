using WMCL.Link;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.App.Services.Platform;
using WMCL.App.Services.Infrastructure;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Launch.Java;
using WMCL.Minecraft;

namespace WMCL.App.Features.Settings;

public partial class SettingsViewModel
{
    [RelayCommand]
    private async Task InstallJavaAsync()
    {
        if (IsInstallingJava) return;
        if (!int.TryParse(JavaInstallVersion, out var major)) major = 21;
        IsInstallingJava = true;
        JavaInstallProgress = 0;
        JavaInstallStatus = $"正在准备 Java {major}…";
        _javaInstallCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                JavaInstallProgress = (update.Percent ?? 0) * 100;
                JavaInstallStatus = $"{update.Stage} {JavaInstallProgress:0}%";
            });
            var runtime = await _services.JavaProvisioner.EnsureExactAsync(
                major, progress, _javaInstallCts.Token);
            JavaPath = runtime.Path;
            JavaInstallStatus = $"Java {runtime.MajorVersion} 已就绪：{runtime.Path}";
        }
        catch (OperationCanceledException)
        {
            JavaInstallStatus = "已取消 Java 安装";
        }
        catch (Exception ex)
        {
            JavaInstallStatus = "Java 安装失败：" + ex.Message;
        }
        finally
        {
            IsInstallingJava = false;
            JavaInstallProgress = 0;
            _javaInstallCts?.Dispose();
            _javaInstallCts = null;
        }
    }

    [RelayCommand]
    private void CancelJavaInstall() => _javaInstallCts?.Cancel();

    [RelayCommand]
    private async Task CleanJavaAsync()
    {
        if (IsInstallingJava) return;
        if (!await _confirm(
                "清理 Java", "删除本启动器下载的 mojang-* Java 运行时？系统 Java 不受影响。"))
            return;
        if (IsInstallingJava)
        {
            JavaInstallStatus = "Java 正在安装，请等待结束后再清理。";
            return;
        }
        try
        {
            using (var operation = _services.GameOperations.AcquireExclusive("清理 Java"))
            {
                var removed = _services.JavaProvisioner.RemoveDownloaded();
                JavaInstallStatus = $"已清理 {removed} 个下载的 Java 运行时";
            }
            await DetectJavaAsync();
        }
        catch (Exception exception)
        {
            JavaInstallStatus = "清理 Java 失败：" + exception.Message;
        }
    }

    [RelayCommand]
    private async Task DetectJavaAsync()
    {
        Status = "正在扫描 Java…";
        try
        {
            var runtimes = await _services.JavaLocator.DiscoverAsync(forceRefresh: true);
            JavaRuntimes.Clear();
            foreach (var runtime in runtimes) JavaRuntimes.Add(runtime);
            SelectedJavaRuntime = runtimes.FirstOrDefault(runtime =>
                                      string.Equals(runtime.Path, JavaPath, StringComparison.Ordinal));
            Status = runtimes.Count == 0
                ? "未发现 Java；启动时会自动下载 Mojang Runtime，也可手动填写路径"
                : "已发现：" + string.Join("，", runtimes.Select(r => $"Java {r.MajorVersion}"));
        }
        catch (Exception ex)
        {
            Status = "扫描失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void UseAutomaticJava()
    {
        JavaPath = "";
        SelectedJavaRuntime = null;
        Status = "Java 已设为自动：启动时按版本发现或下载 Mojang Runtime";
    }

    [RelayCommand]
    private void OpenConfigFolder() => OpenPath(WmclPaths.ConfigDirectory);
}
