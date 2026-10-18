using System.IO.Compression;
using System.Text;
using WMCL.Modding.Modpack;

namespace WMCL.Minecraft.Tests;

public class LocalModpackTests
{
    [Fact]
    public void DetectsAndParsesMmcComponents()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Add(archive, "mmc-pack.json", """
                {"components":[{"uid":"net.minecraft","version":"1.20.1"},{"uid":"net.minecraftforge","version":"47.2.18"}]}
                """);
            Add(archive, "instance.cfg", "name=MMC Pack\n");
            Add(archive, "minecraft/mods/test.jar", "jar");
        }
        stream.Position = 0;
        using var read = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Equal(LocalModpackKind.Mmc, LocalModpackInstaller.Detect(read));
        var info = LocalModpackInstaller.Parse(read, LocalModpackKind.Mmc);
        Assert.Equal("MMC Pack", info.Name);
        Assert.Equal("1.20.1", info.Minecraft);
        Assert.Equal("47.2.18", info.Forge);
        Assert.Equal("minecraft/", info.OverridesFolder);
    }

    [Fact]
    public void DetectsHmclAndMcbbs()
    {
        using (var stream = new MemoryStream())
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                Add(archive, "modpack.json", """{"name":"HMCL Pack","gameVersion":"1.19.2"}""");
                Add(archive, "minecraft/mods/a.jar", "x");
            }
            stream.Position = 0;
            using var read = new ZipArchive(stream, ZipArchiveMode.Read);
            var kind = LocalModpackInstaller.Detect(read);
            Assert.Equal(LocalModpackKind.Hmcl, kind);
            var info = LocalModpackInstaller.Parse(read, kind);
            Assert.Equal("1.19.2", info.Minecraft);
        }

        using (var stream = new MemoryStream())
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                Add(archive, "mcbbs.packmeta", """
                    {"name":"MCBBS Pack","addons":[{"id":"game","version":"1.16.5"},{"id":"fabric","version":"0.14.0"}]}
                    """);
                Add(archive, "overrides/mods/a.jar", "x");
            }
            stream.Position = 0;
            using var read = new ZipArchive(stream, ZipArchiveMode.Read);
            var kind = LocalModpackInstaller.Detect(read);
            Assert.Equal(LocalModpackKind.Mcbbs, kind);
            var info = LocalModpackInstaller.Parse(read, kind);
            Assert.Equal("1.16.5", info.Minecraft);
            Assert.Equal("0.14.0", info.FabricLoader);
        }
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes);
    }
}

public class LocalModpackSettingsTests
{
    [Fact]
    public void ParsesMmcCommandsJvmAndServer()
    {
        var settings = LocalModpackInstaller.ParseMmcSettings(new[]
        {
            "OverrideCommands=true",
            "PreLaunchCommand=\"$INST_JAVA\" -version",
            "OverrideJavaArgs=true",
            "JvmArgs=-Xmx2G",
            "JoinServerOnLaunch=true",
            "JoinServerOnLaunchAddress=example.com:25566"
        });
        Assert.Equal("\"{java}\" -version", settings.PreLaunchCommand);
        Assert.Equal("-Xmx2G", settings.JvmArgs);
        Assert.Equal("example.com", settings.Server);
        Assert.Equal(25566, settings.Port);
    }

    [Fact]
    public void ParsesMcbbsLaunchInfo()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""
            {"launchInfo":{"javaArgument":["-Xmx2G"],"launchArgument":["--fullscreen"]}}
            """);
        var settings = LocalModpackInstaller.ParseMcbbsLaunchInfo(document.RootElement);
        Assert.Equal("-Xmx2G", settings.JvmArgs);
        Assert.Equal("--fullscreen", settings.GameArgs);
    }
}
