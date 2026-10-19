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
        var classified = filtered
            .Select(version => (Version: version, Category: Classify(version)))
            .ToList();

        if (string.IsNullOrWhiteSpace(query))
        {
            // PCL2 的“最新版本”是正式版/预览版首项的副本，原分组仍保留这些版本。
            var latestRelease = classified
                .Where(item => item.Category == "正式版")
                .OrderByDescending(item => item.Version.ReleaseTime ?? "")
                .Select(item => item.Version)
                .FirstOrDefault();
            var latestPreview = classified
                .Where(item => item.Category == "预览版")
                .OrderByDescending(item => item.Version.ReleaseTime ?? "")
                .Select(item => item.Version)
                .FirstOrDefault();

            var latest = new List<VersionItem>();
            if (latestRelease is not null)
                latest.Add(latestRelease);
            if (latestPreview is not null &&
                (latestRelease is null ||
                 CompareReleaseTime(latestPreview, latestRelease) > 0))
                latest.Add(latestPreview);

            if (latest.Count > 0)
                Groups.Add(BuildGroup("最新版本", latest, isExpanded: true));
        }

        AddGroup("正式版", classified.Where(item => item.Category == "正式版").Select(item => item.Version));
        AddGroup("预览版", classified.Where(item => item.Category == "预览版").Select(item => item.Version));
        AddGroup("远古版", classified.Where(item => item.Category == "远古版").Select(item => item.Version));
        AddGroup("愚人节版", classified.Where(item => item.Category == "愚人节版").Select(item => item.Version));

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

    private static int CompareReleaseTime(VersionItem left, VersionItem right)
    {
        var leftTime = DateTimeOffset.TryParse(left.ReleaseTime, out var l)
            ? l
            : DateTimeOffset.MinValue;
        var rightTime = DateTimeOffset.TryParse(right.ReleaseTime, out var r)
            ? r
            : DateTimeOffset.MinValue;
        return leftTime.CompareTo(rightTime);
    }

    private static string Classify(VersionItem version)
    {
        var id = version.Id;
        var type = version.Type;

        if (type == "release") return "正式版";

        if (type == "snapshot")
        {
            // PCL2 把 Mojang 误分类的 1.x 早期正式版快照归回正式版。
            if (id.StartsWith("1.", StringComparison.OrdinalIgnoreCase) &&
                !id.Contains("combat", StringComparison.OrdinalIgnoreCase) &&
                !id.Contains("rc", StringComparison.OrdinalIgnoreCase) &&
                !id.Contains("experimental", StringComparison.OrdinalIgnoreCase) &&
                !id.Contains("pre", StringComparison.OrdinalIgnoreCase))
                return "正式版";

            return IsAprilFools(version) ? "愚人节版" : "预览版";
        }

        if (type == "special") return "愚人节版";

        return "远古版";
    }

    private static bool IsAprilFools(VersionItem version)
    {
        var id = version.Id.Trim().ToLowerInvariant();
        if (id is "20w14infinite" or "20w14∞" or "3d shareware v1.34" or "1.rv-pre1"
            or "15w14a" or "2.0" or "22w13oneblockatatime" or "23w13a_or_b"
            or "24w14potato" or "25w14craftmine" or "26w14a")
            return true;

        if (!DateTimeOffset.TryParse(version.ReleaseTime, out var time)) return false;
        var utcPlusTwo = time.ToUniversalTime().AddHours(2);
        return utcPlusTwo.Month == 4 && utcPlusTwo.Day == 1;
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
