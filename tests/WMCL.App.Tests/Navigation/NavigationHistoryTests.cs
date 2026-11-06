using WMCL.App.Services.Navigation;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class NavigationHistoryTests
{
    [Fact]
    public void PushesAndPopsInOrder()
    {
        var history = new NavigationHistory();
        Assert.False(history.CanGoBack);

        history.Push(new NavigationEntry(null!, "第一页", null, null));
        history.Push(new NavigationEntry(null!, "第二页", null, "mods"));
        Assert.True(history.CanGoBack);
        Assert.Equal(2, history.Count);

        Assert.True(history.TryPop(out var top));
        Assert.Equal("第二页", top!.Title);
        Assert.Equal("mods", top.SubKey);

        Assert.True(history.TryPop(out _));
        Assert.False(history.CanGoBack);
        Assert.False(history.TryPop(out _));
    }

    [Fact]
    public void ClearRemovesEverything()
    {
        var history = new NavigationHistory();
        history.Push(new NavigationEntry(null!, "页", null, null));
        history.Clear();
        Assert.False(history.CanGoBack);
    }
}
