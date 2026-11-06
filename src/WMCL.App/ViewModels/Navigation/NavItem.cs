using Avalonia.Media;

namespace WMCL.App.ViewModels;

/// <summary>
/// 顶部标签/子页条目。页面既可以立即给定，也可以给工厂按需创建
/// （<see cref="Page"/> 首次读取时才实例化，用于非首屏页面）。
/// </summary>
public sealed class NavItem
{
    private readonly Func<ViewModelBase>? _factory;
    private ViewModelBase? _page;
    private string? _iconData;

    public NavItem(string icon, string title, ViewModelBase page, string key = "", string? iconData = null)
    {
        Icon = icon;
        Title = title;
        _page = page;
        Key = key;
        _iconData = iconData;
    }

    public NavItem(string icon, string title, Func<ViewModelBase> factory, string key = "", string? iconData = null)
    {
        Icon = icon;
        Title = title;
        _factory = factory;
        Key = key;
        _iconData = iconData;
    }

    public string Icon { get; }

    public string Title { get; }

    public string Key { get; }

    /// <summary>页面实例；工厂条目在首次访问时才创建。</summary>
    public ViewModelBase Page => _page ??= _factory!();

    public bool IsHeader { get; init; }

    public string? IconData => _iconData;

    public bool HasIcon => !string.IsNullOrEmpty(_iconData);

    public Geometry? IconGeometry => _iconData is null ? null : Geometry.Parse(_iconData);
}
