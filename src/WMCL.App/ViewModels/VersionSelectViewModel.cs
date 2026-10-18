using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Minecraft;

namespace WMCL.App.ViewModels;

/// <summary>PCL2 PageSelect 的简化版：左侧已安装版本，右侧版本详情与操作。</summary>
public partial class VersionSelectViewModel : ViewModelBase
{
    private readonly LaunchViewModel _launch;
    private readonly AppServices _services;
    private readonly Action _close;
    private readonly Action _download;

    public VersionSelectViewModel(
        LaunchViewModel launch,
        AppServices services,
        Action close,
        Action download)
    {
        _launch = launch;
        _services = services;
        _close = close;
        _download = download;
        SelectedVersion = launch.SelectedVersion is { IsInstalled: true } selected
            ? selected
            : Versions.FirstOrDefault();
        Versions.CollectionChanged += (_, _) => RefreshSelection();
        _ = LoadDetailsAsync(SelectedVersion);
    }

    [ObservableProperty]
    private string _versionLoader = "";

    [ObservableProperty]
    private string _versionPath = "";

    [ObservableProperty]
    private string _versionJava = "";

    public void RefreshSelection()
    {
        OnPropertyChanged(nameof(HasVersions));
        if (SelectedVersion is null || !Versions.Contains(SelectedVersion))
            SelectedVersion = _launch.SelectedVersion is { IsInstalled: true } selected
                ? selected
                : Versions.FirstOrDefault();
        _ = LoadDetailsAsync(SelectedVersion);
    }

    private async Task LoadDetailsAsync(VersionItem? version)
    {
        if (version is null)
        {
            VersionLoader = "";
            VersionPath = "";
            VersionJava = "";
            return;
        }
        VersionPath = Path.Combine(
            _services.Settings.GameDirectory, "versions", version.Id);
        try
        {
            var json = await _services.Repository.GetVersionAsync(version.Id);
            VersionLoader = MinecraftVersionUtil.GetLoader(json) switch
            {
                "fabric" => "Fabric",
                "forge" => "Forge",
                "neoforge" => "NeoForge",
                "quilt" => "Quilt",
                "optifine" => "OptiFine",
                _ => "原版"
            };
            VersionJava = json.JavaVersion?.MajorVersion is { } major
                ? $"Java {major}"
                : "自动选择 Java";
        }
        catch
        {
            VersionLoader = "未知";
            VersionJava = "未知";
        }
    }

    public ObservableCollection<VersionItem> Versions => _launch.InstalledVersions;
    public bool HasVersions => Versions.Count > 0;

    [ObservableProperty]
    private VersionItem? _selectedVersion;

    public string SelectedVersionTitle => SelectedVersion?.Id ?? "未选择版本";
    public string SelectedVersionInfo => SelectedVersion is null
        ? "请选择一个已安装版本"
        : SelectedVersion.Subtitle;

    partial void OnSelectedVersionChanged(VersionItem? value)
    {
        OnPropertyChanged(nameof(SelectedVersionTitle));
        OnPropertyChanged(nameof(SelectedVersionInfo));
        _ = LoadDetailsAsync(value);
    }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedVersion is null) return;
        _launch.SelectedVersion = SelectedVersion;
        _close();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        if (SelectedVersion is null) return;
        _launch.SelectedVersion = SelectedVersion;
        _close();
        _launch.RequestVersionSettings();
    }

    [RelayCommand]
    private void Download()
    {
        _close();
        _download();
    }

    [RelayCommand]
    private void Back() => _close();
}
