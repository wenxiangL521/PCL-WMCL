using PCL.Minecraft;
using PCL.Minecraft.Models;

namespace PCL.Minecraft.Tests;

public class VersionMergerTests
{
    [Fact]
    public void MergesScalarsLibrariesAndArguments()
    {
        var parent = new VersionJson
        {
            Id = "parent",
            MainClass = "parent.Main",
            Assets = "old",
            Type = "release",
            Libraries =
            [
                new Library { Name = "com.example:parent:1.0" }
            ],
            Arguments = new Arguments
            {
                Jvm =
                [
                    new ArgumentEntry { IsLiteral = true, Values = ["-Dparent=true"] }
                ],
                Game =
                [
                    new ArgumentEntry { IsLiteral = true, Values = ["--parent"] }
                ]
            }
        };
        var child = new VersionJson
        {
            Id = "child",
            InheritsFrom = "parent",
            MainClass = "child.Main",
            Assets = "new",
            Libraries =
            [
                new Library { Name = "com.example:child:1.0" }
            ],
            Arguments = new Arguments
            {
                Jvm =
                [
                    new ArgumentEntry { IsLiteral = true, Values = ["-Dchild=true"] }
                ],
                Game =
                [
                    new ArgumentEntry { IsLiteral = true, Values = ["--child"] }
                ]
            }
        };

        var merged = VersionMerger.Merge(parent, child);

        Assert.Equal("child", merged.Id);
        Assert.Equal("child.Main", merged.MainClass);
        Assert.Equal("new", merged.Assets);
        Assert.Equal(2, merged.Libraries.Count);
        Assert.Equal(new[] { "-Dparent=true", "-Dchild=true" }, merged.Arguments!.Jvm.SelectMany(e => e.Values));
        Assert.Equal(new[] { "--parent", "--child" }, merged.Arguments.Game.SelectMany(e => e.Values));
    }

    [Fact]
    public void ChildOverridesDownloadEntries()
    {
        var parent = new VersionJson
        {
            Downloads = new Dictionary<string, DownloadInfo>
            {
                ["client"] = new() { Url = "https://example.invalid/old.jar", Sha1 = "old" }
            }
        };
        var child = new VersionJson
        {
            Downloads = new Dictionary<string, DownloadInfo>
            {
                ["client"] = new() { Url = "https://example.invalid/new.jar", Sha1 = "new" }
            }
        };

        var merged = VersionMerger.Merge(parent, child);

        Assert.Equal("https://example.invalid/new.jar", merged.Downloads!["client"].Url);
    }
}
