using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core.Jobs;
using WMCL.Minecraft;

namespace WMCL.App.ViewModels;

public partial class GameManageViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private CancellationTokenSource? _cts;

    public GameManageViewModel(AppServices services)
    {
        _services = services;
    }

    public ObservableCollection<VersionItem> Versions { get; } = [];
    public string GameDirectory => _services.Settings.GameDirectory;
    public bool HasVersions => Versions.Count > 0;

    [ObservableProperty]
    private VersionItem? _selectedVersion;

    [ObservableProperty]
    private string _status = "选择已安装版本进行修复或删除";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private bool _canRepair;

    [ObservableProperty]
    private bool _canDelete;

    partial void OnSelectedVersionChanged(VersionItem? value) => UpdateCommands();
    partial void OnIsBusyChanged(bool value) => UpdateCommands();

    private void UpdateCommands()
    {
        CanRepair = !IsBusy && SelectedVersion is not null;
        CanDelete = !IsBusy && SelectedVersion is not null;
    }

    public async Task LoadAsync()
    {
        var current = SelectedVersion?.Id;
        var installed = await Task.Run(() => _services.Repository.ListInstalled());
        Versions.Clear();
        foreach (var version in installed.OrderByDescending(v => v.ReleaseTime ?? ""))
        {
            Versions.Add(new VersionItem
            {
                Id = version.Id,
                Type = version.Type ?? "release",
                ReleaseTime = version.ReleaseTime,
                IsInstalled = true
            });
        }

        SelectedVersion = Versions.FirstOrDefault(v => v.Id == current)
                          ?? Versions.FirstOrDefault();
        OnPropertyChanged(nameof(HasVersions));
        Status = Versions.Count == 0
            ? "还没有已安装版本"
            : $"共 {Versions.Count} 个已安装版本";
        UpdateCommands();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(GameDirectory);
        OpenPath(GameDirectory);
    }

    [RelayCommand]
    private void OpenVersionFolder()
    {
        if (SelectedVersion is null) return;
        var path = Path.Combine(GameDirectory, "versions", SelectedVersion.Id);
        Directory.CreateDirectory(path);
        OpenPath(path);
    }

    [RelayCommand]
    private async Task RepairAsync()
    {
        if (!CanRepair || SelectedVersion is null) return;
        var target = SelectedVersion;
        var root = GameDirectory;
        IsBusy = true;
        Progress = 0;
        ProgressText = "";
        Status = $"正在校验/修复 {target.Id}…";
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                ProgressText = update.Stage + (string.IsNullOrWhiteSpace(update.Detail)
                    ? ""
                    : " · " + update.Detail);
            });
            using var operation = _services.GameOperations.Acquire(root, "修复游戏 " + target.Id);
            var result = await new VersionInstaller(new VersionRepository(root)).InstallAsync(
                target.Id,
                new InstallOptions
                {
                    Mirror = _services.Mirror,
                    MaxConcurrency = Math.Clamp(_services.Settings.DownloadThreads, 1, 64),
                    VerifyExisting = true
                },
                progress,
                _cts.Token);
            Status = $"修复完成：{target.Id}，{result.Files} 个文件";
            Progress = 100;
        }
        catch (OperationCanceledException)
        {
            Status = "已取消修复";
        }
        catch (Exception ex)
        {
            Status = "修复失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
            ProgressText = "";
            _cts.Dispose();
            _cts = null;
            UpdateCommands();
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!CanDelete || SelectedVersion is null) return;
        var id = SelectedVersion.Id;
        var root = GameDirectory;
        if (!await DialogService.ConfirmAsync(
                "删除实例",
                $"确定删除 {id}？" + Environment.NewLine + "此操作不可恢复。"))
            return;

        try
        {
            using var operation = _services.GameOperations.Acquire(root, "删除实例 " + id);
            InstanceManager.Delete(root, id);
            await LoadAsync();
            Status = $"已删除 {id}";
        }
        catch (Exception ex)
        {
            Status = "删除失败：" + ex.Message;
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            ExternalLauncher.Open(path);
        }
        catch
        {
            // 打开失败不阻塞页面
        }
    }
}
