using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class FeedbackViewModelTests
{
    private static FeedbackViewModel CreateVm()
    {
        var settings = new AppSettings();
        var services = new AppServices(settings);
        return new FeedbackViewModel(services);
    }

    [AvaloniaFact]
    public void Constructor_GeneratesDiagnostics()
    {
        var vm = CreateVm();
        Assert.False(string.IsNullOrEmpty(vm.Diagnostics));
        Assert.Contains("PCL WMCL", vm.Diagnostics);
        Assert.Contains("操作系统", vm.Diagnostics);
        Assert.Contains("游戏目录", vm.Diagnostics);
    }

    [AvaloniaFact]
    public void Diagnostics_ContainsVersionAndPlatform()
    {
        var vm = CreateVm();
        Assert.Contains("版本", vm.Diagnostics);
        Assert.Contains("架构", vm.Diagnostics);
    }

    [AvaloniaFact]
    public void Diagnostics_ContainsLogFiles()
    {
        var vm = CreateVm();
        Assert.Contains("日志文件", vm.Diagnostics);
    }

    [AvaloniaFact]
    public void ReportUrl_PointsToGitHub()
    {
        var vm = CreateVm();
        Assert.Contains("github.com", vm.ReportUrl);
    }

    [AvaloniaFact]
    public void GenerateDiagnosticsCommand_RefreshesDiagnostics()
    {
        var vm = CreateVm();
        vm.Diagnostics = "";
        vm.GenerateDiagnosticsCommand.Execute(null);
        Assert.False(string.IsNullOrEmpty(vm.Diagnostics));
        Assert.Equal("诊断信息已生成", vm.Status);
    }
}
