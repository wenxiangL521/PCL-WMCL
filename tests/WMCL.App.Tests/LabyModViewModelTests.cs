using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Core.Jobs;
using WMCL.Modding.Forgelike;

namespace WMCL.App.Tests;

public sealed class LabyModViewModelTests
{
    private static readonly LabyModVersionInfo Version = new(
        "production", "47.4", "https://example.invalid/installer.jar",
        "LabyMod4ProductionInstaller.jar");

    [AvaloniaFact]
    public async Task ShowsTwoChannelsAndDownloadsInstaller()
    {
        var snapshot = Version with { Channel = "snapshot", Version = "48", InstallerFileName = "LabyMod4SnapshotInstaller.jar" };
        var destination = Path.Combine(Path.GetTempPath(), "labymod-view-" + Guid.NewGuid().ToString("N") + ".jar");
        var model = new LabyModViewModel(
            _ => Task.FromResult<IReadOnlyList<LabyModVersionInfo>>([Version, snapshot]),
            async (version, path, progress, _) =>
            {
                await File.WriteAllTextAsync(path, version.InstallerFileName);
                progress?.Report(new JobProgress("下载 LabyMod 安装器", 1));
                return path;
            });
        var view = new LabyModView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            await model.LoadAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, model.Versions.Count);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "正式版");
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "快照版");
            await model.DownloadInstallerAsync(Version, destination);
            Assert.Equal(Version.InstallerFileName, await File.ReadAllTextAsync(destination));
            Assert.Contains("安装器已保存", model.DownloadStatus);
        }
        finally
        {
            window.Close();
            if (File.Exists(destination)) File.Delete(destination);
        }
    }
}
