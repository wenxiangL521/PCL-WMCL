using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.Modding.OptiFine;

namespace WMCL.App.ViewModels;

public partial class OptiFabricSelectionViewModel : ViewModelBase
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<OptiFabricFile>>> _load;
    private string? _minecraft;
    private Task? _loading;
    private CancellationTokenSource? _cancellation;

    public OptiFabricSelectionViewModel(OptiFabricService service) : this(service.GetFilesAsync) { }

    internal OptiFabricSelectionViewModel(Func<string, CancellationToken, Task<IReadOnlyList<OptiFabricFile>>> load) => _load = load;

    public ObservableCollection<OptiFabricFile> Versions { get; } = [];

    [ObservableProperty]
    private OptiFabricFile? _selectedVersion;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _status = "选择 Minecraft 版本后获取 OptiFabric";

    public Task PrepareAsync(string minecraft, bool forceReload = false)
    {
        if (!forceReload && _minecraft == minecraft && _loading is not null) return _loading;
        _minecraft = minecraft;
        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();
        return _loading = LoadAsync(minecraft, _cancellation);
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        if (_minecraft is not { } minecraft) return;
        _loading = null;
        await PrepareAsync(minecraft);
    }

    private async Task LoadAsync(string minecraft, CancellationTokenSource cancellation)
    {
        Versions.Clear();
        SelectedVersion = null;
        HasError = false;
        IsBusy = true;
        Status = $"正在获取 OptiFabric {minecraft} 版本…";
        try
        {
            var versions = await _load(minecraft, cancellation.Token);
            if (_cancellation != cancellation) return;
            foreach (var version in versions) Versions.Add(version);
            SelectedVersion = Versions.FirstOrDefault();
            Status = OptiFabricService.IsOriginsVersion(minecraft)
                ? "Minecraft 1.14 / 1.15 需手动下载 OptiFabric Origins"
                : Versions.Count == 0 ? $"没有支持 Minecraft {minecraft} 的 OptiFabric 版本"
                : $"共 {Versions.Count} 个 OptiFabric 版本，来自 CurseForge";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (_cancellation != cancellation) return;
            HasError = true;
            Status = "获取 OptiFabric 失败：" + exception.Message;
        }
        finally
        {
            if (_cancellation == cancellation)
            {
                IsBusy = false;
                _cancellation = null;
            }
            cancellation.Dispose();
        }
    }
}
