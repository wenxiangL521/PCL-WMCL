using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed class InstanceNavigationReuseContractTests
{
    [AvaloniaFact]
    public async Task EveryInstanceSectionRetainsOneShellAndReusesExistingPageModels()
    {
        using var fixture = new Fixture();
        var main = fixture.Main;
        var originalPage = main.CurrentPage;
        main.NavigateTo("instance");
        var view = new InstanceView { DataContext = main.Instance };
        var window = new Window { Content = view, DataContext = main, Width = 850, Height = 500 };
        try
        {
            window.Show();
            await PumpAsync();
            var menu = view.FindControl<ListBox>("SectionList")!;
            Assert.Equal(10, menu.ItemCount);
            Assert.Null(view.FindControl<Button>("MoreContentButton"));
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "更多内容"));
            var titles = new[] { "概览", "设置", "修改", "Mod 管理", "导出", "服务器", "资源包", "存档", "光影", "截图" };
            for (var index = 0; index < titles.Length; index++)
            {
                menu.SelectedIndex = index;
                await main.InstanceSectionPreparationTask.WaitAsync(TimeSpan.FromSeconds(10));
                await PumpAsync();
                Assert.Same(main.Instance, main.CurrentPage);
                Assert.Equal(index, menu.SelectedIndex);
                Assert.Equal(index, main.Instance.SelectedSectionIndex);
                Assert.Equal("Source", main.Instance.SelectedInstance?.Id);
                Assert.True(main.CanGoBack);
                var sectionHost = index == 9 ? (Control)view.FindControl<ScreenshotView>("ScreenshotCard")!
                    : index is 2 or 3 or 6 or 7 or 8 ? view.FindControl<ContentControl>("EmbeddedPageHost")!
                    : view.FindControl<ScrollViewer>("PageScroll")!;
                Assert.Equal(new Point(20, 15), sectionHost.Bounds.Position);
                if (index == 2)
                {
                    Assert.Same(main.Install, main.Instance.EmbeddedPage);
                    Assert.True(main.Install.IsExistingInstance);
                    Assert.Equal("Source", main.Install.InstanceName);
                    Assert.Contains(view.GetVisualDescendants(), control => control is InstallView);
                }
                else if (index == 3)
                {
                    Assert.Same(main.Mods, main.Instance.EmbeddedPage);
                    Assert.True(main.Mods.IsInstanceManagement);
                    Assert.Equal("Source", main.Mods.SelectedModInstance);
                    Assert.Contains(view.GetVisualDescendants(), control => control is ModsView);
                }
                else if (index is 6 or 7 or 8)
                {
                    Assert.Same(main.Content, main.Instance.EmbeddedPage);
                    Assert.False(main.Content.IsDownloadPage);
                    Assert.Equal("Source", main.Content.SelectedInstance);
                    Assert.Equal(titles[index], main.Content.ContentTypeLabel);
                    Assert.Contains(view.GetVisualDescendants(), control => control is InstanceContentView);
                }
                else Assert.Null(main.Instance.EmbeddedPage);
                var screenshots = Environment.GetEnvironmentVariable("WMCL_INSTANCE_SHOT_DIR");
                if (!string.IsNullOrWhiteSpace(screenshots))
                {
                    Directory.CreateDirectory(screenshots);
                    await Task.Delay(420);
                    await PumpAsync();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    frame.Save(Path.Combine(screenshots, $"instance-{index}.png"));
                }
            }
            main.GoBackCommand.Execute(null);
            Assert.Same(originalPage, main.CurrentPage);
            Assert.False(main.CanGoBack);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TargetChangesUpdateReusedManagementPagesAndIsolatedScreenshots()
    {
        using var fixture = new Fixture();
        var main = fixture.Main;
        main.NavigateTo("instance");
        var view = new InstanceView { DataContext = main.Instance };
        var window = new Window { Content = view, DataContext = main, Width = 850, Height = 500 };
        try
        {
            window.Show();
            foreach (var section in new[] { 3, 6, 7, 8, 9 })
            {
                main.Instance.SelectedInstance = fixture.Source;
                main.Instance.SelectedSectionIndex = section;
                await main.InstanceSectionPreparationTask.WaitAsync(TimeSpan.FromSeconds(10));
                await PumpAsync();
                main.Instance.SelectedInstance = fixture.Other;
                await main.InstanceSectionPreparationTask.WaitAsync(TimeSpan.FromSeconds(10));
                await PumpAsync();
                Assert.Same(main.Instance, main.CurrentPage);
                Assert.Equal(section, main.Instance.SelectedSectionIndex);
                if (section == 3) Assert.Equal("Other", main.Mods.SelectedModInstance);
                if (section is 6 or 7 or 8) Assert.Equal("Other", main.Content.SelectedInstance);
                if (section == 9)
                {
                    var screenshots = view.FindControl<ScreenshotView>("ScreenshotCard")!;
                    Assert.True(screenshots.IsVisible);
                    var files = screenshots.FindControl<ListBox>("ShotList")!.Items.Cast<ScreenshotEntry>().ToArray();
                    Assert.Equal("Other.png", Assert.Single(files).Name);
                    Assert.StartsWith(Path.Combine(fixture.Root, "versions", "Other"), files[0].Path);
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CompatibilityNavigationMethodsDoNotAddAStackEntryForEachSection()
    {
        using var fixture = new Fixture();
        var main = fixture.Main;
        var originalPage = main.CurrentPage;
        main.OpenInstanceMods();
        await main.InstanceSectionPreparationTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(main.Instance, main.CurrentPage);
        Assert.Equal(3, main.Instance.SelectedSectionIndex);
        main.OpenInstanceContent("content-resource", "资源包");
        await main.InstanceSectionPreparationTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(main.Instance, main.CurrentPage);
        Assert.Equal(6, main.Instance.SelectedSectionIndex);
        main.OpenInstanceInstall();
        await main.InstanceInstallPreparationTask.WaitAsync(TimeSpan.FromSeconds(10));
        await main.InstanceSectionPreparationTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(main.Instance, main.CurrentPage);
        Assert.Equal(2, main.Instance.SelectedSectionIndex);
        main.GoBackCommand.Execute(null);
        Assert.Same(originalPage, main.CurrentPage);
        Assert.False(main.CanGoBack);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateModificationPreparationCannotRestoreAnAbandonedTargetOrSection(bool changeTarget)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<WMCL.Modding.Fabric.FabricLoaderInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture(services => new ModsViewModel(services,
            (_, _) =>
            {
                entered.TrySetResult();
                return release.Task;
            },
            (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
            _ => Task.FromResult<IReadOnlyList<WMCL.Modding.OptiFine.OptiFineVersion>>([])));
        var main = fixture.Main;
        main.NavigateTo("instance");
        main.Instance.SelectedSectionIndex = 2;
        var originalPreparation = main.InstanceSectionPreparationTask;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (changeTarget) main.Instance.SelectedInstance = fixture.Other;
            else main.Instance.SelectedSectionIndex = 3;
            var currentPreparation = main.InstanceSectionPreparationTask;
            release.TrySetResult([new WMCL.Modding.Fabric.FabricLoaderInfo("0.16.0", true)]);
            await Task.WhenAll(originalPreparation, currentPreparation).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(main.Instance, main.CurrentPage);
            Assert.Equal(changeTarget ? 2 : 3, main.Instance.SelectedSectionIndex);
            if (changeTarget)
            {
                Assert.Same(main.Install, main.Instance.EmbeddedPage);
                Assert.Equal("Other", main.Install.InstanceName);
            }
            else
            {
                Assert.Same(main.Mods, main.Instance.EmbeddedPage);
                Assert.Equal("Source", main.Mods.SelectedModInstance);
            }
            Assert.False(main.Instance.IsSectionPreparing);
        }
        finally
        {
            release.TrySetResult([]);
            await originalPreparation.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 5; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(WMCL.Tests.TestEnvironment.Root, "instance-navigation-" + Guid.NewGuid().ToString("N"));
        public MainWindowViewModel Main { get; }
        public VersionItem Source { get; }
        public VersionItem Other { get; }

        public Fixture(Func<AppServices, ModsViewModel>? componentFactory = null)
        {
            foreach (var id in new[] { "Source", "Other" })
            {
                var directory = Path.Combine(Root, "versions", id);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, id + ".json"),
                    $$"""{"id":"{{id}}","clientVersion":"1.20.1","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[{"name":"net.fabricmc:fabric-loader:0.16.0"}]}""");
                File.WriteAllText(Path.Combine(directory, id + ".jar"), "client");
                InstanceSettingsStore.Save(directory, new InstanceSettings { IsolationMode = IsolationPolicy.Isolated });
                var screenshots = Path.Combine(directory, "screenshots");
                Directory.CreateDirectory(screenshots);
                using var image = AssetLoader.Open(new Uri("avares://WMCL.App/Assets/Skins/Steve.png"));
                using var output = File.Create(Path.Combine(screenshots, id + ".png"));
                image.CopyTo(output);
            }
            var services = new AppServices(new AppSettings { GameDirectory = Root }, saveSettings: _ => { });
            Main = new MainWindowViewModel(services,
                createInstall: (download, appServices) => new InstallViewModel(download,
                    componentFactory?.Invoke(appServices) ?? InstallTestData.CreateMods(appServices), appServices, null,
                    InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection()),
                createMods: InstallTestData.CreateMods);
            Source = new VersionItem { Id = "Source", IsInstalled = true, Category = "fabric" };
            Other = new VersionItem { Id = "Other", IsInstalled = true, Category = "fabric" };
            Main.Instance.Instances.Add(Source);
            Main.Instance.Instances.Add(Other);
            Main.Instance.SelectedInstance = Source;
        }

        public void Dispose()
        {
            Main.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
