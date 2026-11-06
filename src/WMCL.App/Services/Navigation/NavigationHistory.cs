using WMCL.App.ViewModels;

namespace WMCL.App.Services.Navigation;

/// <summary>返回栈条目：页面、标题，以及它所属的标签与子页。</summary>
public sealed record NavigationEntry(ViewModelBase Page, string Title, NavItem? TopTab, string? SubKey);

/// <summary>页面返回栈。抽出来便于单独测试，也避免主导航 VM 继续膨胀。</summary>
public sealed class NavigationHistory
{
    private readonly Stack<NavigationEntry> _entries = new();

    public bool CanGoBack => _entries.Count > 0;

    public int Count => _entries.Count;

    public void Push(NavigationEntry entry) => _entries.Push(entry);

    public bool TryPop(out NavigationEntry? entry)
    {
        if (_entries.Count == 0)
        {
            entry = null;
            return false;
        }
        entry = _entries.Pop();
        return true;
    }

    public void Clear() => _entries.Clear();
}
