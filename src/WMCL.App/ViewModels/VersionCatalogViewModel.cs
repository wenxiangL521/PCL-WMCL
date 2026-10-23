using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WMCL.App.ViewModels;

public partial class VersionCatalogViewModel : ViewModelBase
{
    private IReadOnlyList<VersionItem> _all = [];
    private VersionItem? _selected;
    private readonly Dictionary<string, bool> _expansion = [];

    public ObservableCollection<VersionCatalogGroup> Groups { get; } = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private string _resultText = "加载官方版本…";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    public bool HasError => ErrorMessage.Length > 0;
    public bool IsEmpty => !IsLoading && !HasError && Groups.Count == 0;
    public bool HasSearch => !string.IsNullOrWhiteSpace(SearchText);
    public string EmptyMessage => HasSearch ? "没有匹配的官方版本" : "暂无可下载的官方版本，请刷新列表";
    public string StatusText => IsLoading ? "正在获取官方版本…" : HasError ? "暂时无法获取官方版本" : ResultText;
    public bool CanCancel => _selected is not null;
    public event EventHandler<VersionItem?>? Finished;

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnResultTextChanged(string value) => OnPropertyChanged(nameof(StatusText));

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) IsSearchVisible = true;
        RebuildGroups();
        OnPropertyChanged(nameof(HasSearch));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    public void Open(IReadOnlyList<VersionItem> versions, VersionItem? selected)
    {
        _all = versions.ToList();
        _selected = selected;
        _expansion.Clear();
        IsSearchVisible = false;
        ErrorMessage = "";
        if (SearchText.Length > 0) SearchText = "";
        else RebuildGroups();
        OnPropertyChanged(nameof(CanCancel));
    }

    private void RebuildGroups()
    {
        var query = SearchText.Trim();
        var classified = _all.Select(version =>
                (Version: version, Category: Classify(version)))
            .OrderByDescending(item => ReleaseDate(item.Version))
            .ToList();
        var filtered = classified.Where(item => query.Length == 0 ||
                item.Version.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Version.Id.Replace("infinite", "∞", StringComparison.OrdinalIgnoreCase)
                    .Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Version.Type.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                Summary(item.Version, item.Category).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Groups.Clear();
        if (query.Length == 0)
        {
            var release = classified.FirstOrDefault(item => item.Category == "正式版").Version;
            var preview = classified.FirstOrDefault(item => item.Category == "预览版").Version;
            var latest = new List<VersionCatalogEntry>();
            if (release is not null)
                latest.Add(CreateEntry(release, "正式版", $"最新正式版，发布于 {FormatDate(release)}"));
            if (preview is not null && (release is null || ReleaseDate(preview) > ReleaseDate(release)))
                latest.Add(CreateEntry(preview, "预览版", $"最新预览版，发布于 {FormatDate(preview)}"));
            if (latest.Count > 0) AddGroup("最新版本", latest);
        }

        foreach (var category in new[] { "正式版", "预览版", "远古版", "愚人节版" })
        {
            var entries = filtered.Where(item => item.Category == category)
                .Select(item => CreateEntry(item.Version, category, Summary(item.Version, category)))
                .ToList();
            if (entries.Count > 0) AddGroup(category, entries);
        }

        ResultText = filtered.Count == 0 ? "没有匹配的官方版本" : $"共 {filtered.Count} 个官方版本";
        OnPropertyChanged(nameof(IsEmpty));

        void AddGroup(string title, IReadOnlyList<VersionCatalogEntry> entries)
        {
            var group = new VersionCatalogGroup(title, entries,
                query.Length > 0 || _expansion.GetValueOrDefault(title, title == "最新版本"));
            if (query.Length == 0)
                group.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(VersionCatalogGroup.IsExpanded))
                        _expansion[title] = group.IsExpanded;
                };
            Groups.Add(group);
        }
    }

    private VersionCatalogEntry CreateEntry(VersionItem version, string category, string summary) =>
        new(version, category, summary, version.Id == _selected?.Id,
            new RelayCommand(() => Finished?.Invoke(this, version)));

    private static DateTimeOffset ReleaseDate(VersionItem version) =>
        DateTimeOffset.TryParse(version.ReleaseTime, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date : DateTimeOffset.MinValue;

    private static string FormatDate(VersionItem version)
    {
        var date = ReleaseDate(version);
        return date == DateTimeOffset.MinValue ? "未知时间"
            : date.LocalDateTime.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
    }

    internal static string Classify(VersionItem version)
    {
        if (version.Type == "release") return "正式版";
        if (version.Type == "special") return "愚人节版";
        if (version.Type != "snapshot") return "远古版";
        if (IsAprilFools(version)) return "愚人节版";

        var id = version.Id;
        return id.StartsWith("1.", StringComparison.OrdinalIgnoreCase) &&
               !new[] { "combat", "rc", "experimental", "pre" }
                   .Any(part => id.Contains(part, StringComparison.OrdinalIgnoreCase))
            ? "正式版" : "预览版";
    }

    private static bool IsAprilFools(VersionItem version)
    {
        if (FoolName(version.Id).Length > 0) return true;
        var date = ReleaseDate(version).ToUniversalTime().AddHours(2);
        return date.Month == 4 && date.Day == 1;
    }

    private static string Summary(VersionItem version, string category) =>
        category == "愚人节版" && FoolName(version.Id) is { Length: > 0 } lore
            ? lore : FormatDate(version);

    private static string FoolName(string id) => id.ToLowerInvariant() switch
    {
        "2.0" => "2013 | 这个秘密计划了两年的更新将游戏推向了一个新高度！",
        "15w14a" => "2015 | 作为一款全年龄向的游戏，我们需要和平，需要爱与拥抱。",
        "1.rv-pre1" => "2016 | 是时候将现代科技带入 Minecraft 了！",
        "3d shareware v1.34" => "2019 | 我们从地下室的废墟里找到了这个开发于 1994 年的杰作！",
        "20w14infinite" or "20w14∞" => "2020 | 我们加入了 20 亿个新的维度，让无限的想象变成了现实！",
        "22w13oneblockatatime" => "2022 | 一次一个方块更新！迎接全新的挖掘、合成与骑乘玩法吧！",
        "23w13a_or_b" => "2023 | 研究表明：玩家喜欢作出选择——越多越好！",
        "24w14potato" => "2024 | 毒马铃薯一直都被大家忽视和低估，于是我们超级加强了它！",
        "25w14craftmine" => "2025 | 你可以合成任何东西——包括合成你的世界！",
        "26w14a" => "2026 | 为什么需要物品栏？让方块们跟着你走吧！",
        _ => ""
    };

    [RelayCommand]
    private void Cancel()
    {
        if (CanCancel) Finished?.Invoke(this, null);
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";
}

public sealed partial class VersionCatalogGroup(
    string title, IReadOnlyList<VersionCatalogEntry> entries, bool isExpanded) : ObservableObject
{
    public string Title { get; } = title;
    public string CountText => $"{Entries.Count}";
    public IReadOnlyList<VersionCatalogEntry> Entries { get; } = entries;

    [ObservableProperty]
    private bool _isExpanded = isExpanded;

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}

public sealed class VersionCatalogEntry(
    VersionItem version, string category, string summary, bool isCurrent, IRelayCommand selectCommand)
{
    private static readonly ConcurrentDictionary<string, Bitmap> Icons = new();

    public VersionItem Version { get; } = version;
    public string Title => Version.Id.Equals("20w14infinite", StringComparison.OrdinalIgnoreCase)
        ? "20w14∞" : Version.Id;
    public string Summary { get; } = summary;
    public bool IsCurrent { get; } = isCurrent;
    public IRelayCommand SelectCommand { get; } = selectCommand;
    public Bitmap IconImage => LoadIcon(Icon);
    internal static Bitmap LoadIcon(string icon) => Icons.GetOrAdd(icon, path =>
    {
        using var stream = AssetLoader.Open(new Uri(path));
        return new Bitmap(stream);
    });
    public string Icon => $"avares://WMCL.App/Assets/Blocks/{category switch
    {
        "正式版" => "Grass",
        "预览版" => "CommandBlock",
        "愚人节版" => "GoldBlock",
        _ => "CobbleStone"
    }}.png";
}
