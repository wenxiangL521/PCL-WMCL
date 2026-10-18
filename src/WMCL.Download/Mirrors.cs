namespace WMCL.Download;

public static class Mirrors
{
    private static readonly (string From, string To)[] Rules =
    [
        ("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com"),
        ("https://launchermeta.mojang.com", "https://bmclapi2.bangbang93.com"),
        ("https://launcher.mojang.com", "https://bmclapi2.bangbang93.com"),
        ("https://resources.download.minecraft.net", "https://bmclapi2.bangbang93.com/assets"),
        ("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven"),
        ("https://maven.minecraftforge.net", "https://bmclapi2.bangbang93.com/maven"),
        ("https://maven.neoforged.net/releases", "https://bmclapi2.bangbang93.com/maven"),
        ("https://meta.fabricmc.net", "https://bmclapi2.bangbang93.com/fabric-meta"),
        ("https://maven.fabricmc.net", "https://bmclapi2.bangbang93.com/maven")
    ];

    public static string ToBmclapi(string url)
    {
        foreach (var (from, to) in Rules)
        {
            if (url.StartsWith(from, StringComparison.OrdinalIgnoreCase))
                return to + url[from.Length..];
        }
        return url;
    }

    public static IReadOnlyList<string> GetUrls(string url, MirrorMode mode)
    {
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return [url];

        var mirror = ToBmclapi(url);
        if (string.Equals(mirror, url, StringComparison.Ordinal))
            return [url];

        return mode switch
        {
            MirrorMode.Official => [url, mirror],
            MirrorMode.Bmclapi => [mirror, url],
            _ => [url, mirror]
        };
    }
}
