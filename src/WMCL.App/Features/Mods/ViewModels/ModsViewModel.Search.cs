using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Fabric;
using WMCL.Modding.Local;
using WMCL.Modding.OptiFine;
using WMCL.Modding.Forgelike;
using WMCL.Modding.Modrinth;
using WMCL.Modding.LiteLoader;

namespace WMCL.App.Features.Mods;

public partial class ModsViewModel
{
    [RelayCommand]
    private async Task SearchModsAsync()
    {
        if (IsModBusy) return;
        _hasBrowsedMods = true;
        _searchOffset = 0;
        SearchResults.Clear();
        SelectedMod = null;
        CanLoadMore = false;
        await FetchModsAsync(append: false);
    }

    [RelayCommand]
    private async Task LoadMoreModsAsync()
    {
        if (!CanLoadMore || IsModBusy) return;
        await FetchModsAsync(append: true);
    }

    private async Task FetchModsAsync(bool append)
    {
        if (IsModBusy) return;
        var searchToken = ++_modSearchToken;
        var platform = SelectedModSource;
        var query = ModSearchQuery.Create(SearchText, EffectiveGameVersion, SearchLoader, SelectedModSource);
        var game = query.GameVersion;
        var loader = query.Loader;

        IsModBusy = true;
        ModStatus = query.DescribeSearching();
        if (!append)
        {
            SearchResults.Clear();
            SelectedMod = null;
        }
        try
        {
            var page = await _modCatalog.SearchAsync(platform, SearchText, game, loader, _searchOffset);
            if (searchToken != _modSearchToken) return;
            foreach (var project in page.Projects)
                SearchResults.Add(project);
            ModStatus = $"{(platform == "CurseForge" ? "CurseForge" : "Modrinth")} 共 {page.TotalCount} 个结果";
            _searchOffset = page.NextOffset;
            CanLoadMore = page.HasMore;
        }
        catch (Exception ex)
        {
            if (searchToken == _modSearchToken) ModStatus = "搜索失败：" + ex.Message;
        }
        finally
        {
            IsModBusy = false;
            UpdateModCommands();
        }
    }

}
