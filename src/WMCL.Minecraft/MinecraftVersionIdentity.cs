using System.Text.Json;
using System.Text.RegularExpressions;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft;

public enum MinecraftVersionIdentitySource
{
    ClientVersion,
    InstallManifest,
    Patch,
    GameArgument,
    DownloadUrl,
    Library,
    JarReference,
    ClientMetadata,
    ProfileId,
    FolderName,
    ProfileText,
    OfficialCatalog,
    Unknown
}

public sealed record MinecraftVersionIdentity(string Version, bool IsReliable, MinecraftVersionIdentitySource Source);

internal static class MinecraftVersionIdentifier
{
    private const string VersionPattern = @"(?:[1-9][0-9]w[0-9]{2}[a-g]|(?:1|[2-9][0-9])\.[0-9]+(?:\.[0-9]+)?(?:-(?:pre|rc|snapshot-?)[0-9]+| Pre-Release(?: [0-9]+)?)?)(?:_unobfuscated)?";

    public static MinecraftVersionIdentity? FromProfile(VersionJson profile, JsonElement source, string? manifestVersion)
    {
        if (!string.IsNullOrWhiteSpace(profile.BaseGameVersion))
            return Reliable(profile.BaseGameVersion, MinecraftVersionIdentitySource.ClientVersion);
        if (!string.IsNullOrWhiteSpace(manifestVersion))
            return Reliable(manifestVersion, MinecraftVersionIdentitySource.InstallManifest);
        if (source.TryGetProperty("patches", out var patches) && patches.ValueKind == JsonValueKind.Array)
            foreach (var patch in patches.EnumerateArray())
                if (patch.ValueKind == JsonValueKind.Object && Text(patch, "id") == "game" && Text(patch, "version") is { } patched)
                    return Reliable(patched, MinecraftVersionIdentitySource.Patch);
        var arguments = profile.Arguments?.Game.SelectMany(argument => argument.IsLiteral && argument.Rules is null
            ? argument.Values : [string.Empty]).ToArray() ?? [];
        for (var index = 0; index + 1 < arguments.Length; index++)
            if (arguments[index] == "--fml.mcVersion" && !string.IsNullOrWhiteSpace(arguments[index + 1]))
                return Reliable(arguments[index + 1], MinecraftVersionIdentitySource.GameArgument);
        foreach (var download in profile.Downloads?.Values.AsEnumerable() ?? [])
            if (download.Url is { } url && Regex.Match(url, @"(?<=launcher\.mojang\.com/mc/game/)[^/]+") is { Success: true } matched)
                return Reliable(matched.Value, MinecraftVersionIdentitySource.DownloadUrl);
        foreach (var library in profile.Libraries)
        {
            var parts = library.Name.Split(':');
            if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[2])) continue;
            if (parts[0] is "net.fabricmc" or "net.legacyfabric" or "org.quiltmc" && parts[1] == "intermediary")
                return Reliable(parts[2], MinecraftVersionIdentitySource.Library);
            if (parts[0] == "net.minecraftforge" && parts[1] is "forge" or "fmlloader" &&
                Regex.Match(parts[2], @"^(1|[2-9][0-9])\.[0-9]+(?:\.[0-9]+)?(?:_pre[0-9]*)?(?=-)") is { Success: true } forge)
                return Reliable(forge.Value, MinecraftVersionIdentitySource.Library);
            if (parts[0].Equals("optifine", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("OptiFine", StringComparison.OrdinalIgnoreCase) &&
                Regex.Match(parts[2], @"^(1|[2-9][0-9])\.[0-9]+(?:\.[0-9]+)?(?=_)") is { Success: true } optifine)
                return Reliable(optifine.Value, MinecraftVersionIdentitySource.Library);
        }
        return Text(source, "jar") is { } jar ? Reliable(jar, MinecraftVersionIdentitySource.JarReference) : null;
    }

    public static MinecraftVersionIdentity Fallback(string instanceName, VersionJson profile, JsonElement source)
    {
        if (Regex.IsMatch(profile.Id, @"^(?:[ab][0-9]+\.[0-9]+(?:\.[0-9]+)?(?:_[0-9]+[a-z]?)?|c0\.[0-9a-z_]+|rd-[0-9]+|inf-[0-9]+)$", RegexOptions.IgnoreCase) ||
            profile.Id.ToLowerInvariant() is "1.rv-pre1" or "3d shareware v1.34" or "20w14∞" or "20w14infinite" or
                "22w13oneblockatatime" or "23w13a_or_b" or "24w14potato" or "25w14craftmine")
            return Reliable(profile.Id, MinecraftVersionIdentitySource.ProfileId);
        var profileId = Match(profile.Id);
        if (profileId is not null) return Reliable(profileId, MinecraftVersionIdentitySource.ProfileId);
        var folder = Match(instanceName);
        if (folder is not null) return new(folder, false, MinecraftVersionIdentitySource.FolderName);
        foreach (var property in source.EnumerateObject())
        {
            if (property.Name.Equals("libraries", StringComparison.OrdinalIgnoreCase)) continue;
            var candidate = Match(property.Value.GetRawText());
            if (candidate is not null) return new(candidate, false, MinecraftVersionIdentitySource.ProfileText);
        }
        return new(instanceName, false, MinecraftVersionIdentitySource.Unknown);
    }

    public static MinecraftVersionIdentity Reliable(string version, MinecraftVersionIdentitySource source) =>
        new(version.Replace("_unobfuscated", "", StringComparison.Ordinal).Replace(" Unobfuscated", "", StringComparison.Ordinal), true, source);

    private static string? Match(string text) => Regex.Match(text, VersionPattern, RegexOptions.IgnoreCase) is { Success: true } match
        ? match.Value.Replace("_unobfuscated", "", StringComparison.OrdinalIgnoreCase) : null;

    private static string? Text(JsonElement source, string name) => source.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;
}
