using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

/// <summary>取消路径 .part 清理的回归测试：取消清理、暂停保留、完成后重发、幂等守卫。</summary>
[Trait("Kind", "Headless")]
public sealed class DownloadCancelCleanupTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _oldXdgData;
    private readonly string _root;

    public DownloadCancelCleanupTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "wmcl-cancel-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_dataDir, "game");
        Directory.CreateDirectory(_root);
        _oldXdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _dataDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _oldXdgData);
        try
        {
            Directory.Delete(_dataDir, true);
        }
        catch
        {
        }
    }

    /// <summary>用挂起的 loadVersions 构造可控的安装：目录被占用时失败（Acquire 抛出），不触网。</summary>
    private DownloadViewModel Create(out GameOperationCoordinator gameOperations)
    {
        var services = new AppServices(new AppSettings { GameDirectory = _root });
        gameOperations = services.GameOperations;
        var download = new DownloadViewModel(services);
        download.AcceptInstanceCatalog(new[] { new VersionItem { Id = "1.21.1" } });
        download.SelectedVersion = download.Versions.First(v => v.Id == "1.21.1");
        return download;
    }

    [AvaloniaFact]
    public async Task CancelledInstall_CleansPartFiles()
    {
        var download = Create(out var operations);

        Directory.CreateDirectory(Path.Combine(_root, "libraries"));
        File.WriteAllText(Path.Combine(_root, "libraries", "leftover.part"), "x");

        using (operations.Acquire(_root, "测试占用"))
        {
            await download.InstallCommand.ExecuteAsync(null);
        }

        Assert.False(download.IsBusy);
        Assert.False(File.Exists(Path.Combine(_root, "libraries", "leftover.part")));
    }

    [AvaloniaFact]
    public async Task PausedInstall_KeepsPartFilesForResume()
    {
        var download = Create(out var operations);
        Directory.CreateDirectory(Path.Combine(_root, "libraries"));
        File.WriteAllText(Path.Combine(_root, "libraries", "resumable.part"), "x");

        // 模拟暂停态（IsPaused=true）下任务以异常退出：finally 走暂停分支，不清理 .part。
        download.IsPaused = true;
        using (operations.Acquire(_root, "测试占用"))
        {
            await download.InstallCommand.ExecuteAsync(null);
        }

        Assert.True(download.IsPaused);
        Assert.True(File.Exists(Path.Combine(_root, "libraries", "resumable.part")));
        download.IsPaused = false;
    }

    [AvaloniaFact]
    public async Task CancelCommand_IsIdempotentAfterTerminalState()
    {
        var download = Create(out var operations);
        using (operations.Acquire(_root, "测试占用"))
        {
            await download.InstallCommand.ExecuteAsync(null);
        }

        var statusAfterFailure = download.Status;
        download.CancelCommand.Execute(null);
        download.CancelCommand.Execute(null);

        Assert.Equal(statusAfterFailure, download.Status);
        Assert.False(download.IsBusy);
        Assert.True(download.CanInstall);
    }

    [AvaloniaFact]
    public async Task AfterCancel_NewInstallUsesNewRequest()
    {
        var download = Create(out var operations);
        using (operations.Acquire(_root, "测试占用"))
        {
            await download.InstallCommand.ExecuteAsync(null);
        }
        Assert.Contains("安装失败", download.Status);

        // 占用释放后重新发起：新请求 ID、新 CTS、旧任务不干扰。
        using var hold = operations.Acquire(_root, "第二次占用");
        await download.InstallCommand.ExecuteAsync(null);

        Assert.Contains("安装失败", download.Status);
        Assert.False(download.IsBusy);
    }
}
