namespace PCL.Minecraft;

/// <summary>
/// 版本隔离策略：auto 沿用可装 Mod 判定，isolated 强制版本目录，global 强制 .minecraft 根目录。
/// 对照 PCL2 的全局/实例版本隔离设置。
/// </summary>
public static class IsolationPolicy
{
    public const string Auto = "auto";
    public const string Isolated = "isolated";
    public const string Global = "global";

    public static string Normalize(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        Isolated => Isolated,
        Global => Global,
        _ => Auto
    };

    public static string Resolve(string? instanceMode, string? globalMode)
    {
        var instance = Normalize(instanceMode);
        return instance == Auto ? Normalize(globalMode) : instance;
    }

    public static string ResolveForInstance(string gameRoot, string versionId, string? globalMode)
    {
        var settings = InstanceSettingsStore.Load(
            Path.Combine(gameRoot, "versions", versionId));
        return Resolve(settings.IsolationMode, globalMode);
    }

    public static string GetGameDirectory(
        string gameRoot,
        string versionId,
        Models.VersionJson version,
        string? globalMode = null)
    {
        var mode = ResolveForInstance(gameRoot, versionId, globalMode);
        return MinecraftVersionUtil.GetGameDirectory(gameRoot, versionId, version, mode);
    }
}
