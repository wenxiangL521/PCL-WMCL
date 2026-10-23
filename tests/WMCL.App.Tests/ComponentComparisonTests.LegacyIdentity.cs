using System.Text.Json;
using Avalonia.Headless.XUnit;
using WMCL.Minecraft;
using WMCL.Modding.OptiFine;

namespace WMCL.App.Tests;

public sealed partial class ComponentComparisonTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ANewerManifestCannotOpenTheModificationPage(bool legacy, bool invalidProfile)
    {
        string? error = null;
        var (model, _) = await Model(showInstanceError: (_, message) =>
        {
            error = message;
            return Task.CompletedTask;
        });
        var path = InstallManifestStore.GetPath(_root, "Custom");
        var manifest = InstallManifestStore.Load(_root, "Custom")!;
        manifest.SchemaVersion = InstallManifestStore.SupportedSchemaVersion + 1;
        InstallManifestStore.Save(_root, "Custom", manifest);
        if (legacy)
        {
            var legacyPath = Path.Combine(_root, "versions/Custom/PCL/install.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            File.Move(path, legacyPath);
            path = legacyPath;
        }
        var original = File.ReadAllBytes(path);
        var profilePath = Path.Combine(_root, "versions/Custom/Custom.json");
        if (invalidProfile) File.WriteAllText(profilePath, "{");
        var originalProfile = File.ReadAllBytes(profilePath);
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.False(model.IsExistingInstance);
        Assert.False(model.CanApplyInstall);
        Assert.Null(error);
        Assert.Contains("请更新 WMCL", model.InstanceInitializationError);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(originalProfile, File.ReadAllBytes(profilePath));
    }

    [AvaloniaFact]
    public async Task ANewerManifestIntroducedAfterOpeningCannotBeRenamed()
    {
        var (model, _) = await Model();
        model.InstanceName = "Renamed";
        Assert.True(model.CanApplyInstall);
        var manifest = InstallManifestStore.Load(_root, "Custom")!;
        manifest.SchemaVersion = InstallManifestStore.SupportedSchemaVersion + 1;
        InstallManifestStore.Save(_root, "Custom", manifest);
        var original = File.ReadAllBytes(InstallManifestStore.GetPath(_root, "Custom"));
        await model.InstallCombinedCommand.ExecuteAsync(null);
        Assert.Contains("请更新 WMCL", model.CombinedStatus);
        Assert.True(Directory.Exists(Path.Combine(_root, "versions/Custom")));
        Assert.False(Directory.Exists(Path.Combine(_root, "versions/Renamed")));
        Assert.Equal(original, File.ReadAllBytes(InstallManifestStore.GetPath(_root, "Custom")));
    }

    [AvaloniaTheory]
    [InlineData("Fabric", "")]
    [InlineData("LegacyFabric", " ")]
    [InlineData("Quilt", "未知版本")]
    [InlineData("Forge", "unknown")]
    [InlineData("NeoForge", "Unknown")]
    public async Task AnUnresolvedDeclaredLoaderCannotUseTheDefaultCatalogVersion(string kind, string installed)
    {
        var (model, _) = await Model("无");
        var manifest = InstallManifestStore.Load(_root, "Custom")!;
        manifest.Components[kind.ToLowerInvariant()] = installed;
        InstallManifestStore.Save(_root, "Custom", manifest);
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.False(model.IsExistingInstance);
        Assert.False(model.CanApplyInstall);
        Assert.Contains($"无法确定 {kind} 的版本", model.CombinedStatus);
    }

    [AvaloniaTheory]
    [InlineData("Fabric", "net.fabricmc.loader.impl.launch.knot.KnotClient")]
    [InlineData("Quilt", "org.quiltmc.loader.impl.launch.knot.KnotClient")]
    [InlineData("Forge", "net.minecraftforge.bootstrap.ForgeBootstrap")]
    [InlineData("NeoForge", "net.neoforged.bootstrap.NeoForgeBootstrap")]
    public async Task ALoaderWithoutManifestOrVersionCoordinatesStopsOpening(string kind, string mainClass)
    {
        var (model, _) = await Model("无");
        File.Delete(InstallManifestStore.GetPath(_root, "Custom"));
        WriteLegacyProfile(mainClass, []);
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.False(model.IsExistingInstance);
        Assert.False(model.CanApplyInstall);
        Assert.Contains($"无法确定 {kind} 的版本", model.CombinedStatus);
    }

    [AvaloniaTheory]
    [InlineData("Fabric", "net.fabricmc:fabric-loader:0.15.0", "0.15.0")]
    [InlineData("LegacyFabric", "net.legacyfabric:fabric-loader:0.15.0", "0.15.0")]
    [InlineData("Quilt", "org.quiltmc:quilt-loader:0.15.0", "0.15.0")]
    [InlineData("Forge", "net.minecraftforge:forge:1.20.1-47.2.0", "47.2.0")]
    [InlineData("Forge", "net.minecraftforge:minecraftforge:47.2.0", "47.2.0")]
    [InlineData("Forge", "net.minecraftforge:fmlloader:1.20.1-47.2.0", "47.2.0")]
    [InlineData("NeoForge", "net.neoforged:neoforge:20.1.1", "20.1.1")]
    [InlineData("NeoForge", "net.neoforged:forge:1.20.1-47.1.99", "47.1.99")]
    public async Task AUniqueHistoricalLoaderCoordinateStillOpensWithoutAManifest(string kind, string coordinate, string installed)
    {
        var (model, _) = await Model("无");
        File.Delete(InstallManifestStore.GetPath(_root, "Custom"));
        WriteLegacyProfile("net.minecraft.client.main.Main", [coordinate]);
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.True(model.IsExistingInstance);
        Assert.Equal(kind, model.SelectedBaseLoader);
        Assert.Equal(installed, model.IsFabricBase ? model.Mods.SelectedLoader : model.Mods.SelectedForgelikeVersion);
        Assert.False(model.CanApplyInstall);
    }

    [AvaloniaTheory]
    [InlineData("--fml.neoForgeVersion", "20.1.1")]
    [InlineData("--fml.forgeVersion", "47.1.99")]
    public async Task NeoForgeCanRestoreItsHistoricalVersionFromGameArguments(string name, string installed)
    {
        var (model, _) = await Model("无");
        File.Delete(InstallManifestStore.GetPath(_root, "Custom"));
        File.WriteAllText(Path.Combine(_root, "versions/Custom/Custom.json"), JsonSerializer.Serialize(new
        {
            id = "Custom",
            clientVersion = "1.20.1",
            mainClass = "net.neoforged.bootstrap.Main",
            libraries = Array.Empty<object>(),
            arguments = new { game = new[] { name, installed } }
        }));
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.True(model.IsExistingInstance);
        Assert.Equal("NeoForge", model.SelectedBaseLoader);
        Assert.Equal(installed, model.Mods.SelectedForgelikeVersion);
        Assert.False(model.CanApplyInstall);
    }

    [AvaloniaTheory]
    [InlineData("net.fabricmc:fabric-loader:0.15.0", "net.fabricmc:fabric-loader:0.16.0", "Fabric")]
    [InlineData("net.minecraftforge:forge:1.20.1-47.2.0", "net.minecraftforge:forge:1.20.1-47.3.0", "Forge")]
    public async Task ConflictingHistoricalLoaderCoordinatesCannotUseTheFirstLibrary(string first, string second, string kind)
    {
        var (model, _) = await Model("无");
        File.Delete(InstallManifestStore.GetPath(_root, "Custom"));
        WriteLegacyProfile("net.minecraft.client.main.Main", [first, second]);
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.False(model.IsExistingInstance);
        Assert.False(model.CanApplyInstall);
        Assert.Contains($"无法确定 {kind} 的版本", model.CombinedStatus);
    }

    [AvaloniaTheory]
    [InlineData("OptiFine", false)]
    [InlineData("OptiFine", true)]
    [InlineData("Forge", false)]
    [InlineData("Forge", true)]
    [InlineData("Fabric", false)]
    [InlineData("Fabric", true)]
    public async Task AHistoricalOptiFineCoordinateRestoresTheOldObjectInsteadOfTheDefault(string kind, bool noManifest)
    {
        var (model, _) = await Model("无", catalog: new(OptiFine: game =>
            Task.FromResult<IReadOnlyList<OptiFineVersion>>([
                new(game, "HD_U", "I6", "OptiFine-new.jar", ""), new(game, "HD_U", "I5", "OptiFine-old.jar", "")])));
        var mainClass = kind == "Fabric" ? "net.fabricmc.loader.impl.launch.knot.KnotClient" : "net.minecraft.launchwrapper.Launch";
        var libraries = kind switch
        {
            "Forge" => new[] { "net.minecraftforge:forge:1.20.1-47.2.0", "optifine:OptiFine:1.20.1_HD_U_I5" },
            "Fabric" => ["net.fabricmc:fabric-loader:0.15.0", "optifine:OptiFine:1.20.1_HD_U_I5"],
            _ => ["optifine:OptiFine:1.20.1_HD_U_I5"]
        };
        WriteLegacyProfile(mainClass, libraries);
        if (noManifest) File.Delete(InstallManifestStore.GetPath(_root, "Custom"));
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.True(model.IsExistingInstance);
        Assert.Equal(kind, model.SelectedBaseLoader);
        Assert.Equal("I5", model.Mods.SelectedOptiFineVersion!.Patch);
        Assert.Equal("OptiFine-old.jar", model.Mods.SelectedOptiFineVersion.FileName);
        Assert.True(model.IsOptiFineSelected);
        Assert.False(model.OptiFineCard.HasComparison);
        Assert.False(model.CanApplyInstall);
        await Select(model.OptiFineCard, "I6");
        Assert.True(model.CanApplyInstall);
        await model.OptiFineCard.RestoreCommand.ExecuteAsync(null);
        Assert.Equal("I5", model.Mods.SelectedOptiFineVersion!.Patch);
        Assert.False(model.CanApplyInstall);
    }

    [AvaloniaTheory]
    [InlineData("1.20.1_HD_U_I4")]
    [InlineData("1.20.2_HD_U_I5")]
    [InlineData("1.20.1_HD_I5")]
    [InlineData("")]
    public async Task AnUnresolvedOptiFineCoordinateStopsOpening(string coordinate)
    {
        var (model, _) = await Model("无");
        File.Delete(InstallManifestStore.GetPath(_root, "Custom"));
        WriteLegacyProfile("net.minecraft.launchwrapper.Launch", ["optifine:OptiFine:" + coordinate]);
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.False(model.IsExistingInstance);
        Assert.False(model.CanApplyInstall);
        Assert.Contains("OptiFine", model.CombinedStatus);
    }

    [AvaloniaFact]
    public async Task AmbiguousOptiFineInstallerMetadataCannotBecomeTheOriginalObject()
    {
        var (model, _) = await Model("无", catalog: new(OptiFine: game =>
            Task.FromResult<IReadOnlyList<OptiFineVersion>>([
                new(game, "HD_U", "I5", "first.jar", ""), new(game, "HD_U", "I5", "second.jar", "")])));
        File.Delete(InstallManifestStore.GetPath(_root, "Custom"));
        WriteLegacyProfile("net.minecraft.launchwrapper.Launch", ["optifine:OptiFine:1.20.1_HD_U_I5"]);
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.False(model.IsExistingInstance);
        Assert.Contains("多个文件", model.CombinedStatus);
    }

    [AvaloniaFact]
    public async Task ConflictingHistoricalOptiFineCoordinatesCannotUseTheFirstLibrary()
    {
        var (model, _) = await Model("无");
        File.Delete(InstallManifestStore.GetPath(_root, "Custom"));
        WriteLegacyProfile("net.minecraft.launchwrapper.Launch",
            ["optifine:OptiFine:1.20.1_HD_U_I5", "optifine:OptiFine:1.20.1_HD_U_I6"]);
        await model.OpenInstanceAsync(_root, "Custom");
        Assert.False(model.IsExistingInstance);
        Assert.False(model.CanApplyInstall);
        Assert.Contains("无法确定 OptiFine", model.CombinedStatus);
    }

    private void WriteLegacyProfile(string mainClass, string[] libraries)
    {
        File.WriteAllText(Path.Combine(_root, "versions/Custom/Custom.json"), JsonSerializer.Serialize(new
        {
            id = "Custom",
            clientVersion = "1.20.1",
            mainClass,
            libraries = libraries.Select(name => new { name }).ToArray()
        }));
    }
}
