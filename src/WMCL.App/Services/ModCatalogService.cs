using WMCL.Modding.CurseForge;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Services;

public sealed record ModCatalogPage(
    IReadOnlyList<ModrinthProject> Projects,
    long TotalCount,
    int NextOffset,
    bool HasMore);

public sealed record ModCatalogVersion(
    ModrinthVersion? Modrinth,
    CurseForgeFile? CurseForge,
    bool IsCompatible);

public sealed class ModCatalogService(ModrinthClient modrinth, CurseForgeClient curseForge)
{
    private const int PageSize = 20;

    public bool IsAvailable(string platform) => platform != "CurseForge" || curseForge.IsConfigured;

    public async Task<ModCatalogPage> SearchAsync(
        string platform, string query, string gameVersion, string loader, int offset = 0,
        CancellationToken ct = default)
    {
        IReadOnlyList<ModrinthProject> projects;
        long total;
        if (platform == "CurseForge")
        {
            var result = await curseForge.SearchAsync(
                query, gameVersion, loader, PageSize, ct,
                CurseForgeClient.ClassIdMods, loader.Length > 0, offset).ConfigureAwait(false);
            projects = result.Projects.Select(project => new ModrinthProject(
                project.Id.ToString(), project.Slug, project.Name,
                project.Summary, "mod",
                (int)Math.Min(project.Downloads, int.MaxValue),
                project.LogoUrl, [])).ToArray();
            total = result.TotalCount;
        }
        else
        {
            var result = await modrinth.SearchAsync(
                query, gameVersion, loader, PageSize, ct, "mod", offset).ConfigureAwait(false);
            projects = result.Projects;
            total = result.TotalHits;
        }

        var nextOffset = offset + projects.Count;
        return new ModCatalogPage(projects, total, nextOffset,
            projects.Count >= PageSize && nextOffset < total);
    }

    public async Task<IReadOnlyList<ModCatalogVersion>> GetVersionsAsync(
        string platform, string projectId, string? gameVersion, string? loader,
        CancellationToken ct = default)
    {
        if (platform == "CurseForge")
        {
            if (!long.TryParse(projectId, out var id)) return [];
            var files = await curseForge.GetFilesAsync(id, "", null, ct).ConfigureAwait(false);
            return files.Select(file => new ModCatalogVersion(null, file,
                IsCurseForgeCompatible(file, gameVersion, loader))).ToArray();
        }

        var versions = await modrinth.GetVersionsAsync(projectId, ct).ConfigureAwait(false);
        return versions.Where(version => version.Files.Count > 0)
            .OrderByDescending(version => version.DatePublished)
            .Select(version => new ModCatalogVersion(version, null,
                IsModrinthCompatible(version, gameVersion, loader))).ToArray();
    }

    private static bool IsModrinthCompatible(ModrinthVersion version, string? game, string? loader) =>
        (string.IsNullOrWhiteSpace(game) ||
         version.GameVersions.Contains(game, StringComparer.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(loader) || loader == "vanilla" ||
         version.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase));

    private static bool IsCurseForgeCompatible(CurseForgeFile file, string? game, string? loader) =>
        (string.IsNullOrWhiteSpace(game) ||
         file.GameVersions.Contains(game, StringComparer.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(loader) || loader == "vanilla" ||
         file.GameVersions.Any(value => value.Equals(loader, StringComparison.OrdinalIgnoreCase) ||
                                        value.Contains(loader, StringComparison.OrdinalIgnoreCase)));
}
