using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WMCL.App.Animation;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class VersionPickerWindow : Window
{
    private readonly List<VersionItem> _all;
    private VersionItem? _selected;

    public VersionPickerWindow()
    {
        _all = [];
        InitializeComponent();
        Opened += async (_, _) =>
        {
            await ModalMotion.AnimateInAsync(this);
            SearchBox.Focus();
        };
    }

    public VersionPickerWindow(
        IReadOnlyList<VersionItem> versions,
        VersionItem? selected) : this()
    {
        _all = versions.ToList();
        _selected = selected is null
            ? null
            : _all.FirstOrDefault(v => v.Id == selected.Id) ?? selected;
        RebuildGroups();
    }

    private void RebuildGroups()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _all
            : _all.Where(v =>
                    v.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    v.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var groups = new List<VersionPickerNode>();

        if (string.IsNullOrWhiteSpace(query))
        {
            var latest = filtered
                .Where(v => v.IsLatest)
                .OrderByDescending(v => v.ReleaseTime ?? "")
                .ToList();
            if (latest.Count > 0)
                groups.Add(BuildGroup("最新版本", latest, isExpanded: true));
        }

        AddGroup("正式版", filtered.Where(v => v.Type == "release"));
        AddGroup("快照", filtered.Where(v => v.Type == "snapshot"));
        AddGroup("旧版本", filtered.Where(v => v.Type is "old_beta" or "old_alpha"));
        AddGroup("愚人节版本", filtered.Where(IsAprilFools));

        VersionTree.ItemsSource = groups;
        VersionTree.SelectedItem = FindVersion(groups, _selected);
        ResultText.Text = filtered.Count == 0
            ? "没有匹配的官方版本"
            : $"共 {filtered.Count} 个官方版本";
        if (filtered.Count > 0 && VersionTree.SelectedItem is null)
        {
            var first = FindFirstVersion(groups);
            if (first is not null) VersionTree.SelectedItem = first;
        }

        void AddGroup(string title, IEnumerable<VersionItem> source)
        {
            var items = source
                .OrderByDescending(v => v.ReleaseTime ?? "")
                .ToList();
            if (items.Count == 0) return;
            groups.Add(BuildGroup(
                title,
                items,
                isExpanded: _selected is not null &&
                            items.Any(v => v.Id == _selected.Id)));
        }
    }

    private static VersionPickerNode BuildGroup(
        string title, IReadOnlyList<VersionItem> items, bool isExpanded)
    {
        var node = new VersionPickerNode
        {
            Title = $"{title} ({items.Count})",
            IsExpanded = isExpanded
        };
        foreach (var item in items)
            node.Children.Add(new VersionPickerNode { Version = item });
        return node;
    }

    private static VersionPickerNode? FindVersion(
        IReadOnlyList<VersionPickerNode> groups, VersionItem? target)
    {
        if (target is null) return null;
        foreach (var group in groups)
        {
            foreach (var node in group.Children)
                if (node.Version?.Id == target.Id)
                    return node;
        }
        return null;
    }

    private static VersionPickerNode? FindFirstVersion(IReadOnlyList<VersionPickerNode> groups)
    {
        foreach (var group in groups)
        foreach (var node in group.Children)
            if (node.Version is not null) return node;
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

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => RebuildGroups();

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (VersionTree.SelectedItem is VersionPickerNode { Version: not null } node)
            _selected = node.Version;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (VersionTree.SelectedItem is VersionPickerNode { Version: not null } node)
            Close(node.Version);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _selected is not null)
        {
            Close(_selected);
            e.Handled = true;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(_selected);
}

internal sealed class VersionPickerNode
{
    public string Title { get; init; } = "";
    public VersionItem? Version { get; init; }
    public bool IsExpanded { get; init; }
    public bool IsGroup => Version is null;
    public bool IsVersion => Version is not null;
    public string Status => Version?.StatusText ?? "";
    public ObservableCollection<VersionPickerNode> Children { get; } = [];
}
