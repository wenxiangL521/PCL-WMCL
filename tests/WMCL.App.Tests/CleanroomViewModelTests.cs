using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Core.Jobs;
using WMCL.Modding.Forgelike;

namespace WMCL.App.Tests;

public sealed class CleanroomViewModelTests
{
    private static readonly CleanroomVersionInfo Version = new(
        "0.7.11-alpha", DateTimeOffset.Parse("2026-10-19T10:09:31Z"),
        "https://example.invalid/installer.jar", "cleanroom-0.7.11-alpha-installer.jar",
        true, "https://example.invalid/release");

    [AvaloniaFact]
    public async Task InstallsThroughInjectedBackendAndRaisesInstalledEvent()
    {
        var calls = 0;
        var installed = new List<string>();
        var model = new CleanroomViewModel(
            _ => Task.FromResult<IReadOnlyList<CleanroomVersionInfo>>([Version]),
            (_, _, _, _) => Task.FromResult("unused"),
            (version, progress, _, _) =>
            {
                calls++;
                progress?.Report(new JobProgress("运行 Cleanroom 安装器", 1));
                return Task.FromResult("Cleanroom-" + version.Tag);
            });
        model.VersionInstalled += (_, id) => installed.Add(id);
        Assert.True(model.CanInstall);
        await model.InstallAsync(Version);
        Assert.Equal(1, calls);
        Assert.Contains("Cleanroom-0.7.11-alpha", model.InstallStatus);
        Assert.Single(installed);
        Assert.False(model.IsInstalling);
    }

    [AvaloniaFact]
    public async Task ShowsVersionsAndDownloadsInstallerToChosenPath()
    {
        var destination = Path.Combine(Path.GetTempPath(), "cleanroom-view-" + Guid.NewGuid().ToString("N") + ".jar");
        var model = new CleanroomViewModel(
            _ => Task.FromResult<IReadOnlyList<CleanroomVersionInfo>>([Version]),
            async (version, path, progress, _) =>
            {
                await File.WriteAllTextAsync(path, version.InstallerFileName);
                progress?.Report(new JobProgress("下载 Cleanroom 安装器", 1));
                return path;
            });
        var view = new CleanroomView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            await model.LoadAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(model.Versions);
            Assert.Contains("1 个 Cleanroom 版本", model.Status);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "0.7.11-alpha");

            await model.DownloadInstallerAsync(Version, destination);
            Assert.True(File.Exists(destination));
            Assert.Equal(Version.InstallerFileName, await File.ReadAllTextAsync(destination));
            Assert.Equal("安装器已保存：" + destination, model.DownloadStatus);
            Assert.False(model.IsDownloading);
        }
        finally
        {
            window.Close();
            if (File.Exists(destination)) File.Delete(destination);
        }
    }
}
