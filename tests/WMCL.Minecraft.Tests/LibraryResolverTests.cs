using WMCL.Minecraft;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft.Tests;

public class LibraryResolverTests
{
    private static Library Library(string name, string path, params Rule[] rules) => new()
    {
        Name = name,
        Downloads = new LibraryDownloads
        {
            Artifact = new DownloadInfo
            {
                Path = path,
                Url = "https://example.invalid/" + path,
                Sha1 = new string('a', 40),
                Size = 1
            }
        },
        Rules = rules.Length == 0 ? null : rules.ToList()
    };

    [Fact]
    public void KeepsBaseJarAndPlatformNativesSeparately()
    {
        var version = new VersionJson
        {
            Libraries =
            [
                Library("org.lwjgl:lwjgl:3.3.3",
                    "org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3.jar"),
                Library("org.lwjgl:lwjgl:3.3.3:natives-linux",
                    "org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-linux.jar",
                    new Rule { Action = "allow", Os = new RuleOs { Name = "linux" } }),
                Library("org.lwjgl:lwjgl:3.3.3:natives-windows",
                    "org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-windows.jar",
                    new Rule { Action = "allow", Os = new RuleOs { Name = "windows" } })
            ]
        };

        var resolved = LibraryResolver.Resolve(version, RuleContext.CreateDefault());

        Assert.Equal(2, resolved.Count);
        Assert.Contains(resolved, l => l.RelativePath.EndsWith("lwjgl-3.3.3.jar"));
        Assert.Contains(resolved, l =>
            l.Classifier == "natives-linux" &&
            l.RelativePath.EndsWith("lwjgl-3.3.3-natives-linux.jar"));
        Assert.DoesNotContain(resolved, l => l.RelativePath.Contains("natives-windows"));
    }

    [Fact]
    public void ChildVersionOverridesParent()
    {
        var parent = new VersionJson
        {
            Libraries = [Library("com.example:demo:1.0", "com/example/demo/1.0/demo-1.0.jar")]
        };
        var child = new VersionJson
        {
            Libraries = [Library("com.example:demo:2.0", "com/example/demo/2.0/demo-2.0.jar")]
        };

        var merged = VersionMerger.Merge(parent, child);
        var resolved = LibraryResolver.Resolve(merged, RuleContext.CreateDefault());

        var demo = Assert.Single(resolved);
        Assert.Equal("2.0", demo.Name.Version);
        Assert.EndsWith("demo-2.0.jar", demo.RelativePath);
    }

    [Fact]
    public void LegacyNativesMapSelectsClassifier()
    {
        var library = new Library
        {
            Name = "org.lwjgl:lwjgl:2.9.4",
            Natives = new Dictionary<string, string> { ["linux"] = "natives-linux" },
            Downloads = new LibraryDownloads
            {
                Artifact = new DownloadInfo
                {
                    Path = "org/lwjgl/lwjgl/2.9.4/lwjgl-2.9.4.jar",
                    Url = "https://example.invalid/lwjgl.jar"
                },
                Classifiers = new Dictionary<string, DownloadInfo>
                {
                    ["natives-linux"] = new()
                    {
                        Path = "org/lwjgl/lwjgl/2.9.4/lwjgl-2.9.4-natives-linux.jar",
                        Url = "https://example.invalid/lwjgl-natives.jar"
                    }
                }
            }
        };

        var resolved = LibraryResolver.Resolve(
            new VersionJson { Libraries = [library] }, new RuleContext("linux", "x86_64", "", new Dictionary<string, bool>()));

        Assert.Equal(2, resolved.Count);
        Assert.Contains(resolved, item => !item.IsNative && item.RelativePath.EndsWith("lwjgl-2.9.4.jar"));
        var native = Assert.Single(resolved, item => item.IsNative);
        Assert.True(native.IsNative);
        Assert.Equal("natives-linux", native.Classifier);
    }

    [Theory]
    [InlineData("linux", "x86", "natives-32")]
    [InlineData("linux", "x86_64", "natives-64")]
    [InlineData("windows", "x86_64", null)]
    public void ClassifierOnlyLibrariesDoNotInventAnArtifactAndExpandArchitecture(string os, string arch, string? expected)
    {
        var library = new Library
        {
            Name = "org.example:native:1",
            Url = "https://example.invalid/maven/",
            Natives = new() { ["linux"] = "natives-${arch}" },
            Downloads = new LibraryDownloads
            {
                Classifiers = new() { ["natives-32"] = new(), ["natives-64"] = new() }
            }
        };
        var resolved = LibraryResolver.Resolve(new VersionJson { Libraries = [library] }, new RuleContext(os, arch, "", new Dictionary<string, bool>()));
        if (expected is null) Assert.Empty(resolved);
        else
        {
            var native = Assert.Single(resolved);
            Assert.True(native.IsNative);
            Assert.Equal(expected, native.Classifier);
            Assert.EndsWith("native-1-" + expected + ".jar", native.RelativePath);
            Assert.Equal("https://example.invalid/maven/" + native.RelativePath, native.Url);
        }
    }
}
