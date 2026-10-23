using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;

namespace WMCL.App.ViewModels;

public partial class ResourceFavoritePickerViewModel : ViewModelBase
{
    private readonly ResourceFavoritesService _favorites;

    public ResourceFavoritePickerViewModel(ResourceFavoritesService favorites)
    {
        _favorites = favorites;
        favorites.Changed += (_, _) => RefreshFolders();
        RefreshFolders();
    }

    public ObservableCollection<ResourceFavoriteFolder> Folders { get; } = [];

    [ObservableProperty]
    private ResourceFavoriteFolder? _selectedFolder;

    [ObservableProperty]
    private FavoriteResource? _resource;

    [ObservableProperty]
    private string _status = "";

    public bool HasResource => Resource is not null;
    public bool IsFavorite => Resource is { } resource && SelectedFolder is { } folder &&
                              _favorites.Contains(folder.Id, resource);
    public string ToggleText => IsFavorite ? "♥ 已收藏" : "♡ 收藏";

    partial void OnResourceChanged(FavoriteResource? value)
    {
        Status = "";
        OnPropertyChanged(nameof(HasResource));
        RefreshState();
    }

    partial void OnSelectedFolderChanged(ResourceFavoriteFolder? value) => RefreshState();

    private void RefreshFolders()
    {
        var id = SelectedFolder?.Id;
        Folders.Clear();
        foreach (var folder in _favorites.Folders) Folders.Add(folder);
        SelectedFolder = Folders.FirstOrDefault(folder => folder.Id == id) ?? Folders[0];
        RefreshState();
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(ToggleText));
    }

    [RelayCommand]
    private void Toggle()
    {
        if (Resource is not { } resource || SelectedFolder is not { } folder) return;
        try
        {
            _favorites.SetFavorite(folder.Id, resource, !IsFavorite);
            Status = IsFavorite ? $"已收藏到「{SelectedFolder!.Name}」" : "已从该收藏夹移除";
        }
        catch (Exception ex) { Status = "保存收藏失败：" + ex.Message; }
    }
}
