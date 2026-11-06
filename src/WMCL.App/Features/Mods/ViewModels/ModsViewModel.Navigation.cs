using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WMCL.App.Features.Mods;

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

    /// <summary>搜索用版本列表（已安装 + 正式版），首次进入时异步加载。</summary>
    public ObservableCollection<string> SearchGameVersions { get; } = [];



    /// <summary>界面显示实例名，查询时透穿成真实游戏版本。</summary>
    public string EffectiveGameVersion =>
        WMCL.App.Services.SearchVersionFilter.Effective(_services.Repository, _services.Settings, SearchGameVersion);

    public Task EnsureSearchGameVersionsAsync() =>
        WMCL.App.Services.SearchVersionFilter.FillAsync(
            SearchGameVersions, _services.Repository, _services.Settings,
            releasesOnly: false, SearchGameVersion, value => SearchGameVersion = value ?? "");


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
    }
}
