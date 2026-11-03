namespace WMCL.Modding.Modpack;

public sealed record ModpackExportOptions(
    bool IncludeMods = true,
    bool IncludeConfigs = true,
    bool IncludeResourcePacks = true,
    bool IncludeShaderPacks = true,
    bool IncludeSaves = false,
    bool IncludeWmcl = false,
    bool IncludeOptions = true,
    IReadOnlySet<string>? IncludedFiles = null);
