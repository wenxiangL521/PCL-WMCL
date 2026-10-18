using System.Collections.ObjectModel;

namespace WMCL.App.ViewModels;

public sealed class VersionGroup(string title, IEnumerable<VersionItem> versions)
{
    public string Title { get; } = title;
    public ObservableCollection<VersionItem> Versions { get; } = new(versions);
}
