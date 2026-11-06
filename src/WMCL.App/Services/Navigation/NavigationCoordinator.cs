using WMCL.App.Features.Downloads;
using WMCL.App.Features.Launch;
using WMCL.App.Features.Link;
using WMCL.App.Features.Mods;
using WMCL.App.Features.Settings;

namespace WMCL.App.Services.Navigation;

/// <summary>
/// 页面之间的「跳转类」联动：某个页面发出“去下载/去联机设置”等请求时统一在这里转成导航调用，
/// 不再写进 <c>MainWindowViewModel</c> 的构造函数。
/// </summary>
public sealed class NavigationCoordinator
{
    private readonly LaunchViewModel _launch;
    private readonly ModsViewModel _mods;
    private readonly LinkViewModel _link;
    private readonly SettingsViewModel _settings;
    private readonly Action<string> _navigate;
    private readonly Action _openVersionSelect;

    public NavigationCoordinator(
        LaunchViewModel launch,
        ModsViewModel mods,
        LinkViewModel link,
        SettingsViewModel settings,
        Action<string> navigate,
        Action openVersionSelect)
    {
        _launch = launch;
        _mods = mods;
        _link = link;
        _settings = settings;
        _navigate = navigate;
        _openVersionSelect = openVersionSelect;
        Wire();
    }

    private void Wire()
    {
        _launch.DownloadRequested += (_, _) => _navigate("download");
        _mods.DownloadRequested += (_, _) => _navigate("mods");
        _mods.VersionSelectRequested += (_, _) => _openVersionSelect();
        _link.SettingsRequested += (_, _) => _navigate("settings-link");
        _settings.JavaManagementRequested += (_, _) => _navigate("settings-java");
    }
}
