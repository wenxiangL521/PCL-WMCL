using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

// MainWindowViewModel 构造链会读取真实数据目录（task-history.json、
// launch_count.txt），用 XDG_DATA_HOME 隔离到临时目录。
[Trait("Kind", "Headless")]
public sealed class KeyboardShortcutTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _oldXdgData;

    public KeyboardShortcutTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "wmcl-shortcut-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        _oldXdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _dataDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _oldXdgData);
        try { Directory.Delete(_dataDir, true); } catch { }
    }

    [AvaloniaFact]
    public void CycleTopTab_ForwardWrapsAround()
    {
        var settings = new AppSettings();
        var vm = new MainWindowViewModel(settings);
        Assert.Equal(5, vm.TopTabs.Count);

        vm.CycleTopTab(false); // 0 -> 1 (download)
        Assert.Equal(vm.TopTabs[1], vm.SelectedTopTab);

        vm.CycleTopTab(false); // 1 -> 2 (link)
        Assert.Equal(vm.TopTabs[2], vm.SelectedTopTab);
    }

    [AvaloniaFact]
    public void CycleTopTab_ForwardWrapFromLastToFirst()
    {
        // 名副其实的“环绕”：最后一项前进应回到第一项。
        var vm = new MainWindowViewModel(new AppSettings());
        vm.SelectedTopTab = vm.TopTabs[^1];

        vm.CycleTopTab(false); // 4 -> 0
        Assert.Equal(vm.TopTabs[0], vm.SelectedTopTab);
    }

    [AvaloniaFact]
    public void CycleTopTab_BackwardWrapsAround()
    {
        var vm = new MainWindowViewModel(new AppSettings());
        vm.SelectedTopTab = vm.TopTabs[2];

        vm.CycleTopTab(true); // 2 -> 1
        Assert.Equal(vm.TopTabs[1], vm.SelectedTopTab);
    }

    [AvaloniaFact]
    public void CycleTopTab_BackwardWrapFromFirstToLast()
    {
        // 环绕的另一侧：第一项后退应到最后一项。
        var vm = new MainWindowViewModel(new AppSettings());
        vm.SelectedTopTab = vm.TopTabs[0];

        vm.CycleTopTab(true); // 0 -> 4
        Assert.Equal(vm.TopTabs[^1], vm.SelectedTopTab);
    }

    [AvaloniaFact]
    public void NavigateTo_IsCallableFromView()
    {
        // NavigateTo 现在是 internal，视图层可以调用
        var settings = new AppSettings();
        var vm = new MainWindowViewModel(settings);
        vm.NavigateTo("download");
        Assert.Equal(vm.TopTabs[1], vm.SelectedTopTab);
    }
}
