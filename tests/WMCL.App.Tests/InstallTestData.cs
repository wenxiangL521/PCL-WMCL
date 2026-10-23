using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Modding.Fabric;
using WMCL.Modding.OptiFine;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Tests;

internal static class InstallTestData
{
    public static Task<IReadOnlyList<VersionItem>> LoadMinecraftVersions() =>
        Task.FromResult<IReadOnlyList<VersionItem>>([
            new() { Id = "1.20.1" }, new() { Id = "1.21.1" }, new() { Id = "1.12.2" }
        ]);

    public static OptiFineVersion OptiFine(string minecraft = "1.20.1") =>
        new(minecraft, "HD_U", "I6", "OptiFine.jar", "47.2.0");

    public static OptiFabricFile OptiFabric(string minecraft = "1.20.1", string version = "1.14.3", long id = 5025647) =>
        new(id, $"optifabric-{version}.jar", $"OptiFabric-v{version}", DateTimeOffset.UnixEpoch, 2, [minecraft, "Fabric"]);

    public static OptiFabricSelectionViewModel CreateOptiFabricSelection() => new(
        (game, _) => Task.FromResult<IReadOnlyList<OptiFabricFile>>([OptiFabric(game)]));

    public static ModrinthVersion Api(string minecraft = "1.20.1", string kind = "Fabric", string version = "0.92.12", string id = "api-version") =>
        new(id, ApiComponentService.ProjectId(kind), ApiComponentService.Name(kind), version + "+" + minecraft, "release",
            DateTimeOffset.UnixEpoch, [minecraft], [ApiComponentService.Loader(kind)],
            [new ModrinthFile("api-" + version + ".jar", "https://example.invalid/" + id + ".jar", 2, new string('a', 40), true)], []);

    public static ApiSelectionViewModel CreateApiSelection() => new(
        (game, kind, _) => Task.FromResult<IReadOnlyList<ModrinthVersion>>([Api(game, kind)]));

    public static ModsViewModel CreateMods(AppServices services) => new(services,
        (_, _) => Task.FromResult<IReadOnlyList<FabricLoaderInfo>>([new("0.16.0", true), new("0.15.0", true)]),
        (_, _) => Task.FromResult<IReadOnlyList<string>>(["47.2.0"]),
        game => Task.FromResult<IReadOnlyList<OptiFineVersion>>([OptiFine(game)]));
}
