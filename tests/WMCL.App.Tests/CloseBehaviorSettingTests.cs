using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class CloseBehaviorSettingTests
{
    [Fact]
    public void DefaultsToHideToTray()
    {
        Assert.Equal("hide", new AppSettings().CloseBehavior);
        Assert.Equal("隐藏到托盘", SettingsViewModel.ToCloseBehaviorLabel("hide"));
        Assert.Equal("退出程序", SettingsViewModel.ToCloseBehaviorLabel("exit"));
        Assert.Equal("hide", SettingsViewModel.ToCloseBehaviorValue("显示启动器"));
        Assert.Equal("exit", SettingsViewModel.ToCloseBehaviorValue("退出程序"));
    }
}
