using WMCL.Minecraft;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft.Tests;

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

    [Theory]
    [InlineData("Parent.jar", "Child.jar")]
    [InlineData("custom.jar", "custom.jar")]
    [InlineData(null, null)]
    public void InheritedClientPathUsesTheChildIdentityWithoutChangingTheParent(string? path, string? expected)
    {
        var parent = new VersionJson
        {
            Id = "Parent",
            Downloads = new() { ["client"] = new() { Path = path, Url = "https://example.invalid/client.jar", Sha1 = "hash", Size = 42 } }
        };
        var child = new VersionJson { Id = "Child", InheritsFrom = "Parent" };
        var merged = VersionMerger.Merge(parent, child);
        Assert.Equal(expected, merged.Downloads!["client"].Path);
        Assert.Equal(path, parent.Downloads["client"].Path);
        Assert.Equal("https://example.invalid/client.jar", merged.Downloads["client"].Url);
        Assert.Equal("hash", merged.Downloads["client"].Sha1);
        Assert.Equal(42, merged.Downloads["client"].Size);
        var grandchild = VersionMerger.Merge(merged, new VersionJson { Id = "Grandchild", InheritsFrom = "Child" });
        Assert.Equal(path == "Parent.jar" ? "Grandchild.jar" : path, grandchild.Downloads!["client"].Path);
        Assert.Equal(expected, merged.Downloads["client"].Path);
    }

    [Fact]
    public void ExplicitChildClientPathIsPreservedWhenItMatchesTheParentName()
    {
        var parent = new VersionJson { Id = "Parent", Downloads = new() { ["client"] = new() { Path = "Parent.jar" } } };
        var child = new VersionJson { Id = "Child", Downloads = new() { ["client"] = new() { Path = "Parent.jar", Sha1 = "child" } } };
        var merged = VersionMerger.Merge(parent, child);
        Assert.Equal("Parent.jar", merged.Downloads!["client"].Path);
        Assert.Equal("child", merged.Downloads["client"].Sha1);
    }

    [Fact]
    public void ServerOverrideStillMapsTheInheritedClientToTheChildIdentity()
    {
        var parent = new VersionJson { Id = "Parent", Downloads = new() { ["client"] = new() { Path = "Parent.jar" } } };
        var child = new VersionJson { Id = "Child", Downloads = new() { ["server"] = new() { Path = "server.jar" } } };
        var merged = VersionMerger.Merge(parent, child);
        Assert.Equal("Child.jar", merged.Downloads!["client"].Path);
        Assert.Equal("server.jar", merged.Downloads["server"].Path);
        Assert.Equal("Parent.jar", parent.Downloads["client"].Path);
    }
}
