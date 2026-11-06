using WMCL.App.Features.Downloads;
using WMCL.App.Features.Instances;
using WMCL.App.Features.Mods;
using WMCL.App.Services;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class AutoLoadDetailGuardTests
{
    private static AppServices Services()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "autoload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
    }

    [Fact]
    public void ModpackDetailStopsAutomaticLoadMore()
    {
        var vm = new ModpackViewModel(Services()) { CanLoadMore = true };
        Assert.True(vm.CanAutoLoadMore);
        vm.SelectedProject = new ModpackItem("Pack", "desc", "pack", 1, "Modrinth", "id", 0);
        Assert.False(vm.CanAutoLoadMore);
        vm.SelectedProject = null;
        Assert.True(vm.CanAutoLoadMore);
    }

    [Fact]
    public void ModDetailStopsAutomaticLoadMore()
    {
        var vm = new ModsViewModel(Services()) { CanLoadMore = true };
        Assert.True(vm.CanAutoLoadMore);
        vm.SelectedMod = new ModrinthProject("id", "slug", "Mod", "desc", "mod", 1, null, []);
        Assert.False(vm.CanAutoLoadMore);
    }

    [Fact]
    public void InstanceResourceDetailStopsAutomaticLoadMore()
    {
        var vm = new InstanceContentViewModel(Services()) { CanLoadMoreResources = true };
        Assert.True(vm.CanAutoLoadMoreResources);
        vm.SelectedDownloadProject = new ResourceItem("Title", "desc", "Modrinth", "id", 0);
        Assert.False(vm.CanAutoLoadMoreResources);
    }
}
