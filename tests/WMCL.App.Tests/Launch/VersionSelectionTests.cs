using WMCL.App.ViewModels;
using WMCL.App.ViewModels.Launching;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class VersionSelectionTests
{
    private static VersionItem Item(string id, bool installed, bool hidden = false, string category = "") =>
        new()
        {
            Id = id,
            IsInstalled = installed,
            IsHidden = hidden,
            Category = category
        };

    [Fact]
    public void InstalledFiltersOnlyInstalled()
    {
        var all = new[] { Item("a", true), Item("b", false), Item("c", true) };
        Assert.Equal(["a", "c"], VersionSelection.Installed(all).Select(item => item.Id));
    }

    [Fact]
    public void PickKeepsPreviousSelectionWhenAvailable()
    {
        var installed = new[] { Item("a", true), Item("b", true) };
        Assert.Equal("b", VersionSelection.Pick(installed, "b")!.Id);
    }

    [Fact]
    public void PickSkipsHiddenAndErrorWhenFallingBack()
    {
        var installed = new[] { Item("err", true, category: "error"), Item("hidden", true, hidden: true), Item("ok", true) };
        Assert.Equal("ok", VersionSelection.Pick(installed, null)!.Id);
    }

    [Fact]
    public void PickReturnsNullWhenNothingSelectable()
    {
        var installed = new[] { Item("hidden", true, hidden: true) };
        Assert.Null(VersionSelection.Pick(installed, null));
    }

    [Fact]
    public void PickFallsBackWhenPreviousWasRemoved()
    {
        var installed = new[] { Item("a", true) };
        Assert.Equal("a", VersionSelection.Pick(installed, "gone")!.Id);
    }
}
