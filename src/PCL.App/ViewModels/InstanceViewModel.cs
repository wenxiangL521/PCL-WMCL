using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core;

namespace PCL.App.ViewModels;

public partial class InstanceViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public InstanceViewModel(AppServices services)
    {
        _services = services;
    }

    public ObservableCollection<VersionItem> Instances { get; } = [];
    public string GameDirectory => _services.Settings.GameDirectory;
    public bool HasInstances => Instances.Count > 0;
    public string SelectedInstanceTitle => SelectedInstance?.Id ?? "未选择实例";
    public string SelectedInstanceInfo => SelectedInstance is null
        ? "从左侧选择一个实例"
        : $"{SelectedInstance.TypeText} · {SelectedInstance.ReleaseTime ?? "未知时间"}";
    public string SelectedInstancePath => SelectedInstance is null
        ? GameDirectory
        : Path.Combine(GameDirectory, "versions", SelectedInstance.Id);

    [ObservableProperty]
    private VersionItem? _selectedInstance;

    [ObservableProperty]
    private string _status = "就绪";

    partial void OnSelectedInstanceChanged(VersionItem? value)
    {
        OnPropertyChanged(nameof(SelectedInstanceTitle));
        OnPropertyChanged(nameof(SelectedInstanceInfo));
        OnPropertyChanged(nameof(SelectedInstancePath));
    }

    public async Task LoadAsync()
    {
        Status = "正在扫描实例…";
        try
        {
            var installed = await Task.Run(() => _services.Repository.ListInstalled());
            Instances.Clear();
            foreach (var version in installed)
            {
                Instances.Add(new VersionItem
                {
                    Id = version.Id,
                    Type = version.Type ?? "release",
                    ReleaseTime = version.ReleaseTime,
                    IsInstalled = true
                });
            }

            SelectedInstance = Instances.FirstOrDefault();
            Status = Instances.Count == 0 ? "还没有安装任何版本" : $"共 {Instances.Count} 个实例";
        }
        catch (Exception ex)
        {
            Status = "扫描失败：" + ex.Message;
        }
        finally
        {
            OnPropertyChanged(nameof(HasInstances));
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private void OpenFolder()
    {
        var path = SelectedInstance is null
            ? GameDirectory
            : Path.Combine(GameDirectory, "versions", SelectedInstance.Id);
        OpenPath(Directory.Exists(path) ? path : GameDirectory);
    }

    [RelayCommand]
    private void OpenGameFolder() => OpenPath(GameDirectory);

    private static void OpenPath(string path)
    {
        try
        {
            ProcessStartInfo startInfo;
            if (OperatingSystem.IsWindows())
                startInfo = new ProcessStartInfo("explorer.exe", path);
            else if (OperatingSystem.IsMacOS())
                startInfo = new ProcessStartInfo("open", path);
            else
                startInfo = new ProcessStartInfo("xdg-open", path);

            startInfo.UseShellExecute = false;
            Process.Start(startInfo);
        }
        catch
        {
            // 打开目录失败不阻塞 UI
        }
    }
}
