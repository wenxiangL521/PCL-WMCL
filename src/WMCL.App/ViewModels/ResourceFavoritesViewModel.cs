using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;

namespace WMCL.App.ViewModels;

public partial class ResourceFavoritesViewModel : ViewModelBase
{
    private readonly ResourceFavoritesService _favorites;
    private readonly Func<FavoriteResource, Task> _open;
    private readonly Func<string, string, Task<bool>> _confirm;
    private readonly Func<string, CancellationToken, Task<FavoriteResource?>>? _lookup;
    private bool _refreshing;

    public ResourceFavoritesViewModel(ResourceFavoritesService favorites, Func<FavoriteResource, Task> open,
        Func<string, string, Task<bool>>? confirm = null, ResourceFavoriteBatchViewModel? batch = null,
        Func<string, CancellationToken, Task<FavoriteResource?>>? lookup = null)
    {
        _favorites = favorites;
        _open = open;
        _confirm = confirm ?? ((title, message) => DialogService.ConfirmAsync(title, message));
        _lookup = lookup;
        Batch = batch;
        if (batch is not null)
            batch.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(ResourceFavoriteBatchViewModel.IsOpen)) return;
                OnPropertyChanged(nameof(IsBatchOpen));
                OnPropertyChanged(nameof(ShowResourceList));
                OnPropertyChanged(nameof(ShowSelectionActions));
            };
        favorites.Changed += (_, _) => RefreshFolders();
        RefreshFolders();
    }

    public ObservableCollection<ResourceFavoriteFolder> Folders { get; } = [];
    public ResourceFavoriteBatchViewModel? Batch { get; }
    public bool HasBatch => Batch is not null;
    public bool IsBatchOpen => Batch?.IsOpen == true;
    public bool ShowResourceList => !IsManaging && !IsBatchOpen;
    public ObservableCollection<FavoriteResourceRow> Rows { get; } = [];
    public IReadOnlyList<string> Types { get; } = ["全部类型", "Mod", "整合包", "数据包", "资源包", "光影", "存档", "插件"];

    [ObservableProperty]
    private ResourceFavoriteFolder? _selectedFolder;

    [ObservableProperty]
    private ResourceFavoriteFolder? _destinationFolder;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _selectedType = "全部类型";

    [ObservableProperty]
    private bool _isManaging;

    [ObservableProperty]
    private string _folderName = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _isOpening;

    public bool HasRows => Rows.Count > 0;
    public int SelectedCount => Rows.Count(row => row.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public bool CanTransfer => HasSelection && DestinationFolder is { } destination && destination.Id != SelectedFolder?.Id;
    public bool CanShare => HasSelection;
    public bool CanDeleteFolder => Folders.Count > 1;
    public bool ShowSelectionActions => HasSelection && !IsManaging && !IsBatchOpen;
    public string ManageText => IsManaging ? "完成管理" : "管理";
    public string CountText => $"{Rows.Count} 个项目 · 已选 {SelectedCount} 项";
    public string EmptyTitle => SelectedFolder?.Resources.Count > 0 ? "没有匹配的收藏" : "还没有收藏资源";
    public string EmptyDescription => SelectedFolder?.Resources.Count > 0
        ? "试试其他关键词或资源类型。"
        : "在 Mod、整合包或资源下载页点击「收藏」，即可在这里再次选择版本并安装。";

    partial void OnSelectedFolderChanged(ResourceFavoriteFolder? value)
    {
        if (_refreshing) return;
        FolderName = value?.Name ?? "";
        RebuildRows();
    }

    partial void OnSearchTextChanged(string value) => RebuildRows();
    partial void OnSelectedTypeChanged(string value) => RebuildRows();
    partial void OnDestinationFolderChanged(ResourceFavoriteFolder? value) => OnPropertyChanged(nameof(CanTransfer));

    partial void OnIsManagingChanged(bool value)
    {
        OnPropertyChanged(nameof(ManageText));
        OnPropertyChanged(nameof(ShowSelectionActions));
        OnPropertyChanged(nameof(ShowResourceList));
    }

    private void RefreshFolders()
    {
        var selectedId = SelectedFolder?.Id;
        var destinationId = DestinationFolder?.Id;
        _refreshing = true;
        try
        {
            Folders.Clear();
            foreach (var folder in _favorites.Folders) Folders.Add(folder);
            SelectedFolder = Folders.FirstOrDefault(folder => folder.Id == selectedId) ?? Folders[0];
            DestinationFolder = Folders.FirstOrDefault(folder => folder.Id == destinationId && folder.Id != SelectedFolder.Id)
                                ?? Folders.FirstOrDefault(folder => folder.Id != SelectedFolder.Id);
            FolderName = SelectedFolder.Name;
        }
        finally { _refreshing = false; }
        OnPropertyChanged(nameof(CanDeleteFolder));
        RebuildRows();
    }

    private void RebuildRows()
    {
        if (_refreshing) return;
        Rows.Clear();
        var query = SearchText.Trim();
        foreach (var resource in SelectedFolder?.Resources ?? [])
        {
            if (SelectedType != "全部类型" && SelectedType != resource.TypeLabel) continue;
            if (query.Length > 0 && !resource.Title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !resource.Description.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !resource.Detail.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !resource.Slug.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            var row = new FavoriteResourceRow(resource);
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(FavoriteResourceRow.IsSelected)) RefreshSelection();
            };
            Rows.Add(row);
        }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        Batch?.SetSelection(Rows.Where(row => row.IsSelected).Select(row => row.Resource).ToArray());
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowSelectionActions));
        OnPropertyChanged(nameof(CanTransfer));
        OnPropertyChanged(nameof(CanShare));
        OnPropertyChanged(nameof(CountText));
    }

    public string CreateShareCode() =>
        ResourceFavoritesService.CreateShareCode(Rows.Where(row => row.IsSelected).Select(row => row.Resource));

    public string CreateNativeShareCode() =>
        ResourceFavoritesService.CreateNativeShareCode(
            Rows.Where(row => row.IsSelected).Select(row => row.Resource));

    public int ImportShareCode(string code)
    {
        if (SelectedFolder is null) return 0;
        var resources = ResourceFavoritesService.ParseShareCode(code);
        var unresolved = 0;
        if (resources.Count == 0)
        {
            var ids = ResourceFavoritesService.ParseNativeShareCode(code);
            if (ids.Count == 0)
            {
                Status = "分享码无效或没有可导入的收藏";
                return 0;
            }
            var known = Folders.SelectMany(folder => folder.Resources)
                .DistinctBy(resource => resource.Key)
                .ToArray();
            resources = ids.Select(id => known.FirstOrDefault(resource =>
                    resource.ProjectId.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                    resource.Key.Equals(id, StringComparison.OrdinalIgnoreCase)))
                .Where(resource => resource is not null)
                .Select(resource => resource!)
                .DistinctBy(resource => resource.Key)
                .ToArray();
            unresolved = ids.Count - resources.Count;
            if (resources.Count == 0)
            {
                Status = $"{unresolved} 个项目 ID 未在本机收藏中找到，未导入";
                return 0;
            }
        }
        return CommitImport(resources, unresolved);
    }

    public async Task<int> ImportShareCodeAsync(string code, CancellationToken ct = default)
    {
        if (SelectedFolder is null) return 0;
        var resources = ResourceFavoritesService.ParseShareCode(code);
        var unresolved = 0;
        if (resources.Count == 0)
        {
            var ids = ResourceFavoritesService.ParseNativeShareCode(code);
            if (ids.Count == 0)
            {
                Status = "分享码无效或没有可导入的收藏";
                return 0;
            }
            var known = Folders.SelectMany(folder => folder.Resources)
                .DistinctBy(resource => resource.Key)
                .ToArray();
            var resolved = new List<FavoriteResource>();
            foreach (var id in ids)
            {
                var local = known.FirstOrDefault(resource =>
                    resource.ProjectId.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                    resource.Key.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (local is not null)
                {
                    resolved.Add(local);
                    continue;
                }
                if (_lookup is null)
                {
                    unresolved++;
                    continue;
                }
                try
                {
                    var remote = await _lookup(id, ct).ConfigureAwait(true);
                    if (remote is not null) resolved.Add(remote);
                    else unresolved++;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    unresolved++;
                }
            }
            resources = resolved.DistinctBy(resource => resource.Key).ToArray();
            if (resources.Count == 0)
            {
                Status = $"{unresolved} 个项目 ID 无法解析，未导入";
                return 0;
            }
        }
        return CommitImport(resources, unresolved);
    }

    private int CommitImport(IReadOnlyList<FavoriteResource> resources, int unresolved)
    {
        if (SelectedFolder is null) return 0;
        foreach (var resource in resources)
            _favorites.SetFavorite(SelectedFolder.Id, resource, true);
        RefreshFolders();
        Status = unresolved == 0
            ? $"已导入 {resources.Count} 个收藏到 {SelectedFolder.Name}"
            : $"已导入 {resources.Count} 个收藏到 {SelectedFolder.Name}；{unresolved} 个项目 ID 未能解析";
        return resources.Count;
    }

    [RelayCommand]
    private void ToggleManage() => IsManaging = !IsManaging;

    [RelayCommand]
    private void CreateFolder()
    {
        try
        {
            var folder = _favorites.CreateFolder(FolderName);
            SelectedFolder = Folders.Single(item => item.Id == folder.Id);
            Status = "已创建收藏夹";
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    [RelayCommand]
    private void RenameFolder()
    {
        if (SelectedFolder is not { } folder) return;
        try
        {
            _favorites.RenameFolder(folder.Id, FolderName);
            Status = "已重命名收藏夹";
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteFolderAsync()
    {
        if (SelectedFolder is not { } folder || !CanDeleteFolder) return;
        if (!await _confirm("删除收藏夹", $"删除「{folder.Name}」及其中的 {folder.Resources.Count} 个收藏？已安装的文件会保留。")) return;
        try
        {
            _favorites.DeleteFolder(folder.Id);
            Status = "已删除收藏夹";
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in Rows) row.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in Rows) row.IsSelected = false;
    }

    [RelayCommand]
    private async Task RemoveSelectedAsync()
    {
        if (SelectedFolder is not { } folder || !HasSelection) return;
        var keys = Rows.Where(row => row.IsSelected).Select(row => row.Resource.Key).ToArray();
        if (!await _confirm("移除收藏", $"从「{folder.Name}」移除选中的 {keys.Length} 个收藏？已安装的文件会保留。")) return;
        try
        {
            _favorites.Remove(folder.Id, keys);
            Status = $"已移除 {keys.Length} 个收藏";
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    [RelayCommand]
    private void CopySelected() => Transfer(false);

    [RelayCommand]
    private void MoveSelected() => Transfer(true);

    private void Transfer(bool move)
    {
        if (!CanTransfer || SelectedFolder is not { } source || DestinationFolder is not { } destination) return;
        var keys = Rows.Where(row => row.IsSelected).Select(row => row.Resource.Key).ToArray();
        try
        {
            _favorites.Transfer(source.Id, destination.Id, keys, move);
            Status = $"已{(move ? "移动" : "复制")} {keys.Length} 个收藏到「{destination.Name}」";
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    [RelayCommand]
    private async Task OpenAsync(FavoriteResource? resource)
    {
        if (resource is null || IsOpening) return;
        IsOpening = true;
        try { await _open(resource); }
        catch (Exception ex) { Status = "打开收藏失败：" + ex.Message; }
        finally { IsOpening = false; }
    }
}

public partial class FavoriteResourceRow(FavoriteResource resource) : ViewModelBase
{
    public FavoriteResource Resource { get; } = resource;

    [ObservableProperty]
    private bool _isSelected;
}
