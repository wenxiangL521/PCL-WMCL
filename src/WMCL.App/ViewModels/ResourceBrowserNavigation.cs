using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WMCL.App.ViewModels;

public partial class ModsViewModel
{
    private bool _hasBrowsedMods;

    public async Task EnsureResourceSearchAsync()
    {
        if (!CanAutoSearchMods || _hasBrowsedMods || HasResourceDetail) return;
        while (IsModBusy)
        {
            await Task.Delay(100);
            if (!CanAutoSearchMods || _hasBrowsedMods || HasResourceDetail) return;
        }
        await SearchModsCommand.ExecuteAsync(null);
    }

    private bool CanAutoSearchMods => IsSearchMode && !IsInstanceManagement && !HasResourceDetail;

    public bool HasResourceDetail => SelectedMod is not null;

    [ObservableProperty]
    private bool _isInstanceManagement;

    [ObservableProperty]
    private string _searchGameVersion = "";

    public string[] SearchLoaders { get; } = ["全部", "fabric", "forge", "neoforge", "quilt", "liteloader"];

    [ObservableProperty]
    private string _searchLoader = "全部";

    [RelayCommand]
    private void ReturnToResults() => SelectedMod = null;

    [RelayCommand]
    private void ResetSearch()
    {
        SearchText = "";
        SearchGameVersion = "";
        SearchLoader = "全部";
        SelectedModSource = "Modrinth";
    }
}

public partial class ModpackViewModel
{
    private bool _hasBrowsedPacks;

    public async Task EnsureResourceSearchAsync()
    {
        if (_hasBrowsedPacks || HasResourceDetail) return;
        while (IsBusy)
        {
            await Task.Delay(100);
            if (_hasBrowsedPacks || HasResourceDetail) return;
        }
        await SearchCommand.ExecuteAsync(null);
    }

    public bool HasResourceDetail => SelectedProject is not null;

    [RelayCommand]
    private void ReturnToResults() => SelectedProject = null;

    [RelayCommand]
    private void ResetSearch()
    {
        SearchText = "";
        GameVersion = "";
        SelectedPlatform = "Modrinth";
    }
}

public partial class InstanceContentViewModel
{
    private bool _hasBrowsedResources;
    private Task _activeResourceSearch = Task.CompletedTask;

    public async Task EnsureResourceSearchAsync()
    {
        var mode = SelectedDownloadType;
        await _activeResourceSearch;
        while (IsDownloadBusy)
        {
            await Task.Delay(100);
            if (mode != SelectedDownloadType || !IsDownloadPage || _hasBrowsedResources || HasResourceDetail) return;
        }
        if (mode != SelectedDownloadType || !IsDownloadPage || _hasBrowsedResources || HasResourceDetail) return;
        await SearchDownloadsCommand.ExecuteAsync(null);
    }

    public bool HasDownloadResults => DownloadResults.Count > 0;

    public bool HasResourceDetail => SelectedDownloadProject is not null;

    [ObservableProperty]
    private bool _isDownloadPage;

    [ObservableProperty]
    private string _searchGameVersion = "";

    [RelayCommand]
    private void ReturnToResults() => SelectedDownloadProject = null;

    [RelayCommand]
    private void ResetSearch()
    {
        DownloadSearchText = "";
        SearchGameVersion = "";
        SelectedPlatform = ShowWorlds ? "CurseForge" : "Modrinth";
    }
}
