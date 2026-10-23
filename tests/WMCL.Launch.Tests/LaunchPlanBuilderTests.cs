using WMCL.Launch;
using WMCL.Launch.Java;
using WMCL.Minecraft.Models;

namespace WMCL.Launch.Tests;

public sealed class LaunchPlanBuilderTests : IDisposable
{
    private readonly string _gameDirectory;

    public LaunchPlanBuilderTests()
    {
        _gameDirectory = Path.Combine(Path.GetTempPath(), "wmcl-launch-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_gameDirectory, "versions", "1.21.1"));
        File.WriteAllText(
            Path.Combine(_gameDirectory, "versions", "1.21.1", "1.21.1.jar"), "dummy");
    }

    [Fact]
    public void BuildsClasspathAndReplacesPlaceholders()
    {
        var version = new VersionJson
        {
            Id = "1.21.1",
            MainClass = "net.minecraft.client.main.Main",
            Type = "release",
            AssetIndex = new AssetIndexInfo { Id = "17" },
            Arguments = new Arguments
            {
                Jvm =
                [
                    new ArgumentEntry
                    {
                        IsLiteral = true,
                        Values = ["-Djava.library.path=${natives_directory}"]
                    },
                    new ArgumentEntry { IsLiteral = true, Values = ["-cp"] },
                    new ArgumentEntry { IsLiteral = true, Values = ["${classpath}"] }
                ],
                Game =
                [
                    new ArgumentEntry
                    {
                        IsLiteral = true,
                        Values =
                        [
                            "--username", "${auth_player_name}",
                            "--gameDir", "${game_directory}",
                            "--assetIndex", "${assets_index_name}"
                        ]
                    }
                ]
            },
            Libraries =
            [
                new Library
                {
                    Name = "org.lwjgl:lwjgl:3.3.3",
                    Downloads = new LibraryDownloads
                    {
                        Artifact = new DownloadInfo
                        {
                            Path = "org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3.jar",
                            Url = "https://example.invalid/lwjgl.jar"
                        }
                    }
                },
                new Library
                {
                    Name = "org.lwjgl:lwjgl:3.3.3:natives-linux",
                    Downloads = new LibraryDownloads
                    {
                        Artifact = new DownloadInfo
                        {
                            Path = "org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-linux.jar",
                            Url = "https://example.invalid/lwjgl-natives.jar"
                        }
                    },
                    Rules = [new Rule { Action = "allow", Os = new RuleOs { Name = "linux" } }]
                }
            ]
        };

        var nativeSubdirectory = Path.Combine(_gameDirectory, "versions", "1.21.1", "natives",
            "linux", "x64", "org", "lwjgl");
        Directory.CreateDirectory(nativeSubdirectory);
        File.WriteAllText(Path.Combine(nativeSubdirectory, "liblwjgl.so"), "");

        var request = new LaunchRequest
        {
            Version = version,
            VersionId = "1.21.1",
            GameDirectory = _gameDirectory,
            Java = new JavaRuntime("/usr/bin/java", new Version(21, 0, 0), 21, "x86_64",
                "/usr/lib/jvm/test", "Test"),
            Account = AccountInfo.Offline("Steve"),
            MemoryMb = 2048
        };

        var plan = LaunchPlanBuilder.Build(request);
        var arguments = plan.Arguments.ToList();

        Assert.Contains("lwjgl-3.3.3.jar", plan.Classpath);
        Assert.Contains("lwjgl-3.3.3-natives-linux.jar", plan.Classpath);
        Assert.Contains("-Xmx2048M", arguments);

        var usernameIndex = arguments.IndexOf("--username");
        Assert.True(usernameIndex >= 0);
        Assert.Equal("Steve", arguments[usernameIndex + 1]);

        Assert.Contains("net.minecraft.client.main.Main", arguments);
        Assert.All(arguments, argument => Assert.DoesNotContain("${", argument));
        Assert.Equal(Path.Combine(_gameDirectory, "versions", "1.21.1", "natives"),
            plan.NativesDirectory);
        var libraryPathArg = Assert.Single(arguments,
            argument => argument.StartsWith("-Djava.library.path=", StringComparison.Ordinal));
        Assert.Contains(plan.NativesDirectory, libraryPathArg);
        if (OperatingSystem.IsLinux())
        {
            Assert.Contains("/usr/lib", libraryPathArg);
            Assert.Contains(nativeSubdirectory, libraryPathArg);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_gameDirectory))
            Directory.Delete(_gameDirectory, recursive: true);
    }
}
