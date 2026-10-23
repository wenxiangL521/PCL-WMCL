using System.Collections.Immutable;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Modrinth;

namespace WMCL.Modding.Resources;

public sealed record ResourceInstallRequest(
    string Platform, string ProjectId, string Title, string ResourceType = "mod",
    string? VersionId = null, ModrinthVersion? Modrinth = null, CurseForgeFile? CurseForge = null);

public sealed record ResourceInstallEntry(
    string Platform, string ProjectId, string VersionId, string Title, string ResourceType,
    string FileName, long Size, string? Sha1, ImmutableArray<string> Urls, bool IsDependency)
{
    public string Key => Platform + ":" + ProjectId;
    public string Detail => $"{Platform} · {VersionId} · {FileName}";
}

public sealed record ResourceInstallPlan(string GameVersion, string Loader, ImmutableArray<ResourceInstallEntry> Entries);
