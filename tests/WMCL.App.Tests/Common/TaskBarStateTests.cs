using WMCL.App.Services;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class TaskBarStateTests
{
    private static TaskBarSnapshot Snapshot(
        bool downloadBusy = false, bool modpackBusy = false, bool launchBusy = false,
        double progress = 0, bool importing = false)
        => TaskBarState.From(
            downloadBusy, true, progress,
            false, true, progress,
            false, 0, false, 0, false, 0, false, 0, false, 0,
            modpackBusy, progress,
            false, 0, importing,
            false, false, launchBusy, progress);

    [Fact]
    public void IdleWhenNothingBusy()
    {
        var snapshot = Snapshot();
        Assert.False(snapshot.Active);
        Assert.Equal("", snapshot.Text);
        Assert.Equal("", snapshot.PercentText);
    }

    [Fact]
    public void DownloadWinsOverOtherBusyPages()
    {
        var snapshot = Snapshot(downloadBusy: true, modpackBusy: true, progress: 42);
        Assert.True(snapshot.Active);
        Assert.Equal("下载/安装 Minecraft", snapshot.Text);
        Assert.Equal("42%", snapshot.PercentText);
    }

    [Fact]
    public void ImportingShowsIndeterminatePercent()
    {
        var snapshot = Snapshot(importing: true);
        Assert.True(snapshot.Active);
        Assert.Equal("导入实例", snapshot.Text);
        Assert.Equal("…", snapshot.PercentText);
    }

    [Fact]
    public void LaunchWithZeroProgressStaysIdle()
    {
        var snapshot = Snapshot(launchBusy: true, progress: 0);
        Assert.False(snapshot.Active);
    }
}
