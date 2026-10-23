using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Core;

namespace WMCL.App.Tests;

public sealed class ResourceFavoritesShareTests
{
    [Fact]
    public void ShareCodeRoundTripsValidResourcesAndRejectsBrokenInput()
    {
        var resource = new FavoriteResource("Modrinth", "sodium", "mod", "Sodium", "desc", "sodium");
        var code = ResourceFavoritesService.CreateShareCode([resource]);
        var parsed = Assert.Single(ResourceFavoritesService.ParseShareCode(code));
        Assert.Equal(resource, parsed);
        Assert.Empty(ResourceFavoritesService.ParseShareCode("not json"));
        Assert.Empty(ResourceFavoritesService.ParseShareCode("[]"));
        var native = ResourceFavoritesService.CreateNativeShareCode([resource]);
        Assert.Equal(["sodium"], ResourceFavoritesService.ParseNativeShareCode(native));
        Assert.Empty(ResourceFavoritesService.ParseNativeShareCode("not json"));
    }

    [Fact]
    public void NativeIdShareCodeResolvesKnownFavoritesAndReportsUnknownIds()
    {
        var settings = new AppSettings();
        var service = new ResourceFavoritesService(settings, () => { });
        service.SetFavorite("default", new FavoriteResource("Modrinth", "sodium", "mod", "Sodium", "desc", "sodium"), true);
        var model = new ResourceFavoritesViewModel(service, _ => Task.CompletedTask);
        var code = ResourceFavoritesService.CreateNativeShareCode(
            [new FavoriteResource("Modrinth", "sodium", "mod", "Sodium", "desc", "sodium")]);
        Assert.Equal(1, model.ImportShareCode(code));
        Assert.Single(model.Rows);
        model.ImportShareCode("""["missing"]""");
        Assert.Contains("未在本机收藏中找到", model.Status);
    }

    [Fact]
    public void SelectedFavoritesCanBeSharedAndImportedIntoAnotherFolder()
    {
        var settings = new AppSettings();
        var source = new ResourceFavoritesService(settings, () => { });
        source.SetFavorite("default", new FavoriteResource("Modrinth", "sodium", "mod", "Sodium", "desc", "sodium"), true);
        source.SetFavorite("default", new FavoriteResource("CurseForge", "238222", "mod", "JEI", "desc", ""), true);
        var model = new ResourceFavoritesViewModel(source, _ => Task.CompletedTask);
        Assert.Equal(2, model.Rows.Count);
        model.Rows.Single(row => row.Resource.ProjectId == "sodium").IsSelected = true;
        Assert.True(model.CanShare);
        var code = model.CreateShareCode();
        Assert.Contains("sodium", code);

        var targetSettings = new AppSettings();
        var target = new ResourceFavoritesService(targetSettings, () => { });
        var targetModel = new ResourceFavoritesViewModel(target, _ => Task.CompletedTask);
        Assert.Empty(targetModel.Rows);
        Assert.Equal(1, targetModel.ImportShareCode(code));
        Assert.Single(targetModel.Rows);
        Assert.Equal("sodium", targetModel.Rows[0].Resource.ProjectId);
        Assert.Contains("已导入 1 个收藏", targetModel.Status);
    }
}
