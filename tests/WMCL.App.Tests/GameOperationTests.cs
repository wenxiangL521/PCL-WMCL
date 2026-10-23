using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Core.IO;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed class GameOperationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-game-operations-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CanonicalAliasesAndNestedRootsCannotBypassAnOperation()
    {
        Directory.CreateDirectory(_root);
        var coordinator = new GameOperationCoordinator();
        using (coordinator.Acquire(Path.Combine(_root, "."), "运行游戏 Source"))
        {
            foreach (var path in new[] { _root + Path.DirectorySeparatorChar, Path.Combine(_root, "versions"), Path.GetDirectoryName(_root)! })
            {
                var error = Assert.Throws<InvalidOperationException>(() => coordinator.Acquire(path, "删除实例"));
                Assert.Contains("运行游戏 Source", error.Message);
                Assert.Contains("删除实例", error.Message);
            }
            using var other = coordinator.Acquire(_root + "-other", "安装游戏");
            Assert.Equal("安装游戏", coordinator.GetActiveOperation(_root + "-other"));
        }
        Assert.Null(coordinator.GetActiveOperation(_root));
    }

    [Fact]
    public void LinkedParentDirectoriesUseTheSameOperationIdentity()
    {
        if (OperatingSystem.IsWindows()) return;
        var actual = Path.Combine(_root, "actual");
        Directory.CreateDirectory(Path.Combine(actual, "game"));
        var alias = Path.Combine(_root, "alias");
        Directory.CreateSymbolicLink(alias, actual);
        var coordinator = new GameOperationCoordinator();
        using var operation = coordinator.Acquire(Path.Combine(actual, "game"), "修复游戏");
        Assert.Throws<InvalidOperationException>(() => coordinator.Acquire(Path.Combine(alias, "game"), "启动游戏"));
        Assert.Equal("修复游戏", coordinator.GetActiveOperation(Path.Combine(alias, "game")));
        Directory.Delete(alias);
    }

    [Fact]
    public async Task ConcurrentRequestsHaveOneOwnerAndDisposingAnOldLeaseDoesNotReleaseANewOne()
    {
        var coordinator = new GameOperationCoordinator();
        var leases = new ConcurrentBag<IDisposable>();
        await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() =>
        {
            try { leases.Add(coordinator.Acquire(_root, "安装组件")); }
            catch (InvalidOperationException) { }
        })));
        var first = Assert.Single(leases);
        first.Dispose();
        using var next = coordinator.Acquire(_root, "运行游戏");
        first.Dispose();
        Assert.Equal("运行游戏", coordinator.GetActiveOperation(_root));
        Assert.Throws<InvalidOperationException>(() => coordinator.Acquire(_root, "删除实例"));
    }

    [AvaloniaFact]
    public async Task OtherPagesCannotMutateOrStartTheGameDirectoryHeldByALaunch()
    {
        CreateInstance();
        var services = Services();
        var secondServices = Services();
        using var instance = new InstanceViewModel(secondServices, prompt: (_, _, _) => Task.FromResult<string?>("Renamed"),
            confirm: (_, _) => Task.FromResult(true));
        instance.SelectedInstance = new VersionItem { Id = "Source", IsInstalled = true };
        var download = new DownloadViewModel(secondServices) { SelectedVersion = new VersionItem { Id = "Source", IsInstalled = true } };
        var launch = new LaunchViewModel(secondServices) { SelectedVersion = new VersionItem { Id = "Source", IsInstalled = true } };
        var games = new GameManageViewModel(secondServices) { SelectedVersion = new VersionItem { Id = "Source", IsInstalled = true } };
        var before = Snapshot();
        using (services.GameOperations.Acquire(_root, "运行游戏 Source"))
        {
            await launch.LaunchCommand.ExecuteAsync(null);
            Assert.Contains("运行游戏 Source", launch.Status);
            Assert.False(launch.IsBusy);
            await launch.InstallCommand.ExecuteAsync(null);
            Assert.Contains("运行游戏 Source", launch.Status);
            await download.VerifyCommand.ExecuteAsync(null);
            Assert.Contains("运行游戏 Source", download.Status);
            await games.RepairCommand.ExecuteAsync(null);
            Assert.Contains("运行游戏 Source", games.Status);
            await instance.RepairManifestCommand.ExecuteAsync(null);
            Assert.Contains("运行游戏 Source", instance.ManifestStatus);
            await instance.UpdateModpackContentCommand.ExecuteAsync(null);
            Assert.Contains("运行游戏 Source", instance.ManifestStatus);
            await instance.RenameInstanceCommand.ExecuteAsync(null);
            Assert.Contains("运行游戏 Source", instance.Status);
            await instance.CloneInstanceCommand.ExecuteAsync(null);
            Assert.Contains("运行游戏 Source", instance.Status);
            await instance.DeleteInstanceCommand.ExecuteAsync(null);
            Assert.Contains("运行游戏 Source", instance.Status);
            instance.SaveInstanceSettingsCommand.Execute(null);
            Assert.Contains("运行游戏 Source", instance.Status);
            instance.ResetInstanceSettingsCommand.Execute(null);
            Assert.Contains("运行游戏 Source", instance.Status);
            await instance.PrepareServerFilesCommand.ExecuteAsync(null);
            Assert.Contains("运行游戏 Source", instance.ServerFilesStatus);
            await instance.StartServerCommand.ExecuteAsync(null);
            Assert.Contains("运行游戏 Source", instance.ServerFilesStatus);
            Assert.Equal("运行游戏 Source", services.GameOperations.GetActiveOperation(_root));
            Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
        }
        Assert.Null(secondServices.GameOperations.GetActiveOperation(_root));
    }

    [AvaloniaTheory]
    [InlineData("rename")]
    [InlineData("clone")]
    [InlineData("delete")]
    public async Task ManagementRechecksAfterItsDialogIfAnotherPageStartedWork(string action)
    {
        CreateInstance();
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = Services();
        using var instance = new InstanceViewModel(services, prompt: (_, _, _) => answer.Task, confirm: (_, _) => confirmation.Task);
        instance.SelectedInstance = new VersionItem { Id = "Source", IsInstalled = true };
        var command = action == "delete" ? instance.DeleteInstanceCommand : action == "clone" ? instance.CloneInstanceCommand : instance.RenameInstanceCommand;
        var pending = command.ExecuteAsync(null);
        var before = Snapshot();
        using var operation = services.GameOperations.Acquire(_root, "安装组件");
        answer.SetResult("Renamed");
        confirmation.SetResult(true);
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("安装组件", instance.Status);
        Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
    }

    [Fact]
    public async Task APreparedBatchPlanCannotWriteWhileTheGameIsRunning()
    {
        using var fixture = new ResourceBatchFixture();
        var plan = await fixture.Services.ResourceBatch.BuildAsync([fixture.Resource("shader", "shader")], "first", null);
        using (fixture.Services.GameOperations.Acquire(fixture.Root, "运行游戏 first"))
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Services.ResourceBatch.InstallAsync(plan));
            Assert.Contains("运行游戏 first", error.Message);
            Assert.Empty(fixture.BinaryRequests);
        }
        var result = await fixture.Services.ResourceBatch.InstallAsync(plan);
        Assert.Equal("installed", Assert.Single(result.Outcomes).State);
    }

    [AvaloniaFact]
    public async Task ResourcesLocalModsAndPackImportRespectTheSameRunningGameOperation()
    {
        CreateInstance();
        Directory.CreateDirectory(Path.Combine(_root, "mods"));
        File.WriteAllText(Path.Combine(_root, "mods/user.jar"), "user mod");
        var archive = Path.Combine(_root, "resource.zip");
        File.WriteAllText(archive, "resource");
        var services = Services();
        var mods = InstallTestData.CreateMods(services);
        await mods.LoadAsync();
        await mods.SelectModInstanceAsync("Source");
        mods.SelectedLocalMod = Assert.Single(mods.LocalMods);
        var content = new InstanceContentViewModel(services);
        await content.LoadAsync();
        content.ImportPath = archive;
        var packs = new ModpackViewModel(services) { LocalPath = archive };
        var before = Snapshot();
        using (services.GameOperations.Acquire(_root, "运行游戏 Source"))
        {
            mods.ToggleLocalModCommand.Execute(null);
            Assert.Contains("运行游戏 Source", mods.LocalModStatus);
            await mods.InstallFabricCommand.ExecuteAsync(null);
            Assert.Contains("运行游戏 Source", mods.Status);
            await mods.InstallForgelikeCommand.ExecuteAsync(null);
            Assert.Contains("运行游戏 Source", mods.ForgelikeStatus);
            await mods.InstallOptiFineCommand.ExecuteAsync(null);
            Assert.Contains("运行游戏 Source", mods.OptiFineStatus);
            content.ImportContentCommand.Execute(null);
            Assert.Contains("运行游戏 Source", content.Status);
            await packs.InstallLocalCommand.ExecuteAsync(null);
            Assert.Contains("运行游戏 Source", packs.Status);
            Assert.Equal(before.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
        }
        mods.ToggleLocalModCommand.Execute(null);
        Assert.True(File.Exists(Path.Combine(_root, "mods/user.jar.disabled")));
        content.ImportContentCommand.Execute(null);
        Assert.Contains("已导入", content.Status);
    }

    [AvaloniaFact]
    public async Task ServerPropertiesCannotWriteToAnOldRootAfterSwitchingDirectories()
    {
        CreateInstance();
        var directory = Path.Combine(_root, "versions/Source");
        File.WriteAllText(Path.Combine(directory, "Source.json"),
            """{"id":"Source","clientVersion":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[],"downloads":{"server":{"url":"https://example.invalid/server.jar"}}}""");
        File.WriteAllText(Path.Combine(directory, "server.jar"), "server");
        var services = Services();
        using var instance = new InstanceViewModel(services)
        {
            SelectedInstance = new VersionItem { Id = "Source", IsInstalled = true }
        };
        await instance.PrepareServerFilesCommand.ExecuteAsync(null);
        var path = Path.Combine(directory, "server.properties");
        Assert.Equal(path, instance.ServerPropertiesPath);
        var original = await File.ReadAllTextAsync(path);
        instance.ServerPropertiesText = "motd=changed\n";
        using (services.GameOperations.Acquire(_root, "运行服务端 Source"))
        {
            services.Settings.GameDirectory = _root + "-other";
            await instance.SaveServerPropertiesCommand.ExecuteAsync(null);
            Assert.Contains("游戏目录已切换", instance.ServerFilesStatus);
            Assert.Equal(original, await File.ReadAllTextAsync(path));
            services.Settings.GameDirectory = _root;
            await instance.SaveServerPropertiesCommand.ExecuteAsync(null);
            Assert.Contains("运行服务端 Source", instance.ServerFilesStatus);
            Assert.Equal(original, await File.ReadAllTextAsync(path));
        }
        await instance.SaveServerPropertiesCommand.ExecuteAsync(null);
        Assert.Equal("motd=changed\n", await File.ReadAllTextAsync(path));
        Assert.Null(services.GameOperations.GetActiveOperation(_root));
        Assert.False(Directory.Exists(_root + "-other"));
    }

    [AvaloniaFact]
    public async Task FailedLaunchReleasesTheDirectoryForOtherPages()
    {
        CreateInstance();
        File.WriteAllText(Path.Combine(_root, "versions/Source/Source.json"), """{"id":"Source","inheritsFrom":"Source"}""");
        var services = Services();
        var launch = new LaunchViewModel(services)
        {
            SelectedVersion = new VersionItem { Id = "Source", IsInstalled = true },
            IsMicrosoftAccount = true,
            IsAuthlibAccount = false
        };
        await launch.LaunchCommand.ExecuteAsync(null);
        Assert.Contains("继承出现循环", launch.Status);
        Assert.Null(services.GameOperations.GetActiveOperation(_root));
        using var next = services.GameOperations.Acquire(_root, "修复游戏");
    }

    private void CreateInstance()
    {
        var directory = Path.Combine(_root, "versions/Source");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Source.json"), """{"id":"Source","clientVersion":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[]}""");
        File.WriteAllText(Path.Combine(directory, "Source.jar"), "client");
        InstanceSettingsStore.Save(directory, new InstanceSettings { Description = "keep" });
        InstallManifestStore.Save(_root, "Source", new InstallManifest
        {
            Components = new() { ["minecraft"] = "1.20.1" },
            ModpackKind = "MCBBS",
            ModpackManifestJson = JsonSerializer.Serialize(new
            {
                manifestType = "minecraftModpack",
                manifestVersion = 1,
                addons = new[] { new { id = "game", version = "1.20.1" } },
                files = Array.Empty<object>()
            })
        });
    }

    [Fact]
    public void JavaCleanupExcludesAllRootsAndOldLeaseDisposalCannotReleaseAnotherCleanup()
    {
        var coordinator = new GameOperationCoordinator();
        var cleanup = coordinator.AcquireExclusive("清理 Java");
        Assert.Throws<InvalidOperationException>(() => coordinator.Acquire(_root, "启动游戏"));
        Assert.Throws<InvalidOperationException>(() => coordinator.Acquire(_root + "-other", "安装整合包"));
        Assert.Throws<InvalidOperationException>(() => coordinator.AcquireExclusive("再次清理"));
        Assert.Equal("清理 Java", coordinator.GetActiveOperation(_root));
        cleanup.Dispose();
        using (coordinator.AcquireExclusive("第二次清理"))
        {
            cleanup.Dispose();
            Assert.Equal("第二次清理", coordinator.GetActiveOperation(_root));
        }
        using var normal = coordinator.Acquire(_root, "启动游戏");
        Assert.Throws<InvalidOperationException>(() => coordinator.AcquireExclusive("清理 Java"));
        using var other = coordinator.Acquire(_root + "-other", "另一个游戏");
    }

    [AvaloniaTheory]
    [InlineData("启动或运行游戏")]
    [InlineData("运行服务端")]
    [InlineData("安装整合包")]
    public async Task JavaCleanupRechecksOtherRootsAfterConfirmation(string operation)
    {
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = Services();
        var settings = new SettingsViewModel(services, (_, _) => confirmation.Task);
        var cleaning = settings.CleanJavaCommand.ExecuteAsync(null);
        using var running = services.GameOperations.Acquire(_root + "-another", operation);
        confirmation.SetResult(true);
        await cleaning;
        Assert.Contains(operation, settings.JavaInstallStatus);
        Assert.Contains("清理 Java 失败", settings.JavaInstallStatus);
        Assert.Equal(operation, services.GameOperations.GetActiveOperation(_root + "-another"));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JavaCleanupCancellationOrLateInstallationDoesNotAcquireCleanup(bool lateInstall)
    {
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new SettingsViewModel(Services(), (_, _) => confirmation.Task);
        var cleaning = settings.CleanJavaCommand.ExecuteAsync(null);
        settings.IsInstallingJava = lateInstall;
        confirmation.SetResult(lateInstall);
        await cleaning;
        if (lateInstall) Assert.Contains("Java 正在安装", settings.JavaInstallStatus);
        Assert.Null(Services().GameOperations.GetActiveOperation(_root));
    }

    private AppServices Services() => new(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
    private Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_root, path), FileUtil.Sha1);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
