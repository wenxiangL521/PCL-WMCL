using WMCL.App.Services.Navigation;

namespace WMCL.App.Tests;

public sealed class PageRegistryTests
{
    private sealed class FakePage : ViewModels.ViewModelBase;

    [Fact]
    public void CreatesPageLazilyAndCachesIt()
    {
        var created = 0;
        var registry = new PageRegistry();
        registry.Register("page", () => { created++; return new FakePage(); });

        Assert.False(registry.IsCreated("page"));
        Assert.Equal(0, created);

        var first = registry.Get("page");
        var second = registry.Get("page");
        Assert.Same(first, second);
        Assert.Equal(1, created);
        Assert.True(registry.IsCreated("page"));
    }

    [Fact]
    public void RegisteredInstanceIsReturnedWithoutFactory()
    {
        var page = new FakePage();
        var registry = new PageRegistry();
        registry.Register("page", page);
        Assert.Same(page, registry.Get("page"));
    }

    [Fact]
    public void UnknownKeyThrows()
    {
        var registry = new PageRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.Get("missing"));
    }
}
