using Avalonia.Headless.XUnit;
using WMCL.Core.Update;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class UpdateFlowTests
{
    [AvaloniaFact]
    public async Task InvalidManifestIsNotReportedAsLatestVersion()
    {
        var model = await CheckAsync(_ => Task.FromResult(
            new UpdateCheckResult(UpdateCheckStatus.InvalidManifest, Error: "更新清单缺少 version。")));
        Assert.Contains("更新清单无效", model.Status);
        Assert.False(model.CanDownloadUpdate);
    }

    [AvaloniaFact]
    public async Task NewerReleaseEnablesDownloadAndBadVersionExplainsComparison()
    {
        var model = await CheckAsync(_ => Task.FromResult(new UpdateCheckResult(
            UpdateCheckStatus.UpdateAvailable,
            new UpdateInfo("99.0.0", "https://example.invalid/a.tar.gz", "sha", "说明"))));
        Assert.Contains("发现新版本 v99.0.0", model.Status);
        Assert.True(model.CanDownloadUpdate);

        var incomparable = await CheckAsync(_ => Task.FromResult(new UpdateCheckResult(
            UpdateCheckStatus.IncomparableVersion, Error: "无法比较版本")));
        Assert.Contains("无法检查更新", incomparable.Status);
        Assert.False(incomparable.CanDownloadUpdate);
    }

    private static async Task<UpdateViewModel> CheckAsync(
        Func<CancellationToken, Task<UpdateCheckResult>> check)
    {
        var services = new AppServices(new AppSettings(), saveSettings: _ => { });
        var model = new UpdateViewModel(services, check);
        await model.CheckCommand.ExecuteAsync(null);
        return model;
    }
}
