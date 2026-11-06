using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class AfterLaunchSettingTests
{
    private static SettingsViewModel Create(out AppServices services)
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "after-launch-" + Guid.NewGuid().ToString("N"));
        services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        return new SettingsViewModel(services);
    }

    [AvaloniaFact]
    public void DefaultsToKeepOpen()
    {
        var model = Create(out var services);
        Assert.Equal("保持打开", model.AfterLaunch);
        // 未迁移的旧配置为 null，保存后落为 keep
        model.SaveCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("keep", services.Settings.LauncherAfterLaunch);
    }

    [AvaloniaTheory]
    [InlineData("保持打开", "keep")]
    [InlineData("隐藏到后台", "hide")]
    [InlineData("关闭启动器", "close")]
    public void SelectedOptionPersistsAsValue(string label, string value)
    {
        var model = Create(out var services);
        model.AfterLaunch = label;
        model.SaveCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(value, services.Settings.LauncherAfterLaunch);
    }

    [Theory]
    [InlineData("keep", "保持打开")]
    [InlineData("hide", "隐藏到后台")]
    [InlineData("close", "关闭启动器")]
    [InlineData("unknown", "保持打开")]
    public void ValueMapsBackToLabel(string value, string label)
    {
        Assert.Equal(label, SettingsViewModel.ToAfterLaunchLabel(value));
    }

    [Fact]
    public void OptionsExposeThreeStates()
    {
        var model = Create(out _);
        Assert.Equal(["保持打开", "隐藏到后台", "关闭启动器"], model.AfterLaunchOptions);
    }
}

public sealed class AfterLaunchMigrationTests
{
    [Fact]
    public void LegacyHideFlagMigratesToHide()
    {
        var settings = new AppSettings { HideLauncherOnGame = true };
        SettingsService.ApplyLegacyMigrations(settings);
        Assert.Equal("hide", settings.LauncherAfterLaunch);
    }

    [Fact]
    public void LegacyOffMigratesToKeep()
    {
        var settings = new AppSettings { HideLauncherOnGame = false };
        SettingsService.ApplyLegacyMigrations(settings);
        Assert.Equal("keep", settings.LauncherAfterLaunch);
    }

    [Fact]
    public void ExplicitValueIsNotOverwritten()
    {
        var settings = new AppSettings { HideLauncherOnGame = true, LauncherAfterLaunch = "close" };
        SettingsService.ApplyLegacyMigrations(settings);
        Assert.Equal("close", settings.LauncherAfterLaunch);
    }
}
