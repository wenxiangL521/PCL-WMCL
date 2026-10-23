using System.Text.Json;
using System.Text.Json.Serialization;

namespace WMCL.Modding.Modrinth;

public sealed record ModrinthProject(
    string Id,
    string Slug,
    string Title,
    string Description,
    string ProjectType,
    int Downloads,
    string? IconUrl,
    IReadOnlyList<string> Categories);

public sealed record ModrinthFile(
    string Filename,
    string Url,
    long Size,
    string? Sha1,
    bool Primary);

public sealed record ModrinthDependency(
    string? VersionId,
    string? ProjectId,
    string DependencyType);

public sealed record ModrinthVersion(
    string Id,
    string ProjectId,
    string Name,
    string VersionNumber,
    string VersionType,
    DateTimeOffset DatePublished,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    IReadOnlyList<ModrinthFile> Files,
    IReadOnlyList<ModrinthDependency> Dependencies);

public sealed record ModrinthSearchResult(
    int TotalHits,
    IReadOnlyList<ModrinthProject> Projects);

public sealed record ModrinthInstallResult(IReadOnlyList<string> Files)
{
    public IReadOnlyList<string> ResolvedFiles { get; init; } = Files;
}

internal sealed class ModrinthSearchResponse
{
    [JsonPropertyName("hits")] public List<ModrinthHit> Hits { get; set; } = [];
    [JsonPropertyName("total_hits")] public int TotalHits { get; set; }
}

internal sealed class ModrinthHit
{
    [JsonPropertyName("project_id")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("slug")] public string Slug { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("project_type")] public string ProjectType { get; set; } = "";
    [JsonPropertyName("downloads")] public int Downloads { get; set; }
    [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
    [JsonPropertyName("categories")] public List<string> Categories { get; set; } = [];
}

internal sealed class ModrinthVersionJson
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("project_id")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version_number")] public string VersionNumber { get; set; } = "";
    [JsonPropertyName("version_type")] public string VersionType { get; set; } = "";
    [JsonPropertyName("date_published")] public DateTimeOffset DatePublished { get; set; }
    [JsonPropertyName("game_versions")] public List<string> GameVersions { get; set; } = [];
    [JsonPropertyName("loaders")] public List<string> Loaders { get; set; } = [];
    [JsonPropertyName("files")] public List<ModrinthFileJson> Files { get; set; } = [];
    [JsonPropertyName("dependencies")] public List<ModrinthDependencyJson> Dependencies { get; set; } = [];
}

internal sealed class ModrinthFileJson
{
    [JsonPropertyName("filename")] public string Filename { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("primary")] public bool Primary { get; set; }
    [JsonPropertyName("hashes")] public Dictionary<string, string>? Hashes { get; set; }
}

internal sealed class ModrinthDependencyJson
{
    [JsonPropertyName("version_id")] public string? VersionId { get; set; }
    [JsonPropertyName("project_id")] public string? ProjectId { get; set; }
    [JsonPropertyName("dependency_type")] public string DependencyType { get; set; } = "";
}
