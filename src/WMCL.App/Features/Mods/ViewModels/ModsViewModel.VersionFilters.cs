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
    private void ResetModVersionFilters()
    {
        _allModVersions.Clear();
        _modVersionFilterSet = ResourceVersionFilterSet.Create([]);
        _updatingModVersionFilters = true;
        ModVersionFilters.Clear();
        ModVersionFilters.Add("全部");
        SelectedModVersionFilter = "全部";
        _updatingModVersionFilters = false;
        ModVersions.Clear();
        ModVersionGroups.Clear();
        _modVersionEntries.Clear();
        SelectedModVersion = null;
        OnPropertyChanged(nameof(HasModVersionFilters));
    }

    private void RebuildModVersionFilters()
    {
        _modVersionFilterSet = ResourceVersionFilterSet.Create(
            _allModVersions.SelectMany(version => version.GameVersions ?? []));
        _updatingModVersionFilters = true;
        ModVersionFilters.Clear();
        foreach (var filter in _modVersionFilterSet.Labels) ModVersionFilters.Add(filter);
        var target = string.IsNullOrWhiteSpace(_modGameVersion)
            ? "全部"
            : _modVersionFilterSet.GroupFor(_modGameVersion);
        SelectedModVersionFilter = ModVersionFilters.Contains(target) ? target : "全部";
        _updatingModVersionFilters = false;
        OnPropertyChanged(nameof(HasModVersionFilters));
        ApplyModVersionFilter();
    }

    private void ApplyModVersionFilter()
    {
        var selected = SelectedModVersion;
        ModVersions.Clear();
        foreach (var version in _allModVersions.Where(version =>
                     _modVersionFilterSet.Matches(SelectedModVersionFilter,
                         version.GameVersions ?? [])))
            ModVersions.Add(version);
        SelectedModVersion = ModVersions.FirstOrDefault(version => ReferenceEquals(version, selected))
                              ?? ModVersions.FirstOrDefault();
        RebuildModVersionGroups();
        ModStatus = Mods.ModVersionStatus.Describe(ModVersions.Count, SelectedModVersionFilter);
    }

    private void RebuildModVersionGroups()
    {
        ModVersionGroups.Clear();
        _modVersionEntries.Clear();
        var groups = new Dictionary<string, List<ResourceVersionEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in ModVersions)
        {
            var captured = version;
            var detail = version.Detail +
                         (version.IsCompatible ? "" : " · 不兼容当前安装目标");
            var gameVersions = version.GameVersions ?? [];
            if (gameVersions.Count == 0)
            {
                AddEntry("其他", captured, detail);
                continue;
            }
            foreach (var gameVersion in gameVersions.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!_modVersionFilterSet.Matches(SelectedModVersionFilter, [gameVersion])) continue;
                AddEntry(ResourceVersionFilterSet.CardName(gameVersion), captured, detail);
            }
        }

        var target = string.IsNullOrWhiteSpace(_modGameVersion)
            ? null
            : ResourceVersionFilterSet.CardName(_modGameVersion);
        foreach (var pair in groups
                     .OrderBy(pair => pair.Key == target ? 0 : pair.Key == "其他" ? 2 : 1)
                     .ThenByDescending(pair => ResourceVersionFilterSet.SortCode(pair.Key))
                     .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var entries = pair.Value
                .OrderByDescending(entry => VersionDate(entry.Source))
                .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ModVersionGroups.Add(new ResourceVersionGroup(pair.Key, entries,
                pair.Key == target || groups.Count == 1));
        }
        foreach (var entry in _modVersionEntries)
            entry.IsSelected = ReferenceEquals(entry.Source, SelectedModVersion);
        return;

        void AddEntry(string group, ModVersionItem version, string detail)
        {
            if (!groups.TryGetValue(group, out var entries))
                groups[group] = entries = [];
            if (entries.Any(entry => ReferenceEquals(entry.Source, version))) return;
            var entry = new ResourceVersionEntry(version, version.Label, detail,
                version.IsCompatible, new RelayCommand(() => SelectedModVersion = version));
            entries.Add(entry);
            _modVersionEntries.Add(entry);
        }
    }

    private static DateTimeOffset VersionDate(object source) => source switch
    {
        ModVersionItem { Modrinth: { } modrinth } => modrinth.DatePublished,
        ModVersionItem { CurseForge: { } curseForge } => curseForge.FileDate,
        _ => DateTimeOffset.MinValue
    };

}
