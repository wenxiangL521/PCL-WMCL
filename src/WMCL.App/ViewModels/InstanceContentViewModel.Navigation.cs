using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WMCL.App.ViewModels;

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

    [RelayCommand]
    private void ReturnToResults() => SelectedDownloadProject = null;

    [RelayCommand]
    private void ResetSearch()
    {
        DownloadSearchText = "";
        SearchGameVersion = "";
    }
}
