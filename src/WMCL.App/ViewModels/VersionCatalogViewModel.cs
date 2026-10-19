using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WMCL.App.ViewModels;

/// <summary>
/// 下载页的 Minecraft 版本目录阶段。
/// 对应 PCL2 PageDownloadInstall 的 PanSelect 和 PCL-ME 的 MinecraftCatalogStage：
/// 不是独立 PageSelect，而是下载页内的二级选择阶段。
/// </summary>
public partial class VersionCatalogViewModel : ViewModelBase
{
    private IReadOnlyList<VersionItem> _all = [];

    public ObservableCollection<VersionCatalogNode> Groups { get; } = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _resultText = "加载官方版本…";

    [ObservableProperty]
    private VersionCatalogNode? _selectedNode;

    public event EventHandler<VersionItem?>? Finished;

    partial void OnSearchTextChanged(string value) => RebuildGroups();

    partial void OnSelectedNodeChanged(VersionCatalogNode? value) =>
        OnPropertyChanged(nameof(CanConfirm));

    public bool CanConfirm => SelectedNode?.Version is not null;

    public void Open(IReadOnlyList<VersionItem> versions, VersionItem? selected)
    {
        _all = versions.ToList();
        SearchText = "";
        var current = selected is null
            ? null
            : _all.FirstOrDefault(v => v.Id == selected.Id) ?? selected;
        RebuildGroups(current);
    }

    public void RebuildGroups(VersionItem? selected = null)
    {
        selected ??= SelectedNode?.Version;
        var query = SearchText?.Trim() ?? "";
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _all
            : _all.Where(v =>
                    v.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    v.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        Groups.Clear();
        if (string.IsNullOrWhiteSpace(query))
        {
            var latest = filtered
                .Where(v => v.IsLatest)
                .OrderByDescending(v => v.ReleaseTime ?? "")
                .ToList();
            if (latest.Count > 0)
                Groups.Add(BuildGroup("最新版本", latest, isExpanded: true));
        }

        AddGroup("正式版", filtered.Where(v => v.Type == "release"));
        AddGroup("快照", filtered.Where(v => v.Type == "snapshot"));
        AddGroup("旧版本", filtered.Where(v => v.Type is "old_beta" or "old_alpha"));
        AddGroup("愚人节版本", filtered.Where(IsAprilFools));

        ResultText = filtered.Count == 0
            ? "没有匹配的官方版本"
            : $"共 {filtered.Count} 个官方版本";
        SelectedNode = FindVersion(selected);
        if (SelectedNode is null && filtered.Count > 0)
            SelectedNode = FindFirstVersion();

        void AddGroup(string title, IEnumerable<VersionItem> source)
        {
            var items = source
                .OrderByDescending(v => v.ReleaseTime ?? "")
                .ToList();
            if (items.Count == 0) return;
            Groups.Add(BuildGroup(
                title,
                items,
                isExpanded: selected is not null &&
                            items.Any(v => v.Id == selected.Id)));
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedNode?.Version is { } version)
            Finished?.Invoke(this, version);
    }

    [RelayCommand]
    private void Cancel() => Finished?.Invoke(this, null);

    private static VersionCatalogNode BuildGroup(
        string title, IReadOnlyList<VersionItem> items, bool isExpanded)
    {
        var node = new VersionCatalogNode
        {
            Title = $"{title} ({items.Count})",
            IsExpanded = isExpanded
        };
        foreach (var item in items)
            node.Children.Add(new VersionCatalogNode { Version = item });
        return node;
    }

    private VersionCatalogNode? FindVersion(VersionItem? target)
    {
        if (target is null) return null;
        foreach (var group in Groups)
        {
            var node = group.Children.FirstOrDefault(n => n.Version?.Id == target.Id);
            if (node is not null) return node;
        }
        return null;
    }

    private VersionCatalogNode? FindFirstVersion()
    {
        foreach (var group in Groups)
        {
            var node = group.Children.FirstOrDefault(n => n.Version is not null);
            if (node is not null) return node;
        }
        return null;
    }

    private static bool IsAprilFools(VersionItem version)
    {
        if (DateTimeOffset.TryParse(version.ReleaseTime, out var time) &&
            time.Month == 4 && time.Day == 1)
            return true;
        return version.Id.Contains("april", StringComparison.OrdinalIgnoreCase) ||
               version.Id.Contains("fool", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class VersionCatalogNode
{
    public string Title { get; init; } = "";
    public VersionItem? Version { get; init; }
    public bool IsExpanded { get; init; }
    public bool IsGroup => Version is null;
    public bool IsVersion => Version is not null;
    public string Status => Version?.StatusText ?? "";
    public ObservableCollection<VersionCatalogNode> Children { get; } = [];
}
