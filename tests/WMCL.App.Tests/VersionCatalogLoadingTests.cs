using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Core;
using static WMCL.App.Tests.VersionCatalogTests;

namespace WMCL.App.Tests;

public sealed class VersionCatalogLoadingTests
{
    [Fact]
    public async Task OpeningCatalogWaitsForAnAlreadyRunningLoad()
    {
        var services = CreateServices();
        var pending = new TaskCompletionSource<IReadOnlyList<VersionItem>>();
        var calls = 0;
        var download = new DownloadViewModel(services, () =>
        {
            calls++;
            return pending.Task;
        });
        var install = new InstallViewModel(download, InstallTestData.CreateMods(services), services, null, InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        var startup = download.LoadAsync();
        var opening = install.OpenVersionCatalogCommand.ExecuteAsync(null);
        Assert.True(install.IsChoosingVersion);
        Assert.True(install.VersionCatalog.IsLoading);
        Assert.False(opening.IsCompleted);
        Assert.Equal(1, calls);
        pending.SetResult([Version("1.21")]);
        await Task.WhenAll(startup, opening);
        Assert.False(install.VersionCatalog.IsLoading);
        Assert.Equal("1.21", install.VersionCatalog.Groups[0].Entries[0].Version.Id);
        Assert.False(install.VersionCatalog.CanCancel);
    }

    [Fact]
    public async Task FailedCatalogShowsErrorAndRetryCanSucceed()
    {
        var services = CreateServices();
        var calls = 0;
        var download = new DownloadViewModel(services, () => ++calls == 1
            ? Task.FromException<IReadOnlyList<VersionItem>>(new IOException("测试连接失败"))
            : Task.FromResult<IReadOnlyList<VersionItem>>([Version("1.21")]));
        var install = new InstallViewModel(download, InstallTestData.CreateMods(services), services, null, InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        await install.OpenVersionCatalogCommand.ExecuteAsync(null);
        Assert.True(install.VersionCatalog.HasError);
        Assert.False(install.VersionCatalog.IsEmpty);
        Assert.False(install.VersionCatalog.IsLoading);
        await install.RefreshVersionCatalogCommand.ExecuteAsync(null);
        Assert.False(install.VersionCatalog.HasError);
        Assert.False(install.VersionCatalog.IsEmpty);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RefreshFinishingAfterLeavingCatalogDoesNotReopenIt()
    {
        var services = CreateServices();
        var pending = new TaskCompletionSource<IReadOnlyList<VersionItem>>();
        var download = new DownloadViewModel(services, () => pending.Task);
        var current = Version("1.21");
        download.Versions.Add(current);
        var install = new InstallViewModel(download, InstallTestData.CreateMods(services), services, null, InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection());
        await install.OpenVersionCatalogCommand.ExecuteAsync(null);
        install.VersionCatalog.Groups[0].Entries[0].SelectCommand.Execute(null);
        await install.OpenVersionCatalogCommand.ExecuteAsync(null);
        var refreshing = install.RefreshVersionCatalogCommand.ExecuteAsync(null);
        install.VersionCatalog.CancelCommand.Execute(null);
        pending.SetResult([current, Version("1.20")]);
        await refreshing;
        Assert.False(install.IsChoosingVersion);
        Assert.Same(current, download.SelectedVersion);
        Assert.False(install.VersionCatalog.IsLoading);
    }

    private static AppServices CreateServices() => new(new AppSettings
    {
        GameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-catalog-tests", Guid.NewGuid().ToString("N"))
    });

    [Fact]
    public async Task RefreshPreservesTheChosenIdWhenCatalogObjectsAreReplaced()
    {
        var services = CreateServices();
        var refreshed = Version("1.20");
        var latest = new VersionItem { Id = "1.21", IsLatest = true };
        var download = new DownloadViewModel(services, () =>
            Task.FromResult<IReadOnlyList<VersionItem>>([latest, refreshed]));
        download.SelectedVersion = Version("1.20");
        await download.LoadAsync();
        Assert.Same(refreshed, download.SelectedVersion);
    }
}
