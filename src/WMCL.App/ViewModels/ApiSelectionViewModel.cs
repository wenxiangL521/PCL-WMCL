using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.Modding.Fabric;
using WMCL.Modding.Modrinth;

namespace WMCL.App.ViewModels;

public partial class ApiSelectionViewModel : ViewModelBase
{
    private readonly Func<string, string, CancellationToken, Task<IReadOnlyList<ModrinthVersion>>> _load;
    private (string Minecraft, string Kind)? _key;
    private Task? _loading;
    private CancellationTokenSource? _cancellation;

    public ApiSelectionViewModel(ApiComponentService service) : this(service.GetVersionsAsync) { }

    internal ApiSelectionViewModel(Func<string, string, CancellationToken, Task<IReadOnlyList<ModrinthVersion>>> load) => _load = load;

    public ObservableCollection<ModrinthVersion> Versions { get; } = [];

    [ObservableProperty]
    private ModrinthVersion? _selectedVersion;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _status = "选择加载器后获取 API 版本";

    public Task PrepareAsync(string minecraft, string kind, bool forceReload = false)
    {
        var key = (minecraft, kind);
        if (!forceReload && _key == key && _loading is not null) return _loading;
        _key = key;
        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();
        return _loading = LoadAsync(minecraft, kind, _cancellation);
    }

    public void Clear()
    {
        _key = null;
        _loading = null;
        _cancellation?.Cancel();
        _cancellation = null;
        Versions.Clear();
        SelectedVersion = null;
        HasError = false;
        IsBusy = false;
        Status = "选择加载器后获取 API 版本";
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        if (_key is not { } key) return;
        _loading = null;
        await PrepareAsync(key.Minecraft, key.Kind);
    }

    private async Task LoadAsync(string minecraft, string kind, CancellationTokenSource cancellation)
    {
        Versions.Clear();
        SelectedVersion = null;
        HasError = false;
        IsBusy = true;
        var name = ApiComponentService.Name(kind);
        Status = $"正在获取 {name} {minecraft} 版本…";
        try
        {
            var versions = await _load(minecraft, kind, cancellation.Token);
            if (_cancellation != cancellation) return;
            foreach (var version in versions) Versions.Add(version);
            SelectedVersion = Versions.FirstOrDefault();
            Status = Versions.Count == 0 ? $"没有支持 Minecraft {minecraft} 的 {name} 版本"
                : $"共 {Versions.Count} 个 {name} 版本，来自 Modrinth";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (_cancellation != cancellation) return;
            HasError = true;
            Status = $"获取 {name} 失败：" + exception.Message;
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
