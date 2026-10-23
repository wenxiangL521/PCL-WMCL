using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed partial class ExistingInstanceInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-existing-install-" + Guid.NewGuid().ToString("N"));
    private string DirectoryFor(string id = "Custom") => Path.Combine(_root, "versions", id);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyingComponentsPreservesIdentityPackMetadataSettingsAndGlobalGameData(bool legacy)
    {
        CreateInstance();
        var original = File.ReadAllBytes(Path.Combine(DirectoryFor(), "Custom.json"));
        var manifestPath = InstallManifestStore.GetPath(_root, "Custom");
        var node = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        node["UnknownField"] = "keep unknown metadata";
        File.WriteAllText(manifestPath, node.ToJsonString());
        if (legacy)
        {
            var target = Path.Combine(DirectoryFor(), "PCL/install.json");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(manifestPath, target);
            manifestPath = target;
        }
        var backend = new Backend();
        await Service(backend).InstallAsync(Plan(), (_, _, _) => { });
        var version = await new VersionRepository(_root).GetVersionAsync("Custom");
        Assert.Equal("Custom", version.Id);
        Assert.Equal("1.20.1", version.BaseGameVersion);
        Assert.Equal("fabric", MinecraftVersionUtil.GetLoader(version));
        Assert.Equal("Custom.jar", version.Downloads!["client"].Path);
        Assert.Equal(_root, IsolationPolicy.GetGameDirectory(_root, "Custom", version));
        Assert.Equal("global", InstanceSettingsStore.Load(DirectoryFor()).IsolationMode);
        Assert.Equal("description", InstanceSettingsStore.Load(DirectoryFor()).Description);
        Assert.Equal("progress", File.ReadAllText(Path.Combine(_root, "saves/World/level.dat")));
        Assert.Equal("options", File.ReadAllText(Path.Combine(_root, "options.txt")));
        Assert.Equal("user mod", File.ReadAllText(Path.Combine(_root, "mods/user.jar")));
        Assert.True(File.Exists(Path.Combine(_root, "mods", InstallTestData.Api().Files[0].Filename)));
        Assert.False(Directory.Exists(Path.Combine(_root, "versions/1.20.1")));
        var updated = InstallManifestStore.Load(_root, "Custom")!;
        Assert.Equal("Mcbbs", updated.Components["local-pack"]);
        Assert.Equal("original.zip", updated.SourceArchive);
        Assert.Equal("MCBBS", updated.ModpackKind);
        Assert.Equal("keep unknown metadata", JsonNode.Parse(File.ReadAllText(manifestPath))!["UnknownField"]!.GetValue<string>());
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "Custom")).IsHealthy);
        var backup = Directory.EnumerateFiles(Path.Combine(DirectoryFor(), "WMCL/install-backups"), "Custom.json", SearchOption.AllDirectories).Single();
        Assert.Equal(original, File.ReadAllBytes(backup));
        Assert.Empty(Directory.EnumerateDirectories(_root, ".wmcl-components-*"));
    }

    [Fact]
    public async Task RemovingLoaderDeletesOnlyUnmodifiedManagedAddonsAndKeepsIsolatedData()
    {
        CreateInstance(fabric: true);
        var mods = Path.Combine(DirectoryFor(), "mods");
        Directory.CreateDirectory(mods);
        var api = Path.Combine(mods, "old-api.jar");
        File.WriteAllText(api, "old api");
        File.WriteAllText(Path.Combine(mods, "pack.jar"), "pack mod");
        var manifest = InstallManifestStore.Load(_root, "Custom")!;
        manifest.Components["fabric"] = "0.15.0";
        manifest.Components["fabric-api"] = "old";
        manifest.ManagedFiles.Add(new("versions/Custom/mods/old-api.jar", FileUtil.Sha1(api), "fabric-api"));
        manifest.ManagedFiles.Add(new("versions/Custom/mods/pack.jar", FileUtil.Sha1(Path.Combine(mods, "pack.jar")), "MCBBS-file"));
        InstallManifestStore.Save(_root, "Custom", manifest);
        await Service(new Backend()).InstallAsync(Plan("无", false), (_, _, _) => { });
        Assert.False(File.Exists(api));
        Assert.Equal("pack mod", File.ReadAllText(Path.Combine(mods, "pack.jar")));
        var version = await new VersionRepository(_root).GetVersionAsync("Custom");
        Assert.Equal("vanilla", MinecraftVersionUtil.GetLoader(version));
        Assert.Equal(DirectoryFor(), IsolationPolicy.GetGameDirectory(_root, "Custom", version));
        Assert.DoesNotContain("fabric", InstallManifestStore.Load(_root, "Custom")!.Components.Keys);
        Assert.DoesNotContain("fabric-api", InstallManifestStore.Load(_root, "Custom")!.Components.Keys);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "Custom")).IsHealthy);
    }

    [Theory]
    [InlineData("addon")]
    [InlineData("cancel")]
    [InlineData("modified-addon")]
    [InlineData("unmanaged-addon")]
    [InlineData("shared-library")]
    [InlineData("apply-failure")]
    public async Task FailedOrCancelledChangesPreserveTheOriginalInstance(string failure)
    {
        CreateInstance();
        if (failure == "modified-addon")
        {
            var manifest = InstallManifestStore.Load(_root, "Custom")!;
            manifest.ManagedFiles.Add(new("mods/user.jar", new string('a', 40), "fabric-api"));
            InstallManifestStore.Save(_root, "Custom", manifest);
        }
        if (failure == "shared-library")
        {
            Directory.CreateDirectory(Path.Combine(_root, "libraries"));
            File.WriteAllText(Path.Combine(_root, "libraries/generated.jar"), "different library");
        }
        if (failure == "unmanaged-addon")
            File.WriteAllText(Path.Combine(_root, "mods", InstallTestData.Api().Files[0].Filename), "user component");
        if (failure == "apply-failure")
        {
            File.Delete(InstanceSettingsStore.PathFor(DirectoryFor()));
            Directory.CreateDirectory(InstanceSettingsStore.PathFor(DirectoryFor()));
        }
        var before = Snapshot();
        using var cancellation = new CancellationTokenSource();
        var backend = new Backend { FailAddon = failure == "addon", Gate = failure == "cancel" ? new() : null };
        var operation = Service(backend).InstallAsync(Plan(), (_, _, _) => { }, cancellation.Token);
        if (failure == "cancel")
        {
            await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
        }
        await Assert.ThrowsAnyAsync<Exception>(() => operation);
        Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
        Assert.Empty(Directory.EnumerateDirectories(_root, ".wmcl-components-*"));
    }

    [Fact]
    public async Task RenamingTheInstanceDuringStagingDoesNotRecreateItsOldDirectory()
    {
        CreateInstance();
        var backend = new Backend { Gate = new() };
        var operation = Service(backend).InstallAsync(Plan(), (_, _, _) => { });
        await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        InstanceManager.Rename(_root, "Custom", "Renamed");
        backend.Gate.SetResult();
        await Assert.ThrowsAnyAsync<IOException>(() => operation);
        Assert.False(Directory.Exists(DirectoryFor()));
        Assert.True(File.Exists(Path.Combine(DirectoryFor("Renamed"), "Renamed.json")));
        Assert.False(File.Exists(Path.Combine(_root, "libraries/generated.jar")));
    }

    [Fact]
    public async Task OverridesRepairDoesNotRestoreOldCoreOrRemovedComponents()
    {
        CreateInstance();
        var api = Path.Combine(_root, "mods/old-api.jar");
        File.WriteAllText(api, "old api");
        var original = InstallManifestStore.Load(_root, "Custom")!;
        original.SourceArchive = Path.Combine(_root, "original.zip");
        original.OverridesRoot = _root;
        original.ManagedFiles.Add(new("mods/old-api.jar", FileUtil.Sha1(api), "fabric-api"));
        using (var archive = ZipFile.Open(original.SourceArchive, ZipArchiveMode.Create))
        {
            foreach (var path in new[] { "versions/Custom/Custom.json", "mods/old-api.jar", "versions/Custom/WMCL/instance.json", "versions/Custom/WMCL/install.json", "config/example.txt" })
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write("old content");
            }
        }
        InstallManifestStore.Save(_root, "Custom", original);
        await Service(new Backend()).InstallAsync(Plan(), (_, _, _) => { });
        var after = Snapshot();
        Assert.Equal(1, await OverridesRepair.RepairAsync(_root, "Custom"));
        Assert.False(File.Exists(api));
        foreach (var path in new[] { "versions/Custom/Custom.json", "versions/Custom/WMCL/instance.json", "versions/Custom/WMCL/install.json" })
            Assert.Equal(after[path], FileUtil.Sha1(Path.Combine(_root, path)));
        Assert.Equal("old content", File.ReadAllText(Path.Combine(_root, "config/example.txt")));
    }

    [Fact]
    public async Task ResetRemovesObsoleteManagedNativesAndPreservesUnmanagedFiles()
    {
        CreateInstance();
        var natives = Path.Combine(DirectoryFor(), "natives");
        Directory.CreateDirectory(natives);
        File.WriteAllText(Path.Combine(natives, "old.dll"), "old native");
        File.WriteAllText(Path.Combine(natives, "user.txt"), "keep");
        var manifest = InstallManifestStore.Load(_root, "Custom")!;
        manifest.ManagedFiles.Add(new("versions/Custom/natives/old.dll", FileUtil.Sha1(Path.Combine(natives, "old.dll")), "merged"));
        InstallManifestStore.Save(_root, "Custom", manifest);
        await Service(new Backend()).InstallAsync(Plan(), (_, _, _) => { });
        Assert.False(File.Exists(Path.Combine(natives, "old.dll")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(natives, "user.txt")));
        Assert.DoesNotContain(InstallManifestStore.Load(_root, "Custom")!.ManagedFiles, file => file.RelativePath.EndsWith("old.dll"));
    }

    [AvaloniaFact]
    public async Task ChoosingAnotherMinecraftVersionKeepsTheExistingTargetUntilDownloadNavigation()
    {
        CreateInstance();
        var services = Services();
        var download = new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions);
        download.Versions.Add(new VersionItem { Id = "1.21.1" });
        var install = ViewModel(services, download);
        await install.OpenInstanceAsync(_root, "Custom");
        await install.OpenVersionCatalogCommand.ExecuteAsync(null);
        install.VersionCatalog.Groups.SelectMany(group => group.Entries).First(entry => entry.Version.Id == "1.21.1").SelectCommand.Execute(null);
        Assert.Equal("Custom", install.TargetInstanceId);
        Assert.Equal("1.21.1", download.SelectedVersion!.Id);
        Assert.Equal("1.21.1", install.Mods.SelectedGameVersion);
        Assert.Equal("开始修改", install.InstallActionText);
        install.ShowDownloadCatalog();
        Assert.False(install.IsExistingInstance);
        Assert.True(install.IsChoosingVersion);
        Assert.Equal("开始下载", install.InstallActionText);
    }

    [AvaloniaFact]
    public async Task CustomInstanceUsesItsMinecraftIdentityAndRetainsItAcrossCatalogRefreshAndRename()
    {
        CreateInstance();
        var services = Services();
        var download = new DownloadViewModel(services, () => Task.FromResult<IReadOnlyList<VersionItem>>([
            new() { Id = "1.21.1", IsLatest = true }, new() { Id = "1.20.1" }
        ]));
        var install = ViewModel(services, download);
        await install.OpenInstanceAsync(_root, "Custom");
        Assert.True(install.IsExistingInstance);
        Assert.Equal("Custom", install.TargetInstanceId);
        Assert.Equal("Custom", install.InstanceName);
        Assert.Equal("1.20.1", download.SelectedVersion!.Id);
        Assert.Equal("1.20.1", install.Mods.SelectedGameVersion);
        Assert.False(download.SelectedVersion.IsInstalled);
        Assert.Equal("开始修改", install.InstallActionText);
        await download.LoadAsync();
        Assert.Equal("1.20.1", download.SelectedVersion!.Id);
        install.ApplyInstanceChange(new(_root, "Custom", "Renamed", InstanceChangeKind.Renamed));
        Assert.Equal("Renamed", install.TargetInstanceId);
        Assert.Equal("Renamed", install.InstanceName);
        Assert.Equal("1.20.1", download.SelectedVersion!.Id);
        Assert.Equal("1.20.1", install.Mods.SelectedGameVersion);
        install.ApplyInstanceChange(new(_root, "Renamed", null, InstanceChangeKind.Deleted));
        Assert.False(install.IsExistingInstance);
        Assert.Null(download.SelectedVersion);
        Assert.False(install.CanApplyInstall);
        Assert.True(install.IsChoosingVersion);
    }

    [AvaloniaFact]
    public async Task InstalledOfficialVersionDoesNotFollowAnUnrelatedLocalRename()
    {
        var services = Services();
        var download = new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions) { SelectedVersion = new VersionItem { Id = "1.20.1", IsInstalled = true } };
        var install = ViewModel(services, download);
        install.ShowSelectedVersion();
        install.ApplyInstanceChange(new(_root, "1.20.1", "Renamed", InstanceChangeKind.Renamed));
        Assert.Equal("1.20.1", download.SelectedVersion.Id);
        Assert.False(install.IsExistingInstance);
    }

    [AvaloniaFact]
    public async Task UnknownCustomIdentityIsNotSentToComponentMetadataAsMinecraft()
    {
        CreateInstance();
        File.WriteAllText(Path.Combine(DirectoryFor(), "Custom.json"),
            """{"id":"Custom","clientVersion":"Custom","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[]}""");
        var services = Services();
        var download = new DownloadViewModel(services, () => Task.FromResult<IReadOnlyList<VersionItem>>([new() { Id = "1.20.1" }]));
        await download.LoadAsync();
        var install = ViewModel(services, download);
        await install.OpenInstanceAsync(_root, "Custom");
        Assert.Contains("无法识别", install.CombinedStatus);
        Assert.Null(download.SelectedVersion);
        Assert.False(install.CanApplyInstall);
        Assert.NotEqual("Custom", install.Mods.SelectedGameVersion);
    }

    [AvaloniaTheory]
    [InlineData("rename", "Renamed")]
    [InlineData("clone", "Custom")]
    [InlineData("delete", null)]
    public async Task MainWindowRoutesTheInstanceAndManagementEventToTheInstallationTarget(string action, string? expected)
    {
        CreateInstance(fabric: true);
        using var main = new MainWindowViewModel(Services(),
            loadVersions: root => Task.FromResult<IReadOnlyList<VersionItem>>(new VersionRepository(root).ListInstalled()
                .Select(version => new VersionItem { Id = version.Id, IsInstalled = true }).ToArray()),
            instancePrompt: (_, _, _) => Task.FromResult<string?>("Renamed"), instanceConfirm: (_, _) => Task.FromResult(true),
            createInstall: (download, services) => ViewModel(services, download));
        main.Instance.SelectedInstance = new VersionItem { Id = "Custom", IsInstalled = true };
        main.OpenInstanceInstall();
        await main.InstanceInstallPreparationTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(main.Install, main.CurrentPage);
        Assert.Equal("Custom", main.Install.TargetInstanceId);
        Assert.Equal("1.20.1", main.Download.SelectedVersion!.Id);
        var command = action switch
        {
            "clone" => main.Instance.CloneInstanceCommand,
            "delete" => main.Instance.DeleteInstanceCommand,
            _ => main.Instance.RenameInstanceCommand
        };
        await command.ExecuteAsync(null);
        await main.InstanceRefreshTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(expected, main.Install.TargetInstanceId);
        if (expected is not null) Assert.Equal("1.20.1", main.Download.SelectedVersion!.Id);
        else Assert.Null(main.Download.SelectedVersion);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveInstallUsesItsFixedTargetAndUpdatesThePageAfterwards(bool changeRoot)
    {
        CreateInstance();
        var services = Services();
        var backend = new Backend { Gate = new() };
        var install = ViewModel(services, backend: backend);
        await install.OpenInstanceAsync(_root, "Custom");
        TaskEntry? terminal = null;
        install.TaskFinished += (_, task) => terminal = task;
        install.SelectedBaseLoader = "Fabric";
        install.InstallFabricApi = false;
        var operation = install.InstallCombinedCommand.ExecuteAsync(null);
        await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(install.IsCombinedBusy);
        Assert.Equal("应用安装更改", install.Stages.Last().Title);
        Assert.False(install.Stages.Last().IsDone);
        if (changeRoot) services.SetGameDirectory(Path.Combine(_root, "next"));
        else
        {
            InstanceManager.Rename(_root, "Custom", "Renamed");
            install.ApplyInstanceChange(new(_root, "Custom", "Renamed", InstanceChangeKind.Renamed));
        }
        Assert.Equal("Custom", install.TargetInstanceId);
        backend.Gate.SetResult();
        await operation.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(terminal);
        Assert.Equal("安装 Custom", terminal.Title);
        Assert.Equal(changeRoot ? "完成" : "失败", terminal.PercentText);
        if (changeRoot) Assert.Contains("已应用安装更改：Custom", terminal.Detail);
        if (changeRoot)
        {
            Assert.True(install.Stages.Last().IsDone);
            Assert.False(install.IsExistingInstance);
            Assert.True(install.IsChoosingVersion);
            Assert.Equal("fabric", MinecraftVersionUtil.GetLoader(await new VersionRepository(_root).GetVersionAsync("Custom")));
            Assert.False(Directory.Exists(Path.Combine(_root, "next/versions")));
        }
        else
        {
            Assert.True(install.Stages.Last().IsFailed);
            Assert.False(install.Stages.Last().IsDone);
            Assert.True(install.CombinedProgress < 100);
            Assert.Equal("Renamed", install.TargetInstanceId);
            Assert.Equal("1.20.1", install.Download.SelectedVersion!.Id);
            Assert.Contains("失败", install.CombinedStatus);
            Assert.False(Directory.Exists(DirectoryFor()));
        }
    }

    [AvaloniaFact]
    public async Task UnchangedModificationCannotSubmitAndExistingSurfaceKeepsPclControls()
    {
        CreateInstance();
        var backend = new Backend();
        var install = ViewModel(Services(), backend: backend, confirm: false);
        var terminalCount = 0;
        install.TaskFinished += (_, _) => terminalCount++;
        await install.OpenInstanceAsync(_root, "Custom");
        var before = Snapshot();
        var view = new InstallView { DataContext = install };
        var window = new Window { Content = view, Width = 1000, Height = 800 };
        window.Show();
        try
        {
            for (var frame = 0; frame < 40; frame++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(10);
            }
            var action = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "开始修改"));
            Assert.False(action.IsEffectivelyEnabled);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("修改实例：Custom") == true);
            Assert.False(view.FindControl<Button>("BackToCatalogButton")!.IsVisible);
            Assert.False(view.FindControl<TextBlock>("OriginalInstanceNameLabel")!.IsVisible);
            var name = view.FindControl<TextBox>("InstallNameBox")!;
            Assert.False(name.IsReadOnly);
            Assert.Equal("Custom", name.Text);
            await install.InstallCombinedCommand.ExecuteAsync(null);
            Assert.Equal(0, backend.VanillaCalls);
            Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
            Assert.False(install.IsCombinedBusy);
            Assert.False(install.CanApplyInstall);
            Assert.Equal(0, terminalCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ModificationDialogUsesTheOriginalActionAndCancellingKeepsTheInstance()
    {
        CreateInstance();
        var backend = new Backend();
        var services = Services();
        var owner = new Window { Width = 600, Height = 400 };
        var install = new InstallViewModel(new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions), InstallTestData.CreateMods(services), services,
            backend.ForRoot, InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection(),
            (title, message) => DialogService.ConfirmAsync(owner, title, message, confirmText: "开始修改"));
        await install.OpenInstanceAsync(_root, "Custom");
        install.InstanceName = "Renamed";
        var before = Snapshot();
        owner.Show();
        try
        {
            var operation = install.InstallCombinedCommand.ExecuteAsync(null);
            await Task.Delay(30);
            var dialog = Assert.IsType<ConfirmWindow>(Assert.Single(owner.OwnedWindows));
            Assert.Equal("修改确认", dialog.Title);
            var confirm = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "开始修改"));
            Assert.Contains("primary", confirm.Classes);
            Assert.DoesNotContain("danger", confirm.Classes);
            Assert.DoesNotContain(dialog.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "确定删除"));
            var cancel = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "取消"));
            cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await operation;
            Assert.Empty(owner.OwnedWindows);
            Assert.Equal(0, backend.VanillaCalls);
            Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
            Assert.True(install.CanApplyInstall);
            Assert.False(services.Settings.HintModifyInstanceWarningAcknowledged);
        }
        finally
        {
            foreach (var dialog in owner.OwnedWindows.ToArray()) dialog.Close(false);
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task StagedComponentInstallBlocksOtherPagesUntilItCompletes()
    {
        CreateInstance();
        var services = Services();
        var backend = new Backend { Gate = new() };
        var install = ViewModel(services, backend: backend);
        await install.OpenInstanceAsync(_root, "Custom");
        install.SelectedBaseLoader = "Fabric";
        install.InstallFabricApi = false;
        var pending = install.InstallCombinedCommand.ExecuteAsync(null);
        await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            using var instance = new InstanceViewModel(services, prompt: (_, _, _) => Task.FromResult<string?>("Renamed"),
                confirm: (_, _) => Task.FromResult(true));
            instance.SelectedInstance = new VersionItem { Id = "Custom", IsInstalled = true };
            await instance.RenameInstanceCommand.ExecuteAsync(null);
            Assert.Contains("安装组件", instance.Status);
            await instance.RepairManifestCommand.ExecuteAsync(null);
            Assert.Contains("安装组件", instance.ManifestStatus);
            var launch = new LaunchViewModel(services) { SelectedVersion = new VersionItem { Id = "Custom", IsInstalled = true } };
            await launch.LaunchCommand.ExecuteAsync(null);
            Assert.Contains("安装组件", launch.Status);
            Assert.True(File.Exists(Path.Combine(DirectoryFor(), "Custom.json")));
            Assert.False(Directory.Exists(DirectoryFor("Renamed")));
        }
        finally
        {
            backend.Gate.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Contains("已应用安装更改", install.CombinedStatus);
        Assert.Null(services.GameOperations.GetActiveOperation(_root));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingSubmissionReturnsToPreviousPageOnlyAfterAcceptance(bool rejected)
    {
        CreateInstance();
        var services = Services();
        var backend = new Backend { Gate = new() };
        using var main = new MainWindowViewModel(services,
            loadVersions: _ => Task.FromResult<IReadOnlyList<VersionItem>>([]),
            createInstall: (download, appServices) => ViewModel(appServices, download, backend));
        var previousPage = main.CurrentPage;
        main.Instance.SelectedInstance = new VersionItem { Id = "Custom", IsInstalled = true };
        main.OpenInstanceInstall();
        await main.InstanceInstallPreparationTask.WaitAsync(TimeSpan.FromSeconds(5));
        main.Install.SelectedBaseLoader = "Fabric";
        main.Install.InstallFabricApi = false;
        using var blockingOperation = rejected ? services.GameOperations.Acquire(_root, "test active game") : null;
        var operation = main.Install.InstallCombinedCommand.ExecuteAsync(null);
        try
        {
            if (rejected)
            {
                await operation;
                Assert.Same(main.Install, main.CurrentPage);
                Assert.Equal(0, backend.VanillaCalls);
                Assert.Contains("test active game", Assert.Single(main.Tasks.History,
                    task => task.Title == "安装 Custom").Detail);
            }
            else
            {
                await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Same(previousPage, main.CurrentPage);
                Assert.True(main.Install.IsCombinedBusy);
                Assert.Contains(main.Tasks.Tasks, task => task.Title == "组合安装" && task.HasCancel);
            }
        }
        finally
        {
            backend.Gate.TrySetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private void CreateInstance(bool fabric = false)
    {
        Directory.CreateDirectory(DirectoryFor());
        File.WriteAllText(Path.Combine(DirectoryFor(), "Custom.json"),
            $$"""{"id":"Custom","clientVersion":"1.20.1","mainClass":"{{(fabric ? "net.fabricmc.loader.impl.launch.knot.KnotClient" : "net.minecraft.client.main.Main")}}","libraries":[{{(fabric ? "{\"name\":\"net.fabricmc:fabric-loader:0.15.0\"}" : "")}}]}""");
        File.WriteAllText(Path.Combine(DirectoryFor(), "Custom.jar"), "original client");
        InstanceSettingsStore.Save(DirectoryFor(), new InstanceSettings { Description = "description" });
        Directory.CreateDirectory(Path.Combine(_root, "saves/World"));
        Directory.CreateDirectory(Path.Combine(_root, "mods"));
        File.WriteAllText(Path.Combine(_root, "saves/World/level.dat"), "progress");
        File.WriteAllText(Path.Combine(_root, "options.txt"), "options");
        File.WriteAllText(Path.Combine(_root, "mods/user.jar"), "user mod");
        InstallManifestStore.Save(_root, "Custom", new InstallManifest
        {
            Components = new() { ["minecraft"] = "1.20.1", ["local-pack"] = "Mcbbs" },
            SourceArchive = "original.zip",
            ModpackKind = "MCBBS",
            ManagedFiles = [new("versions/Custom/WMCL/instance.json", FileUtil.Sha1(InstanceSettingsStore.PathFor(DirectoryFor())), "settings", "https://example.invalid/old-settings")]
        });
    }

    private AppServices Services() => new(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
    private InstallViewModel ViewModel(AppServices services, DownloadViewModel? download = null, Backend? backend = null, bool confirm = true) =>
        new(download ?? new DownloadViewModel(services, InstallTestData.LoadMinecraftVersions), InstallTestData.CreateMods(services), services,
            root => (backend ?? new Backend()).ForRoot(root), InstallTestData.CreateOptiFabricSelection(), InstallTestData.CreateApiSelection(),
            (_, _) => Task.FromResult(confirm));
    private CombinedInstallPlan Plan(string loader = "Fabric", bool api = true) => CombinedInstallPlan.Create(
        _root, "1.20.1", false, loader, "0.16.0", null, api, false, "", apiVersion: InstallTestData.Api(), existingInstanceId: "Custom");
    private ExistingInstanceInstallService Service(Backend backend) => new(_root, IsolationPolicy.Auto, backend.ForRoot);
    private Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_root, path), FileUtil.Sha1);

    private sealed class Backend : ICombinedInstallBackend
    {
        private string _root = "";
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Gate { get; init; }
        public bool FailAddon { get; init; }
        public int VanillaCalls { get; private set; }
        public ICombinedInstallBackend ForRoot(string root) { _root = root; return this; }
        public async Task InstallVanillaAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct)
        {
            VanillaCalls++;
            Entered.TrySetResult();
            if (Gate is not null) await Gate.Task.WaitAsync(ct);
            var directory = Path.Combine(_root, "versions", plan.Minecraft);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, plan.Minecraft + ".json"),
                JsonSerializer.Serialize(new
                {
                    id = plan.Minecraft,
                    clientVersion = plan.Minecraft,
                    mainClass = "net.minecraft.client.main.Main",
                    downloads = new { client = new { path = plan.Minecraft + ".jar" } },
                    libraries = Array.Empty<object>()
                }), ct);
            await File.WriteAllTextAsync(Path.Combine(directory, plan.Minecraft + ".jar"), "new client", ct);
        }
        public Task<string> InstallLoaderAsync(CombinedInstallPlan plan, string stagingRoot, IProgress<JobProgress> progress, CancellationToken ct)
        {
            const string id = "generated-loader";
            var directory = Path.Combine(stagingRoot, "versions", id);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, id + ".json"),
                $$"""{"id":"{{id}}","inheritsFrom":"{{plan.Minecraft}}","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[]}""");
            Directory.CreateDirectory(Path.Combine(stagingRoot, "libraries"));
            File.WriteAllText(Path.Combine(stagingRoot, "libraries/generated.jar"), "new library");
            return Task.FromResult(id);
        }
        public Task CompleteInstanceAsync(string instanceId, IProgress<JobProgress> progress, CancellationToken ct) => Task.CompletedTask;
        public Task<string> DownloadOptiFineAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> InstallModAsync(string project, CombinedInstallPlan plan, string modsDirectory, IProgress<JobProgress> progress, CancellationToken ct)
        {
            if (FailAddon) throw new IOException("addon failure");
            Directory.CreateDirectory(modsDirectory);
            var filename = plan.ApiVersion!.Files[0].Filename;
            File.WriteAllText(Path.Combine(modsDirectory, filename), "new api");
            return Task.FromResult<IReadOnlyList<string>>([filename]);
        }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
