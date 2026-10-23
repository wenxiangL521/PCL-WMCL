using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WMCL.App.ViewModels;

/// <summary>PCL2 PageDownloadCompDetail 的版本结果卡片。</summary>
public sealed partial class ResourceVersionGroup(
    string title, IEnumerable<ResourceVersionEntry> entries, bool expanded) : ObservableObject
{
    public string Title { get; } = title;
    public IReadOnlyList<ResourceVersionEntry> Entries { get; } = entries.ToList();

    [ObservableProperty]
    private bool _isExpanded = expanded;

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}

public sealed partial class ResourceVersionEntry(
    object source,
    string label,
    string detail,
    bool isCompatible,
    ICommand selectCommand) : ObservableObject
{
    public object Source { get; } = source;
    public string Label { get; } = label;
    public string Detail { get; } = detail;
    public bool IsCompatible { get; } = isCompatible;
    public ICommand SelectCommand { get; } = selectCommand;

    [ObservableProperty]
    private bool _isSelected;
}
