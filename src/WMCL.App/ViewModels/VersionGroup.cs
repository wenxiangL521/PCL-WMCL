using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WMCL.App.ViewModels;

public sealed partial class VersionGroup(string title, IEnumerable<VersionItem> versions, bool expanded = true) : ObservableObject
{
    public string Title { get; } = title;
    public ObservableCollection<VersionItem> Versions { get; } = new(versions);
    public string DisplayTitle => Title == "收藏夹" ? Title : $"{Title} ({Versions.Count})";
    [ObservableProperty]
    private bool _isExpanded = expanded;


    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}
