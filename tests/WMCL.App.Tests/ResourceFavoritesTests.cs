using System.Text.Json;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class ResourceFavoritesTests
{
    [Fact]
    public void ResourcesPersistSeparatelyFromComponentAndInstanceFavorites()
    {
        var settings = new AppSettings { FavoriteDownloadComponents = ["Fabric"], FavoriteVersions = ["1.20.1"] };
        var saved = "";
        var favorites = new ResourceFavoritesService(settings, () => saved = JsonSerializer.Serialize(settings));
        favorites.SetFavorite("default", Resource(), true);
        favorites.SetFavorite("default", Resource("CurseForge"), true);
        var reloadedSettings = JsonSerializer.Deserialize<AppSettings>(saved)!;
        var reloaded = new ResourceFavoritesService(reloadedSettings, () => { });
        Assert.Equal(2, reloaded.Folders.Single().Resources.Count);
        Assert.True(reloaded.Contains("default", Resource()));
        Assert.True(reloaded.Contains("default", Resource("CurseForge")));
        Assert.Equal(["Fabric"], reloadedSettings.FavoriteDownloadComponents);
        Assert.Equal(["1.20.1"], reloadedSettings.FavoriteVersions);
        reloaded.SetFavorite("default", Resource(), false);
        Assert.Equal("CurseForge", reloaded.Folders.Single().Resources.Single().Platform);
    }

    [Fact]
    public void AddingAgainRefreshesMetadataWithoutDuplicatingProject()
    {
        var favorites = Create();
        favorites.SetFavorite("default", Resource(), true);
        favorites.SetFavorite("default", Resource() with { Title = "New title", ResourceType = "datapack" }, true);
        var saved = Assert.Single(favorites.Folders.Single().Resources);
        Assert.Equal("New title", saved.Title);
        Assert.Equal("datapack", saved.ResourceType);
    }

    [Fact]
    public void CopyAndMoveDeduplicateAndOnlyChangeSelectedFolders()
    {
        var favorites = Create();
        var target = favorites.CreateFolder("Target");
        favorites.SetFavorite("default", Resource(), true);
        favorites.SetFavorite("default", Resource("CurseForge"), true);
        favorites.Transfer("default", target.Id, [Resource().Key], false);
        Assert.Equal(2, favorites.Folders[0].Resources.Count);
        favorites.Transfer("default", target.Id, [Resource().Key], true);
        Assert.Single(favorites.Folders[0].Resources);
        Assert.Single(favorites.Folders[1].Resources);
        favorites.SetFavorite(target.Id, Resource(), false);
        Assert.Empty(favorites.Folders[1].Resources);
        Assert.Equal("CurseForge", favorites.Folders[0].Resources.Single().Platform);
    }

    [Fact]
    public void SaveFailureRollsBackMoveAndDoesNotPublishChange()
    {
        var fail = false;
        var settings = new AppSettings();
        var favorites = new ResourceFavoritesService(settings, () => { if (fail) throw new IOException("full"); });
        var folder = favorites.CreateFolder("Target");
        favorites.SetFavorite("default", Resource(), true);
        var previous = settings.ResourceFavoriteFolders;
        var notifications = 0;
        favorites.Changed += (_, _) => notifications++;
        fail = true;
        Assert.Throws<IOException>(() => favorites.Transfer("default", folder.Id, [Resource().Key], true));
        Assert.Same(previous, settings.ResourceFavoriteFolders);
        Assert.True(favorites.Contains("default", Resource()));
        Assert.Empty(favorites.Folders[1].Resources);
        Assert.Equal(0, notifications);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("默认收藏夹")]
    public void InvalidFolderNamesDoNotChangeStorage(string name)
    {
        var favorites = Create();
        Assert.Throws<InvalidOperationException>(() => favorites.CreateFolder(name));
        Assert.Single(favorites.Folders);
    }

    [Theory]
    [InlineData("CurseForge", "001", "mod")]
    [InlineData("CurseForge", "0", "mod")]
    [InlineData("CurseForge", "-1", "mod")]
    [InlineData("Modrinth", "../project", "mod")]
    [InlineData("Other", "111", "mod")]
    [InlineData("Modrinth", "project", "unknown")]
    public void InvalidProjectIdentityIsRejected(string platform, string id, string type)
    {
        var favorites = Create();
        Assert.Throws<InvalidOperationException>(() => favorites.SetFavorite("default", Resource(platform, id, type), true));
        Assert.Empty(favorites.Folders[0].Resources);
    }

    [Fact]
    public void MalformedSettingsAreNormalizedAndLastFolderIsProtected()
    {
        var settings = new AppSettings
        {
            ResourceFavoriteFolders = [null!, new("bad", "", []), new("valid", "Valid",
                [null!, Resource() with { Description = null!, Slug = null! }, Resource(), Resource("Other")])]
        };
        var favorites = new ResourceFavoritesService(settings, () => { });
        var folder = Assert.Single(favorites.Folders);
        Assert.Equal("valid", folder.Id);
        Assert.Equal("", Assert.Single(folder.Resources).Description);
        Assert.Throws<InvalidOperationException>(() => favorites.DeleteFolder(folder.Id));
        Assert.Single(favorites.Folders);
    }

    [Fact]
    public async Task FilteringClearsHiddenSelectionsAndCanceledRemovalKeepsProjects()
    {
        var favorites = Create();
        favorites.SetFavorite("default", Resource(), true);
        favorites.SetFavorite("default", Resource("CurseForge", "222", "shader") with { Title = "Shader" }, true);
        var model = new ResourceFavoritesViewModel(favorites, _ => Task.CompletedTask, (_, _) => Task.FromResult(false));
        model.SelectAllCommand.Execute(null);
        Assert.Equal(2, model.SelectedCount);
        model.SearchText = "shader";
        Assert.Equal(0, model.SelectedCount);
        Assert.Single(model.Rows);
        model.SelectAllCommand.Execute(null);
        await model.RemoveSelectedCommand.ExecuteAsync(null);
        Assert.Equal(2, favorites.Folders[0].Resources.Count);
        model.SearchText = "111";
        Assert.Single(model.Rows);
        model.SelectedType = "光影";
        Assert.Empty(model.Rows);
    }

    [Fact]
    public async Task PendingConfirmationUsesOriginalFolderAndSelectionSnapshot()
    {
        var favorites = Create();
        var other = favorites.CreateFolder("Other");
        favorites.SetFavorite("default", Resource(), true);
        favorites.SetFavorite(other.Id, Resource(), true);
        var confirmed = new TaskCompletionSource<bool>();
        var model = new ResourceFavoritesViewModel(favorites, _ => Task.CompletedTask, (_, _) => confirmed.Task);
        model.SelectAllCommand.Execute(null);
        var removing = model.RemoveSelectedCommand.ExecuteAsync(null);
        model.SelectedFolder = model.Folders.Single(folder => folder.Id == other.Id);
        confirmed.SetResult(true);
        await removing;
        Assert.Empty(favorites.Folders[0].Resources);
        Assert.Single(favorites.Folders[1].Resources);
        Assert.Equal(other.Id, model.SelectedFolder!.Id);
    }

    [Fact]
    public void PickersStayInSyncAfterFolderRenameRemovalAndResourceChanges()
    {
        var favorites = Create();
        var first = new ResourceFavoritePickerViewModel(favorites) { Resource = Resource() };
        var second = new ResourceFavoritePickerViewModel(favorites) { Resource = Resource() };
        first.ToggleCommand.Execute(null);
        Assert.True(second.IsFavorite);
        var folder = favorites.CreateFolder("Other");
        second.SelectedFolder = second.Folders.Single(item => item.Id == folder.Id);
        Assert.False(second.IsFavorite);
        favorites.RenameFolder(folder.Id, "Renamed");
        Assert.Equal("Renamed", second.SelectedFolder!.Name);
        favorites.DeleteFolder(folder.Id);
        Assert.Equal("default", second.SelectedFolder!.Id);
        Assert.True(second.IsFavorite);
    }

    private static ResourceFavoritesService Create() => new(new AppSettings(), () => { });

    internal static FavoriteResource Resource(string platform = "Modrinth", string id = "111", string type = "mod") =>
        new(platform, id, type, "Test project", "A description", "test-project");
}
