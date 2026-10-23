using System.Text.RegularExpressions;
using WMCL.Modding.Forgelike;
using WMCL.Modding.LiteLoader;

namespace WMCL.Modding.Loaders;

public sealed record InstallerArtifact(string FileName, IReadOnlyList<string> Urls, long MinimumSize = 1, string? Hash = null);

public static class ComponentInstallerSources
{
    public static InstallerArtifact Forge(string minecraft, ForgeVersionInfo version)
    {
        ValidateSegment(minecraft);
        ValidateSegment(version.Version);
        var branch = version.Branch;
        if (version.Version is "11.15.1.2318" or "11.15.1.1902" or "11.15.1.1890") branch = "1.8.9";
        if (string.IsNullOrEmpty(branch) && minecraft == "1.7.10" &&
            Version.TryParse(version.Version, out var parsed) && parsed.Revision >= 1300) branch = "1.7.10";
        if (!string.IsNullOrEmpty(branch)) ValidateSegment(branch);
        var coordinate = minecraft + "-" + version.Version + (string.IsNullOrEmpty(branch) ? "" : "-" + branch);
        var filename = "forge-" + coordinate + "-installer.jar";
        return new($"Forge-{minecraft}-{version.Version}.jar",
        [
            $"https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/{coordinate}/{filename}",
            $"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{coordinate}/{filename}"
        ], 64 * 1024, version.InstallerHash);
    }

    public static string ForgeChangelog(string minecraft, string version)
    {
        ValidateSegment(minecraft);
        ValidateSegment(version);
        return $"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{minecraft}-{version}/forge-{minecraft}-{version}-changelog.txt";
    }

    public static string NeoForgeBase(string minecraft, string version)
    {
        ValidateSegment(minecraft);
        ValidateSegment(version);
        var legacy = minecraft == "1.20.1";
        var package = legacy ? "forge" : "neoforge";
        var apiVersion = legacy && !version.StartsWith("1.20.1-", StringComparison.Ordinal) ? "1.20.1-" + version : version;
        return $"https://maven.neoforged.net/releases/net/neoforged/{package}/{apiVersion}/{package}-{apiVersion}";
    }

    public static InstallerArtifact NeoForge(string minecraft, string version)
    {
        var url = NeoForgeBase(minecraft, version) + "-installer.jar";
        var displayVersion = version.StartsWith("1.20.1-", StringComparison.Ordinal) ? version[7..] : version;
        return new($"NeoForge-{minecraft}-{displayVersion}.jar",
            [url.Replace("maven.neoforged.net/releases", "bmclapi2.bangbang93.com/maven", StringComparison.Ordinal), url], 64 * 1024);
    }

    public static InstallerArtifact LiteLoader(LiteLoaderVersion version)
    {
        LiteLoaderService.CreateProfile(version);
        var minecraft = version.Minecraft;
        string url;
        if (version.IsLegacy)
        {
            var build = minecraft switch
            {
                "1.7.10" or "1.7.2" => "04",
                _ => throw new NotSupportedException("未知的旧版 LiteLoader 安装器：" + minecraft)
            };
            url = $"https://dl.liteloader.com/redist/{minecraft}/liteloader-installer-{minecraft}-{build}.jar";
        }
        else
        {
            url = "http://jenkins.liteloader.com/job/LiteLoaderInstaller%20" + minecraft +
                "/lastSuccessfulBuild/artifact/" + (minecraft == "1.8" ? "ant/dist/" : "build/libs/") + version.InstallerFileName;
        }
        return new(version.InstallerFileName.Replace("-SNAPSHOT", "", StringComparison.Ordinal), [url], 1024 * 1024);
    }

    private static void ValidateSegment(string value)
    {
        if (!Regex.IsMatch(value, @"^[0-9A-Za-z][0-9A-Za-z._+\-]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new InvalidDataException("无效的组件版本信息。");
    }
}
