using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;

namespace WMCL.App.ViewModels;

/// <summary>PCL2 PageSelect：左侧版本目录，右侧已安装版本列表；单击版本后返回启动页。</summary>
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
        Versions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasVersions));
            OnPropertyChanged(nameof(CountText));
            RefreshSelection();
        };
    }

    public ObservableCollection<VersionItem> Versions => _launch.InstalledVersions;
    public bool HasVersions => Versions.Count > 0;
    public string CountText => $"已安装 {Versions.Count} 个版本";
    public string VersionsDirectory => Path.Combine(_services.Settings.GameDirectory, "versions");

    [ObservableProperty]
    private VersionItem? _selectedVersion;

    public void RefreshSelection()
    {
        if (SelectedVersion is null || !Versions.Contains(SelectedVersion))
            SelectedVersion = _launch.SelectedVersion is { IsInstalled: true } selected
                ? selected
                : Versions.FirstOrDefault();
    }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedVersion is null) return;
        _launch.SelectedVersion = SelectedVersion;
        _close();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(VersionsDirectory);
            var start = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? VersionsDirectory : "xdg-open",
                Arguments = OperatingSystem.IsWindows() ? "" : VersionsDirectory,
                UseShellExecute = OperatingSystem.IsWindows()
            };
            Process.Start(start);
        }
        catch
        {
            // 打开失败不改变选择状态
        }
    }

    [RelayCommand]
    private void Download()
    {
        _close();
        _download();
    }
}
