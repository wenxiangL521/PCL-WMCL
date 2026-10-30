using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Core.Localization;

namespace WMCL.App.Tests;

public sealed class WelcomeViewModelTests
{
    [AvaloniaFact]
    public void NextCommand_AdvancesStepUntilLast()
    {
        var vm = new WelcomeViewModel();
        Assert.Equal(0, vm.Step);

        vm.NextCommand.Execute(null);
        Assert.Equal(1, vm.Step);

        vm.NextCommand.Execute(null);
        Assert.Equal(2, vm.Step);
    }

    [AvaloniaFact]
    public void BackCommand_DecrementsStep()
    {
        var vm = new WelcomeViewModel();
        vm.NextCommand.Execute(null);
        Assert.Equal(1, vm.Step);

        vm.BackCommand.Execute(null);
        Assert.Equal(0, vm.Step);
    }

    [AvaloniaFact]
    public void SkipCommand_TriggersOnCompleted()
    {
        var vm = new WelcomeViewModel();
        var completed = false;
        vm.OnCompleted = () => completed = true;

        vm.SkipCommand.Execute(null);
        Assert.True(completed);
    }

    [AvaloniaFact]
    public void LastNextCommand_TriggersOnCompleted()
    {
        var vm = new WelcomeViewModel();
        var completed = false;
        vm.OnCompleted = () => completed = true;

        vm.NextCommand.Execute(null); // step 1
        vm.NextCommand.Execute(null); // step 2
        vm.NextCommand.Execute(null); // completes
        Assert.True(completed);
    }

    [AvaloniaFact]
    public void CanGoBack_IsTrueWhenStepGtZero()
    {
        var vm = new WelcomeViewModel();
        Assert.False(vm.CanGoBack);
        vm.NextCommand.Execute(null);
        Assert.True(vm.CanGoBack);
    }

    [AvaloniaFact]
    public void Constructor_SetsStepTexts()
    {
        // Localizer 未加载 locale 时 T() 返回键名本身，"非空"断言恒真。
        // 改为断言与实际文案加载行为一致的具体值：缺省（未加载）状态下
        // 显示键名；加载 zh-CN 后显示翻译文本。
        var vm = new WelcomeViewModel();
        Assert.Equal(Localizer.T("welcome.step.1.title"), vm.StepTitle);
        Assert.Equal(Localizer.T("welcome.step.1.description"), vm.StepDescription);
        // welcome.progress 未加载时回退到 T 的 fallback（硬编码中文兜底）。
        Assert.Equal("第 1 / 3 步", vm.StepProgress);
    }

    [AvaloniaFact]
    public void Constructor_LoadsTranslatedTexts_WhenLocaleInstalled()
    {
        // 用临时 locale 目录走一遍完整加载链（模拟 LocaleInstaller 产物），
        // 断言翻译确实生效而非恒真。
        var dir = Path.Combine(Path.GetTempPath(), "wmcl-welcome-locale-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "zh-CN.yaml"),
                """
                welcome.step.1.title: 测试标题
                welcome.step.1.description: 测试描述
                welcome.progress: 第 {0} / {1} 步
                """);
            Localizer.LoadFromDirectory(dir);
            Localizer.SetLocale("zh-CN");

            var vm = new WelcomeViewModel();
            Assert.Equal("测试标题", vm.StepTitle);
            Assert.Equal("测试描述", vm.StepDescription);
            // 占位符被正确填充。
            Assert.Equal("第 1 / 3 步", vm.StepProgress);

            vm.NextCommand.Execute(null);
            Assert.Equal("第 2 / 3 步", vm.StepProgress);
        }
        finally
        {
            Localizer.LoadFromDirectory(Path.Combine(Path.GetTempPath(), "nonexistent-wmcl-locale"));
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [AvaloniaFact]
    public void Settings_HaveWelcomeCompletedField()
    {
        var settings = new AppSettings();
        Assert.False(settings.WelcomeCompleted);
        Assert.Equal("zh-CN", settings.Locale);
    }
}
