using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed class InstalledVersionIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "wmcl-version-identity-" + Guid.NewGuid().ToString("N"));

    [AvaloniaFact]
    public async Task MergedLoaderFlagsProduceInstallableModCardAndPclDescription()
    {
        await WriteAsync("Multi", """
        {
          "id": "Multi",
          "type": "release",
          "releaseTime": "2024-02-01T00:00:00Z",
          "clientVersion": "1.20.1",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "libraries": [
            { "name": "net.fabricmc:fabric-loader:0.15.11" },
            { "name": "net.minecraftforge:forge:1.20.1-47.2.0" },
            { "name": "optifine:OptiFine:1.20.1_HD_U_I6" }
          ]
        }
        """);

        var item = Assert.Single(await LoadAsync());

        Assert.Equal("fabric", item.Category);
        Assert.Equal(VersionLoaderKind.Fabric | VersionLoaderKind.Forge, item.Loaders);
        Assert.Equal(2, item.LoaderKindCount);
        Assert.Equal("0.15.11", item.FabricVersion);
        Assert.Equal("47.2.0", item.ForgeVersion);
        Assert.True(item.EffectiveHasOptiFine);
        Assert.Equal("I6", item.OptiFineVersion);
        Assert.True(item.IsBaseGameVersionReliable);
        Assert.Equal("正式版 1.20.1, Forge 47.2.0, Fabric 0.15.11, OptiFine I6",
            item.VersionDisplayText);

        var model = CreateModel(item);
        var group = Assert.Single(model.Groups);
        Assert.Equal("可安装 Mod", group.Title);
    }

    [AvaloniaFact]
    public async Task CleanroomAndLabyModInstancesAreRecognizedAsLoaderCards()
    {
        await WriteAsync("Cleanroom", """
        {
          "id": "Cleanroom",
          "type": "release",
          "releaseTime": "2026-10-19T00:00:00Z",
          "clientVersion": "1.12.2",
          "mainClass": "top.outlands.foundation.boot.Foundation",
          "libraries": [
            { "name": "com.cleanroommc:cleanroom:0.7.11-alpha:universal" }
          ]
        }
        """);
        await WriteAsync("LabyMod", """
        {
          "id": "LabyMod",
          "type": "release",
          "releaseTime": "2026-10-19T00:00:00Z",
          "clientVersion": "1.21.1",
          "mainClass": "net.labymod.launcher.Main",
          "labymod_data": { "version": "47.4" },
          "libraries": []
        }
        """);

        var items = (await LoadAsync()).ToDictionary(item => item.Id);
        var cleanroom = items["Cleanroom"];
        Assert.Equal(VersionLoaderKind.Cleanroom, cleanroom.Loaders);
        Assert.Equal("0.7.11-alpha", cleanroom.CleanroomVersion);
        Assert.Equal("正式版 1.12.2, Cleanroom 0.7.11-alpha", cleanroom.VersionDisplayText);
        Assert.Equal("cleanroom", cleanroom.Category);
        var cleanroomModel = CreateModel(cleanroom);
        Assert.Equal("Cleanroom 版本", Assert.Single(cleanroomModel.Groups).Title);

        var laby = items["LabyMod"];
        Assert.Equal(VersionLoaderKind.LabyMod, laby.Loaders);
        Assert.Equal("47.4", laby.LabyModVersion);
        Assert.Equal("正式版 1.21.1, LabyMod 47.4", laby.VersionDisplayText);
        Assert.Equal("labymod", laby.Category);
        Assert.Equal("LabyMod 版本", Assert.Single(CreateModel(laby).Groups).Title);
    }

    [AvaloniaTheory]
    [InlineData("net.minecraftforge:forge:1.20.1-47.2.0", "forge", "Forge 47.2.0")]
    [InlineData("net.neoforged:neoforge:20.4.237", "neoforge", "NeoForge 20.4.237")]
    [InlineData("com.mumfrey:liteloader:1.12.2-RC1", "liteloader", "LiteLoader")]
    public async Task SingleLoaderUsesItsRealCoordinate(string coordinate, string category, string description)
    {
        await WriteAsync("Loader", $$"""
        {
          "id": "Loader",
          "type": "release",
          "releaseTime": "2024-02-01T00:00:00Z",
          "clientVersion": "1.12.2",
          "libraries": [{ "name": "{{coordinate}}" }]
        }
        """);

        var item = Assert.Single(await LoadAsync());
        Assert.Equal(category, item.Category);
        Assert.Contains(description, item.VersionDisplayText);
    }

    [AvaloniaFact]
    public async Task OldReleaseDateWinsBeforeLoaderAndUsesAncientDescription()
    {
        await WriteAsync("Ancient", """
        {
          "id": "Ancient",
          "type": "release",
          "releaseTime": "2012-06-01T00:00:00Z",
          "clientVersion": "1.2.5",
          "libraries": [{ "name": "net.minecraftforge:forge:1.2.5-3.4.9.171" }]
        }
        """);

        var item = Assert.Single(await LoadAsync());
        Assert.Equal("old", item.Category);
        Assert.True(item.IsOldVersion);
        Assert.StartsWith("远古版本", item.VersionDisplayText);
        Assert.Contains("Forge", item.VersionDisplayText);
        Assert.Equal("不常用版本", Assert.Single(CreateModel(item).Groups).Title);
    }

    [AvaloniaTheory]
    [InlineData("My-w-Pack", true, "1.20.1", true, "常规版本")]
    [InlineData("My-w-Pack", false, "My-w-Pack", false, "不常用版本")]
    public async Task CustomInstanceNameIsNotGuessedAsSnapshot(
        string id, bool hasClientVersion, string baseVersion, bool reliable, string expectedGroup)
    {
        var clientVersion = hasClientVersion ? $""", "clientVersion": "1.20.1" """ : "";
        await WriteAsync(id, $$"""
        {
          "id": "{{id}}",
          "type": "release",
          "releaseTime": "2024-02-01T00:00:00Z"{{clientVersion}}
        }
        """);

        var item = Assert.Single(await LoadAsync());
        Assert.False(item.IsSnapshotVersion);
        Assert.False(item.IsFoolVersion);
        Assert.Equal(baseVersion, item.BaseGameVersion);
        Assert.Equal(reliable, item.IsBaseGameVersionReliable);
        Assert.Contains(expectedGroup, CreateModel(item).Groups.Select(group => group.Title));
    }

    [AvaloniaFact]
    public async Task AprilFirstSnapshotUsesPclUtcPlusTwoRuleAndFoolCard()
    {
        await WriteAsync("CustomSnapshot", """
        {
          "id": "CustomSnapshot",
          "type": "snapshot",
          "releaseTime": "2024-03-31T23:30:00Z"
        }
        """);

        var item = Assert.Single(await LoadAsync());
        Assert.True(item.IsFoolVersion);
        Assert.Equal("fool", item.Category);
        Assert.Equal("愚人节版本", Assert.Single(CreateModel(item).Groups).Title);
    }

    [AvaloniaFact]
    public async Task UnknownBaseAndBrokenJsonStayInErrorCardWithoutLaunching()
    {
        await WriteAsync("Unknown", """
        {
          "id": "Unknown",
          "type": "release",
          "releaseTime": "2024-02-01T00:00:00Z"
        }
        """);
        var directory = Path.Combine(_root, "versions", "Broken");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "Broken.json"), "{ broken");

        var items = await LoadAsync();
        var unknown = items.Single(item => item.Id == "Unknown");
        var broken = items.Single(item => item.Id == "Broken");
        Assert.Equal("error", unknown.Category);
        Assert.Equal("error", broken.Category);
        Assert.Equal("PCL 无法识别该版本的 MC 版本号", unknown.VersionDisplayText);
        Assert.Equal("无法读取版本配置", broken.VersionDisplayText);
        Assert.All(items, item => Assert.Equal("错误的版本", Assert.Single(CreateModel(item).Groups).Title));
    }

    [AvaloniaFact]
    public async Task HistoricalOptiFineCoordinateAndCustomInfoAreReadWithoutExactLibraryPrefix()
    {
        await WriteAsync("OptiFineOld", """
        {
          "id": "OptiFineOld",
          "type": "release",
          "releaseTime": "2014-08-01T00:00:00Z",
          "clientVersion": "1.7.10",
          "libraries": [{ "name": "optifine:OptiFine:1.7.10_HD_U_D1" }]
        }
        """);
        var directory = Path.Combine(_root, "versions", "OptiFineOld");
        Directory.CreateDirectory(Path.Combine(directory, "WMCL"));
        var logo = Path.Combine(_root, "custom-logo.png");
        await using (var source = AssetLoader.Open(new Uri("avares://WMCL.App/Assets/Blocks/Grass.png")))
        await using (var target = File.Create(logo))
            await source.CopyToAsync(target);
        await File.WriteAllTextAsync(Path.Combine(directory, "WMCL", "instance.json"), $$"""
        {
          "description": "我的自定义描述",
          "logoPath": "{{logo.Replace("\\", "\\\\")}}",
          "versionCardType": 4
        }
        """);

        var item = Assert.Single(await LoadAsync());
        Assert.Equal("optifine", item.Category);
        Assert.Equal("D1", item.OptiFineVersion);
        Assert.Equal("正式版 1.7.10, OptiFine D1", item.VersionDisplayText);
        Assert.Equal("我的自定义描述", item.InfoText);
        Assert.Equal("  |  正式版 1.7.10, OptiFine D1", item.TitleSuffix);
        Assert.True(item.HasLogo);
        Assert.NotNull(item.IconImage);
        Assert.Equal(VersionCardPlacement.Uncommon, item.CardTypeOverride);
    }

    [AvaloniaFact]
    public async Task InstanceVersionCardTypePersistsAndReloads()
    {
        await WriteAsync("Custom", """
        {
          "id": "Custom",
          "type": "release",
          "releaseTime": "2024-01-01T00:00:00Z",
          "clientVersion": "1.20.1"
        }
        """);
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        using (var model = new InstanceViewModel(services))
        {
            var saved = 0;
            model.InstanceSettingsSaved += (_, _) => saved++;
            await model.LoadAsync();
            Assert.Equal("Custom", model.SelectedInstance?.Id);
            model.InstanceVersionCardType = "不常用版本";
            model.SaveInstanceSettingsCommand.Execute(null);
            Assert.Equal(1, saved);
            Assert.Equal(VersionCardPlacement.Uncommon,
                InstanceSettingsStore.Load(Path.Combine(_root, "versions", "Custom")).VersionCardType);
        }

        using var reloaded = new InstanceViewModel(services);
        await reloaded.LoadAsync();
        Assert.Equal("不常用版本", reloaded.InstanceVersionCardType);
    }

    private async Task<IReadOnlyList<VersionItem>> LoadAsync() =>
        await new VersionCatalog(new VersionRepository(_root)).LoadInstalledAsync();

    private static VersionSelectViewModel CreateModel(params VersionItem[] items)
    {
        var services = new AppServices(new AppSettings
        {
            GameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-identity-model-" + Guid.NewGuid().ToString("N"))
        }, saveSettings: _ => { });
        var launch = new LaunchViewModel(services);
        foreach (var item in items) launch.InstalledVersions.Add(item);
        return new VersionSelectViewModel(launch, services, () => { }, () => { });
    }

    private async Task WriteAsync(string id, string json)
    {
        var directory = Path.Combine(_root, "versions", id);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, id + ".json"), json);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
