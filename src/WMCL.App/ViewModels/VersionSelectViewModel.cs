using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WMCL.App.ViewModels;

/// <summary>PCL2 PageSelect 的简化版：左侧已安装版本，右侧版本详情与操作。</summary>
public partial class VersionSelectViewModel : ViewModelBase
{
    private readonly LaunchViewModel _launch;
    private readonly Action _close;
    private readonly Action _download;

    public VersionSelectViewModel(
        LaunchViewModel launch,
        Action close,
        Action download)
    {
        _launch = launch;
        _close = close;
        _download = download;
        SelectedVersion = launch.SelectedVersion is { IsInstalled: true } selected
            ? selected
            : Versions.FirstOrDefault();
        Versions.CollectionChanged += (_, _) => RefreshSelection();
    }

    public void RefreshSelection()
    {
        OnPropertyChanged(nameof(HasVersions));
        if (SelectedVersion is null || !Versions.Contains(SelectedVersion))
            SelectedVersion = _launch.SelectedVersion is { IsInstalled: true } selected
                ? selected
                : Versions.FirstOrDefault();
    }

    public ObservableCollection<VersionItem> Versions => _launch.InstalledVersions;
    public bool HasVersions => Versions.Count > 0;

    [ObservableProperty]
    private VersionItem? _selectedVersion;

    public string SelectedVersionTitle => SelectedVersion?.Id ?? "未选择版本";
    public string SelectedVersionInfo => SelectedVersion is null
        ? "请选择一个已安装版本"
        : $"{SelectedVersion.TypeText} · {SelectedVersion.ReleaseTime ?? "未知时间"}";

    partial void OnSelectedVersionChanged(VersionItem? value)
    {
        OnPropertyChanged(nameof(SelectedVersionTitle));
        OnPropertyChanged(nameof(SelectedVersionInfo));
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
