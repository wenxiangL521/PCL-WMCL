using WMCL.Core.Update;

namespace WMCL.Core.Tests;

public sealed class UpdateFeedTests
{
    private static UpdateFeed.GitHubRelease Release(string tag, bool prerelease = false, bool draft = false,
        params string[] assets) => new()
        {
            TagName = tag,
            Prerelease = prerelease,
            Draft = draft,
            Body = "## 更新\n\n首个可见段落",
            Assets = assets.Select(name => new UpdateFeed.GitHubAsset
            {
                Name = name,
                DownloadUrl = "https://example.invalid/" + name,
                Size = 1024
            }).ToList()
        };

    [Fact]
    public void PicksHighestVersionAndSkipsDrafts()
    {
        var releases = new[]
        {
            Release("v0.2.0"),
            Release("v0.3.0", draft: true),
            Release("v0.2.5"),
            Release("v0.1.9")
        };
        var latest = UpdateFeed.SelectLatest(releases, includePrerelease: false);
        Assert.NotNull(latest);
        Assert.Equal("v0.2.5", latest!.TagName);
    }

    [Fact]
    public void PrereleaseOnlyIncludedForBetaChannel()
    {
        var releases = new[] { Release("v0.2.0"), Release("v0.3.0-beta.1", prerelease: true) };
        Assert.Equal("v0.2.0", UpdateFeed.SelectLatest(releases, false)!.TagName);
        Assert.Equal("v0.3.0-beta.1", UpdateFeed.SelectLatest(releases, true)!.TagName);
    }

    [Fact]
    public void SelectsPlatformAsset()
    {
        var assets = new[]
        {
            new UpdateFeed.GitHubAsset { Name = "WMCL.exe", DownloadUrl = "https://x/WMCL.exe" },
            new UpdateFeed.GitHubAsset { Name = "PCL-WMCL-linux-x64-0.2.0.tar.gz", DownloadUrl = "https://x/tar" },
            new UpdateFeed.GitHubAsset { Name = "pcl-wmcl-0.2.0-1.x86_64.rpm", DownloadUrl = "https://x/rpm" }
        };
        var picked = UpdateFeed.SelectAsset(assets);
        Assert.NotNull(picked);
        if (OperatingSystem.IsWindows())
            Assert.Equal("WMCL.exe", picked!.Name);
        else if (OperatingSystem.IsLinux())
            Assert.Contains("linux-x64", picked!.Name);
    }
}
