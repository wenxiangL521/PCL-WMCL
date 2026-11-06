using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Modding.Fabric;
using WMCL.Modding.Forgelike;
using WMCL.Modding.Modrinth;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed partial class ExistingInstanceInstallTests
{
    private InstallViewModel CatalogModel(AppServices services, Func<string, bool> fail,
        Func<Task<IReadOnlyList<OptiFineVersion>>>? optifine = null,
        Func<CancellationToken, Task<IReadOnlyList<OptiFabricFile>>>? optifabric = null,
        string error = "catalog unavailable",
        DownloadViewModel? download = null)
    {
        var mods = new ModsViewModel(services,
            (_, _) => fail("Fabric") ? Task.FromException<IReadOnlyList<FabricLoaderInfo>>(new IOException(error))
                : Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([new("0.16.0", true)]),
            (kind, _) => fail(kind == ForgelikeKind.Forge ? "Forge" : "NeoForge")
                ? Task.FromException<IReadOnlyList<string>>(new IOException(error))
                : Task.FromResult<IReadOnlyList<string>>(["47.2.0"]),
            _ => fail("OptiFine") ? Task.FromException<IReadOnlyList<OptiFineVersion>>(new IOException(error))
                : optifine?.Invoke() ?? Task.FromResult<IReadOnlyList<OptiFineVersion>>([InstallTestData.OptiFine()]));
        var api = new ApiSelectionViewModel((_, _, _) => fail("Fabric API")
            ? Task.FromException<IReadOnlyList<ModrinthVersion>>(new IOException(error))
            : Task.FromResult<IReadOnlyList<ModrinthVersion>>([InstallTestData.Api()]));
        var opti = new OptiFabricSelectionViewModel((_, token) => fail("OptiFabric")
            ? Task.FromException<IReadOnlyList<OptiFabricFile>>(new IOException(error))
            : optifabric?.Invoke(token) ?? Task.FromResult<IReadOnlyList<OptiFabricFile>>([InstallTestData.OptiFabric()]));
        return new(download ?? new DownloadViewModel(services, () => fail("Minecraft")
            ? Task.FromException<IReadOnlyList<VersionItem>>(new IOException(error))
            : InstallTestData.LoadMinecraftVersions()), mods, services, null, opti, api);
    }

    [AvaloniaTheory]
    [InlineData("Minecraft")]
    // 新契约：组件（Forge/NeoForge/Fabric/...）失败不再终止初始化，只有基座列表失败才终止
    public async Task MinecraftCatalogFailureStopsInitializationAndRetryReloadsIt(string failedCatalog)
    {
        CreateInstance();
        var original = Snapshot();
        var fail = true;
        var calls = 0;
        var model = CatalogModel(Services(), name =>
        {
            if (name != failedCatalog) return false;
            calls++;
            return fail;
        });
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.True(model.HasInstanceInitializationError);
        Assert.Contains(failedCatalog, model.InstanceInitializationError);
        Assert.Contains("catalog unavailable", model.InstanceInitializationError);
        Assert.False(model.IsExistingInstance);
        Assert.False(model.CanApplyInstall);
        Assert.False(model.ShowInstallAction);
        Assert.Equal(original.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
        var failedCalls = calls;
        fail = false;
        await model.RetryInstanceInitializationCommand.ExecuteAsync(null);
        Assert.True(calls > failedCalls);
        Assert.True(model.IsExistingInstance);
        Assert.False(model.HasInstanceInitializationError);
        Assert.Equal(1, model.InstanceInitializationProgress);
        Assert.Equal(original.OrderBy(pair => pair.Key), Snapshot().OrderBy(pair => pair.Key));
    }

    [AvaloniaFact]
    public async Task AFailureInTheSelectedComponentCatalogCannotBeHiddenBySuccessfulCardCatalogs()
    {
        CreateInstance();
        var calls = 0;
        var model = CatalogModel(Services(), _ => false, optifine: () => ++calls == 1
            ? Task.FromResult<IReadOnlyList<OptiFineVersion>>([InstallTestData.OptiFine()])
            : Task.FromException<IReadOnlyList<OptiFineVersion>>(new IOException("selected catalog failed")));
        await model.OpenInstanceAsync(_root, "Custom");
        // 新契约：组件目录失败不阻断初始化，页面进入修改态并保留卡片错误
        Assert.False(model.HasInstanceInitializationError);
        Assert.True(model.IsExistingInstance);
    }

    [AvaloniaFact]
    public async Task ComponentFailureWaitsForOtherPendingCatalogsBeforeCompleting()
    {
        CreateInstance();
        var gate = new TaskCompletionSource<IReadOnlyList<OptiFineVersion>>();
        var model = CatalogModel(Services(), name => name == "NeoForge", optifine: () => gate.Task);
        try
        {
            var opening = model.OpenInstanceAsync(_root, "Custom");
            await Task.Delay(30);
            Assert.False(opening.IsCompleted);   // 仍在等待 OptiFine
            gate.SetResult([InstallTestData.OptiFine()]);
            await opening.WaitAsync(TimeSpan.FromSeconds(2));
            // 组件失败不阻断：初始化完成，仅保留卡片错误
            Assert.False(model.HasInstanceInitializationError);
            Assert.True(model.IsExistingInstance);
            Assert.False(model.IsPreparingInstance);
            Assert.Equal(1, model.InstanceInitializationProgress);
        }
        finally { gate.TrySetResult([]); }
    }

    [AvaloniaTheory]
    [InlineData("Forge")]
    [InlineData("NeoForge")]
    [InlineData("OptiFine")]
    public async Task ComponentNoVersionSentinelIsNotAnInitializationFailure(string catalog)
    {
        CreateInstance();
        var model = CatalogModel(Services(), name => name == catalog, error: "无");
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.True(model.IsExistingInstance);
        Assert.False(model.HasInstanceInitializationError);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProgressReflectsCompletedCatalogsAndAnAbandonedTargetCannotPublishCompletion(bool changeRoot)
    {
        CreateInstance();
        var services = Services();
        var gate = new TaskCompletionSource<IReadOnlyList<OptiFabricFile>>();
        var model = CatalogModel(services, _ => false, optifabric: _ => gate.Task);
        var opening = model.OpenInstanceAsync(_root, "Custom");
        var view = new InstallView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            var until = DateTime.UtcNow.AddSeconds(2);
            while (model.InstanceInitializationProgress == 0 && DateTime.UtcNow < until) await Task.Delay(10);
            Assert.True(model.IsPreparingInstance);
            Assert.InRange(model.InstanceInitializationProgress, 0.01, 0.69);
            var indicator = Assert.IsType<PclLoadingIndicator>(view.FindControl<Border>("InstanceInitializationPanel")!.Child);
            Assert.Equal($"正在加载 - {Math.Floor(model.InstanceInitializationProgress * 100)}%",
                indicator.FindControl<TextBlock>("LoadingText")!.Text);
            if (changeRoot) services.SetGameDirectory(Path.Combine(_root, "other"));
            gate.SetResult([InstallTestData.OptiFabric()]);
            await opening;
            Assert.Equal(changeRoot ? 0 : 1, model.InstanceInitializationProgress);
            Assert.Equal(!changeRoot, model.IsExistingInstance);
        }
        finally
        {
            gate.TrySetResult([]);
            window.Close();
        }
    }
}
