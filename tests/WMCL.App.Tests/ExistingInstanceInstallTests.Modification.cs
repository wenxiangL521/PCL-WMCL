using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.Controls;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using WMCL.Core.IO;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed partial class ExistingInstanceInstallTests
{
    [AvaloniaFact]
    public async Task ExistingComponentTransactionPersistsApiIdentityForTheNextModification()
    {
        CreateInstance();
        var plan = Plan();
        var hash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes("new api")));
        plan = plan with { ApiVersion = plan.ApiVersion! with { Files = [plan.ApiVersion!.Files[0] with { Sha1 = hash }] } };
        var result = await Service(new Backend()).InstallAsync(plan, (_, _, _) => { });
        Assert.Empty(result.Errors);
        var model = ViewModel(Services());
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.True(model.IsExistingInstance);
        Assert.Equal(hash, model.ApiSelection.SelectedVersion!.Files[0].Sha1);
        Assert.False(model.ApiCard.HasComparison);
        Assert.False(model.CanApplyInstall);
    }

    [AvaloniaFact]
    public async Task NameOnlyModificationMovesIdentityWithoutDownloadingOrChangingUserData()
    {
        CreateInstance();
        var backend = new Backend();
        var services = Services();
        var model = ViewModel(services, backend: backend);
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.False(model.CanApplyInstall);
        var dependent = DirectoryFor("Child");
        Directory.CreateDirectory(dependent);
        File.WriteAllText(Path.Combine(dependent, "Child.json"), """{"id":"Child","inheritsFrom":"Custom"}""");
        var libraries = Path.Combine(_root, "libraries", "wmcl-packs", "Custom");
        Directory.CreateDirectory(libraries);
        File.WriteAllText(Path.Combine(libraries, "user.jar"), "custom library");
        InstanceChangeEventArgs? change = null;
        model.InstancesChanged += (_, update) => change = update;
        model.InstanceName = "Renamed";
        Assert.True(model.CanApplyInstall);
        await model.InstallCombinedCommand.ExecuteAsync(null);
        Assert.Equal(0, backend.VanillaCalls);
        Assert.False(Directory.Exists(DirectoryFor()));
        Assert.Equal("original client", File.ReadAllText(Path.Combine(DirectoryFor("Renamed"), "Renamed.jar")));
        Assert.Equal("description", InstanceSettingsStore.Load(DirectoryFor("Renamed")).Description);
        Assert.Equal("progress", File.ReadAllText(Path.Combine(_root, "saves", "World", "level.dat")));
        Assert.Equal("user mod", File.ReadAllText(Path.Combine(_root, "mods", "user.jar")));
        Assert.Equal("custom library", File.ReadAllText(Path.Combine(_root, "libraries", "wmcl-packs", "Renamed", "user.jar")));
        Assert.Contains("Renamed", File.ReadAllText(Path.Combine(dependent, "Child.json")));
        Assert.DoesNotContain(InstallManifestStore.Load(_root, "Renamed")!.ManagedFiles,
            file => file.RelativePath.StartsWith("versions/Custom/", StringComparison.Ordinal));
        Assert.Equal("Renamed", change!.TargetId);
        Assert.Equal("Renamed", model.TargetInstanceId);
        Assert.Equal("Renamed", model.InstanceName);
        Assert.False(model.CanApplyInstall);
        Assert.True(services.Settings.HintModifyInstanceWarningAcknowledged);
    }

    [AvaloniaFact]
    public async Task NameEditSurvivesMinecraftChangeAndRestorationDoesNotMutateFiles()
    {
        CreateInstance();
        var services = Services();
        var download = new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions);
        download.Versions.Add(new VersionItem { Id = "1.21.1" });
        var model = ViewModel(services, download);
        await model.OpenInstanceAsync(_root, "Custom");
        var before = Snapshot();
        model.InstanceName = "My edited name";
        await model.OpenVersionCatalogCommand.ExecuteAsync(null);
        model.VersionCatalog.Groups.SelectMany(group => group.Entries)
            .First(entry => entry.Version.Id == "1.21.1").SelectCommand.Execute(null);
        await model.PendingComponentSelectionTask;
        Assert.Equal("My edited name", model.InstanceName);
        Assert.Equal("Custom", model.TargetInstanceId);
        model.RestoreInstanceNameCommand.Execute(null);
        Assert.Equal("Custom", model.InstanceName);
        Assert.False(model.HasInstanceNameChange);
        Assert.True(model.CanApplyInstall);
        Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
    }

    [AvaloniaTheory]
    [InlineData("Custom ")]
    [InlineData(" Custom")]
    [InlineData(" ")]
    [InlineData("../escape")]
    public async Task InvalidRenameCannotReachTheBackendOrFilesystem(string requestedName)
    {
        CreateInstance();
        var backend = new Backend();
        var model = ViewModel(Services(), backend: backend);
        await model.OpenInstanceAsync(_root, "Custom");
        var before = Snapshot();
        model.InstanceName = requestedName;
        Assert.True(model.HasInstanceNameError);
        Assert.False(model.CanApplyInstall);
        await model.InstallCombinedCommand.ExecuteAsync(null);
        Assert.Equal(0, backend.VanillaCalls);
        Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
        Assert.Throws<ArgumentException>(() => CombinedInstallPlan.Create(_root, "1.20.1", false, "无", null,
            null, false, false, requestedName, existingInstanceId: "Custom"));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameCollisionDisablesModification(bool libraryCollision)
    {
        CreateInstance();
        var model = ViewModel(Services());
        await model.OpenInstanceAsync(_root, "Custom");
        Directory.CreateDirectory(libraryCollision
            ? Path.Combine(_root, "libraries", "wmcl-packs", "Renamed") : DirectoryFor("Renamed"));
        model.InstanceName = "Renamed";
        Assert.Contains("已存在", model.InstanceNameError);
        Assert.False(model.CanApplyInstall);
    }

    [AvaloniaFact]
    public async Task SubmittedNameOnlyModificationCanCancelBeforeAtomicRename()
    {
        CreateInstance();
        var model = ViewModel(Services());
        await model.OpenInstanceAsync(_root, "Custom");
        var before = Snapshot();
        model.InstanceName = "Renamed";
        model.ExistingInstallSubmitted += (_, _) => model.CancelCombinedCommand.Execute(null);
        TaskEntry? terminal = null;
        model.TaskFinished += (_, task) => terminal = task;
        await model.InstallCombinedCommand.ExecuteAsync(null);
        Assert.Equal("已取消", terminal!.PercentText);
        Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
        Assert.False(Directory.Exists(DirectoryFor("Renamed")));
    }

    [AvaloniaTheory]
    [InlineData("profile")]
    [InlineData("settings")]
    [InlineData("manifest")]
    [InlineData("deleted")]
    [InlineData("root")]
    [InlineData("rename")]
    public async Task ConfirmationRejectsChangesToTheCapturedTarget(string mutation)
    {
        CreateInstance();
        var services = Services();
        var backend = new Backend();
        var entered = new TaskCompletionSource();
        var answer = new TaskCompletionSource<bool>();
        var model = new InstallViewModel(new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions), InstallTestData.CreateMods(services), services,
            backend.ForRoot, InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection(),
            confirmModification: (_, _) => { entered.SetResult(); return answer.Task; });
        await model.OpenInstanceAsync(_root, "Custom");
        model.InstanceName = "Renamed";
        var operation = model.InstallCombinedCommand.ExecuteAsync(null);
        await entered.Task;
        if (mutation == "profile") File.AppendAllText(Path.Combine(DirectoryFor(), "Custom.json"), " ");
        else if (mutation == "settings") File.AppendAllText(InstanceSettingsStore.PathFor(DirectoryFor()), " ");
        else if (mutation == "manifest") File.AppendAllText(InstallManifestStore.GetPath(_root, "Custom"), " ");
        else if (mutation == "deleted") File.Delete(Path.Combine(DirectoryFor(), "Custom.json"));
        else if (mutation == "root") services.SetGameDirectory(Path.Combine(_root, "next"));
        else model.ApplyInstanceChange(new(_root, "Custom", "Elsewhere", InstanceChangeKind.Renamed));
        answer.SetResult(true);
        await operation;
        Assert.Contains(mutation == "root" ? "游戏目录已切换" : "确认期间目标实例已变化", model.CombinedStatus);
        Assert.False(Directory.Exists(DirectoryFor("Renamed")));
        Assert.Equal(0, backend.VanillaCalls);
        Assert.False(services.Settings.HintModifyInstanceWarningAcknowledged);
    }

    [AvaloniaFact]
    public async Task ModificationPlanRetainsTheNameAcceptedAtConfirmation()
    {
        CreateInstance();
        var services = Services();
        var entered = new TaskCompletionSource();
        var answer = new TaskCompletionSource<bool>();
        var model = new InstallViewModel(new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions), InstallTestData.CreateMods(services), services,
            root => new Backend().ForRoot(root), InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection(),
            confirmModification: (_, _) => { entered.SetResult(); return answer.Task; });
        await model.OpenInstanceAsync(_root, "Custom");
        model.InstanceName = "Accepted";
        var operation = model.InstallCombinedCommand.ExecuteAsync(null);
        await entered.Task;
        Assert.False(model.CanEditInstall);
        model.InstanceName = "Later";
        answer.SetResult(true);
        await operation;
        Assert.True(Directory.Exists(DirectoryFor("Accepted")));
        Assert.False(Directory.Exists(DirectoryFor("Later")));
        Assert.Equal("Accepted", model.TargetInstanceId);
    }

    [AvaloniaFact]
    public async Task OriginalNameSurfaceUsesTheOriginalMarginsAndAnimationDuration()
    {
        CreateInstance();
        var model = ViewModel(Services());
        await model.OpenInstanceAsync(_root, "Custom");
        var view = new InstallView { DataContext = model };
        var name = view.FindControl<TextBox>("InstallNameBox")!;
        var transition = Assert.IsType<ThicknessTransition>(Assert.Single(name.Transitions!));
        Assert.Equal(TimeSpan.FromMilliseconds(150), transition.Duration);
        Assert.IsType<CubicEaseOut>(transition.Easing);
        Assert.Equal(new Thickness(56, 16, 16, 0), model.InstallNameMargin);
        model.InstanceName = "Renamed";
        Assert.Equal(new Thickness(56, 16, 40, 0), model.InstallNameMargin);
        Assert.True(view.FindControl<TextBlock>("OriginalInstanceNameLabel")!.IsVisible);
        model.RestoreInstanceNameCommand.Execute(null);
        Assert.False(view.FindControl<Button>("OriginalNameRestoreButton")!.IsVisible);
        Assert.False(model.CanApplyInstall);
    }

    [Fact]
    public async Task ComponentChangesAndRenamePublishTogether()
    {
        CreateInstance();
        var result = await Service(new Backend()).InstallAsync(Plan() with { RenameTo = "Renamed" }, (_, _, _) => { });
        Assert.Equal("Renamed", result.InstanceId);
        Assert.False(Directory.Exists(DirectoryFor()));
        var version = await new VersionRepository(_root).GetVersionAsync("Renamed");
        Assert.Equal("fabric", MinecraftVersionUtil.GetLoader(version));
        Assert.Equal("new client", File.ReadAllText(Path.Combine(DirectoryFor("Renamed"), "Renamed.jar")));
        Assert.Equal("progress", File.ReadAllText(Path.Combine(_root, "saves", "World", "level.dat")));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "Renamed")).IsHealthy);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CompletedNotificationFailureRestoresRenameDependenciesAndProvenance(bool throwFailureNotification, bool cancelled)
    {
        CreateInstance();
        foreach (var child in new[] { "Child", "JarOnly" })
        {
            Directory.CreateDirectory(DirectoryFor(child));
            var profilePath = Path.Combine(DirectoryFor(child), child + ".json");
            File.WriteAllText(profilePath, child == "Child"
                ? """{ "id": "Child", "inheritsFrom": "Custom", "jar": "Custom", "custom": "Custom" }"""
                : """{ "id": "JarOnly", "clientVersion": "1.20.1", "jar": "Custom", "libraries": [] }""");
            foreach (var format in new[] { "WMCL", "PCL" })
            {
                var manifestPath = Path.Combine(DirectoryFor(child), format, "install.json");
                Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
                var manifest = new InstallManifest
                {
                    LastModifiedUtc = DateTimeOffset.Parse("2020-01-02T03:04:05Z"),
                    ManagedFiles = [new("versions/" + child + "/" + child + ".json", FileUtil.Sha1(profilePath),
                        "merged", "https://example.invalid/original.json", "original.json", "original-archive")]
                };
                File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest));
            }
        }
        var profile = Path.Combine(DirectoryFor(), "Custom.json");
        var source = JsonNode.Parse(File.ReadAllText(profile))!;
        source["libraries"]!.AsArray().Add(JsonNode.Parse("""
            {"name":"custom:launch:1","downloads":{"artifact":{"path":"wmcl-packs/Custom/launch.jar"}}}
            """));
        File.WriteAllText(profile, source.ToJsonString());
        var library = Path.Combine(_root, "libraries/wmcl-packs/Custom/launch.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(library)!);
        File.WriteAllText(library, "original private library");
        var before = Snapshot();
        var completedNotifications = 0;
        await Assert.ThrowsAnyAsync<Exception>(() => Service(new Backend()).InstallAsync(
            Plan() with { RenameTo = "Renamed" }, (step, state, _) =>
            {
                if (step != CombinedInstallStep.Apply) return;
                if (state == CombinedInstallStepState.Completed)
                {
                    completedNotifications++;
                    Assert.True(Directory.Exists(DirectoryFor("Renamed")));
                    Assert.False(Directory.Exists(DirectoryFor()));
                    Assert.Equal("Renamed", JsonNode.Parse(File.ReadAllText(Path.Combine(DirectoryFor("Child"), "Child.json")))!["inheritsFrom"]!.GetValue<string>());
                    if (cancelled) throw new OperationCanceledException("completed notification cancelled");
                    throw new IOException("completed notification failure");
                }
                if (state is CombinedInstallStepState.Failed or CombinedInstallStepState.Cancelled && throwFailureNotification)
                    throw new IOException("failure notification failure");
            }));
        Assert.Equal(1, completedNotifications);
        Assert.False(Directory.Exists(DirectoryFor("Renamed")));
        Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
        Assert.Empty(Directory.EnumerateDirectories(_root, ".wmcl-components-*"));
        Assert.Empty(Directory.EnumerateDirectories(_root, ".wmcl-relocate-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateRenameCollisionRollsBackComponentChangesEvenIfFailureNotificationThrows(bool throwNotification)
    {
        CreateInstance();
        var before = Snapshot();
        var collision = DirectoryFor("Renamed");
        await Assert.ThrowsAnyAsync<Exception>(() => Service(new Backend()).InstallAsync(
            Plan() with { RenameTo = "Renamed" }, (step, state, _) =>
            {
                if (step != CombinedInstallStep.Apply) return;
                if (state == CombinedInstallStepState.Running) Directory.CreateDirectory(collision);
                if (state == CombinedInstallStepState.Failed && throwNotification) throw new IOException("notification failure");
            }));
        Assert.True(Directory.Exists(collision));
        Assert.Empty(Directory.EnumerateFiles(collision));
        Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
        Assert.Empty(Directory.EnumerateDirectories(_root, ".wmcl-components-*"));
    }

    [Fact]
    public async Task CancellationBeforePublishingComponentsAlsoPreventsRename()
    {
        CreateInstance();
        var before = Snapshot();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(new Backend()).InstallAsync(
            Plan() with { RenameTo = "Renamed" }, (step, state, _) =>
            {
                if (step == CombinedInstallStep.Apply && state == CombinedInstallStepState.Running) cancellation.Cancel();
            }, cancellation.Token));
        Assert.False(Directory.Exists(DirectoryFor("Renamed")));
        Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
    }

    [AvaloniaFact]
    public async Task ModificationRenameSynchronizesMainWindowSelectionsAndFolderFlags()
    {
        CreateInstance();
        Directory.CreateDirectory(DirectoryFor("AFirst"));
        File.WriteAllText(Path.Combine(DirectoryFor("AFirst"), "AFirst.json"),
            """{"id":"AFirst","clientVersion":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[]}""");
        var services = Services();
        services.GetFavoriteVersions(_root).Add("Custom");
        services.GetHiddenVersions(_root).Add("Custom");
        var otherRoot = Path.Combine(_root, "another-root");
        services.GetFavoriteVersions(otherRoot).Add("Custom");
        using var main = new MainWindowViewModel(services,
            loadVersions: root => Task.FromResult<IReadOnlyList<VersionItem>>(new VersionRepository(root).ListInstalled()
                .Select(version => new VersionItem { Id = version.Id, IsInstalled = true }).ToArray()),
            createInstall: (download, appServices) => ViewModel(appServices, download));
        await main.Launch.LoadAsync();
        await main.Instance.LoadAsync();
        await main.Mods.LoadAsync();
        await main.Content.LoadAsync();
        main.Launch.SelectedVersion = main.Launch.InstalledVersions.Single(version => version.Id == "Custom");
        main.VersionSelect.SelectedVersion = main.Launch.SelectedVersion;
        main.Mods.SelectedModInstance = "Custom";
        main.Content.SelectedInstance = "Custom";
        main.Instance.SelectedInstance = main.Instance.Instances.Single(instance => instance.Id == "Custom");
        main.OpenInstanceInstall();
        await main.InstanceInstallPreparationTask;
        main.Install.InstanceName = "Renamed";
        await main.Install.InstallCombinedCommand.ExecuteAsync(null);
        await main.InstanceRefreshTask;
        Assert.Equal("Renamed", main.Launch.SelectedVersion!.Id);
        Assert.Equal("Renamed", main.VersionSelect.SelectedVersion!.Id);
        Assert.Equal("Renamed", main.Instance.SelectedInstance!.Id);
        Assert.Equal("Renamed", main.Mods.SelectedModInstance);
        Assert.Equal("Renamed", main.Content.SelectedInstance);
        Assert.Equal(["Renamed"], services.GetFavoriteVersions(_root));
        Assert.Equal(["Renamed"], services.GetHiddenVersions(_root));
        Assert.Equal(["Custom"], services.GetFavoriteVersions(otherRoot));
    }

    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task ModificationWarningsReflectIsolationFirstUseAndOriginalLoaders(bool originalLoader, bool isolated, bool acknowledged)
    {
        CreateInstance(fabric: originalLoader);
        if (originalLoader)
        {
            var manifest = InstallManifestStore.Load(_root, "Custom")!;
            manifest.Components["fabric"] = "0.15.0";
            InstallManifestStore.Save(_root, "Custom", manifest);
        }
        InstanceSettingsStore.Save(DirectoryFor(), new InstanceSettings
        {
            IsolationMode = isolated ? IsolationPolicy.Isolated : IsolationPolicy.Global
        });
        var services = Services();
        services.Settings.HintModifyInstanceWarningAcknowledged = acknowledged;
        string? warning = null;
        var model = new InstallViewModel(new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions), InstallTestData.CreateMods(services), services,
            root => new Backend().ForRoot(root), InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection(),
            confirmModification: (_, message) => { warning = message; return Task.FromResult(false); });
        await model.OpenInstanceAsync(_root, "Custom");
        model.SelectedBaseLoader = "Fabric";
        model.Mods.SelectedLoader = "0.16.0";
        model.InstallFabricApi = false;
        var before = Snapshot();
        await model.InstallCombinedCommand.ExecuteAsync(null);
        Assert.NotNull(warning);
        Assert.Equal(!isolated, warning.Contains("共用同一个 Mod"));
        Assert.Equal(!acknowledged, warning.Contains("第三方修改"));
        Assert.Equal(originalLoader, warning.Contains("在修改后可能会不兼容"));
        Assert.Equal(acknowledged, services.Settings.HintModifyInstanceWarningAcknowledged);
        Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
    }

    [AvaloniaFact]
    public async Task ModificationButtonUsesTheOriginalPencilAndReturnsToTheDownloadIcon()
    {
        CreateInstance();
        var model = ViewModel(Services());
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            var button = view.FindControl<DownloadActionButton>("StartDownloadButton")!;
            button.ApplyTemplate();
            var icon = Assert.Single(button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
            var download = icon.Data;
            Assert.False(button.IsModification);
            await model.OpenInstanceAsync(_root, "Custom");
            Assert.True(button.IsModification);
            Assert.Equal("开始修改", button.Content);
            Assert.NotSame(download, icon.Data);
            Assert.Equal(18, icon.Width);
            model.ShowDownloadCatalog();
            Assert.False(button.IsModification);
            Assert.Same(download, icon.Data);
            Assert.Equal("开始下载", button.Content);
        }
        finally { window.Close(); }
    }
}
