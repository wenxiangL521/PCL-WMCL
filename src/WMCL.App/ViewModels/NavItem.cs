namespace WMCL.App.ViewModels;

public sealed record NavItem(
    string Icon,
    string Title,
    ViewModelBase Page,
    string Key = "",
    string? IconData = null)
{
    public bool HasIcon => !string.IsNullOrEmpty(IconData);
}
