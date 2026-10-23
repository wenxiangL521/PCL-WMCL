using System.Text.Json;
using WMCL.Minecraft.Models;

namespace WMCL.Minecraft.Tests;

public sealed partial class MinecraftVersionIdentityTests
{
    [Theory]
    [InlineData("{\"id\":\"Custom\",\"clientVersion\":\"1.20.1\"}", MinecraftVersionIdentitySource.ClientVersion)]
    [InlineData("{\"id\":\"Custom\",\"patches\":[{\"id\":\"game\",\"version\":\"1.20.1\"}]}", MinecraftVersionIdentitySource.Patch)]
    [InlineData("{\"id\":\"Custom\",\"arguments\":{\"game\":[\"--fml.mcVersion\",\"1.20.1\"]}}", MinecraftVersionIdentitySource.GameArgument)]
    [InlineData("{\"id\":\"Custom\",\"downloads\":{\"client\":{\"url\":\"https://launcher.mojang.com/mc/game/1.20.1/client.jar\"}}}", MinecraftVersionIdentitySource.DownloadUrl)]
    [InlineData("{\"id\":\"Custom\",\"libraries\":[{\"name\":\"net.minecraftforge:forge:1.20.1-47.2.0\"}]}", MinecraftVersionIdentitySource.Library)]
    [InlineData("{\"id\":\"Custom\",\"libraries\":[{\"name\":\"net.minecraftforge:fmlloader:1.20.1-47.2.0\"}]}", MinecraftVersionIdentitySource.Library)]
    [InlineData("{\"id\":\"Custom\",\"libraries\":[{\"name\":\"optifine:OptiFine:1.20.1_HD_U_I5\"}]}", MinecraftVersionIdentitySource.Library)]
    [InlineData("{\"id\":\"Custom\",\"libraries\":[{\"name\":\"net.legacyfabric:intermediary:1.20.1\"}]}", MinecraftVersionIdentitySource.Library)]
    [InlineData("{\"id\":\"Custom\",\"jar\":\"1.20.1\"}", MinecraftVersionIdentitySource.JarReference)]
    [InlineData("{\"id\":\"1.20.1-Forge\"}", MinecraftVersionIdentitySource.ProfileId)]
    public async Task ExplicitIdentitySourcesStayReliableAndDoNotRewriteProfiles(string json, MinecraftVersionIdentitySource source)
    {
        await WriteRawIdentityProfileAsync("Friendly", json);
        var version = await new VersionRepository(_root).GetLocalVersionAsync("Friendly");
        Assert.Equal(new MinecraftVersionIdentity("1.20.1", true, source), version.GameVersionIdentity);
        Assert.Equal("1.20.1", version.BaseGameVersion);
        Assert.Equal(json, await File.ReadAllTextAsync(Path.Combine(ProfileDirectory("Friendly"), "Friendly.json")));
    }

    [Theory]
    [InlineData("Pack1.20.1", "{\"id\":\"Friendly\"}", "1.20.1", MinecraftVersionIdentitySource.FolderName)]
    [InlineData("Friendly", "{\"id\":\"Friendly\",\"description\":\"Minecraft 1.20.1\"}", "1.20.1", MinecraftVersionIdentitySource.ProfileText)]
    [InlineData("Friendly", "{\"id\":\"Friendly\",\"assets\":\"1.20.1\"}", "1.20.1", MinecraftVersionIdentitySource.ProfileText)]
    [InlineData("Friendly", "{\"id\":\"Friendly\",\"libraries\":[{\"name\":\"example:mod:1.20.1\"}]}", "Friendly", MinecraftVersionIdentitySource.Unknown)]
    public async Task FolderAndRemainingJsonGuessesCarryUnreliableEvidence(string folder, string json, string expected, MinecraftVersionIdentitySource source)
    {
        await WriteRawIdentityProfileAsync(folder, json);
        var version = await new VersionRepository(_root).GetLocalVersionAsync(folder);
        Assert.Equal(new MinecraftVersionIdentity(expected, false, source), version.GameVersionIdentity);
        Assert.Equal(expected, version.BaseGameVersion);
        Assert.Equal(json, await File.ReadAllTextAsync(Path.Combine(ProfileDirectory(folder), folder + ".json")));
    }

    [Theory]
    [InlineData("1.20.1_unobfuscated", "1.20.1")]
    [InlineData("26.1-snapshot-1", "26.1-snapshot-1")]
    [InlineData("26.1-snapshot-10", "26.1-snapshot-10")]
    [InlineData("24w14a", "24w14a")]
    [InlineData("1.20 Pre-Release 1", "1.20 Pre-Release 1")]
    [InlineData("1.20 Pre-Release 10", "1.20 Pre-Release 10")]
    public async Task OriginalVersionPatternsRemainReliableInTheJsonId(string id, string expected)
    {
        await WriteProfileAsync("Friendly", new VersionJson { Id = id });
        var version = await new VersionRepository(_root).GetLocalVersionAsync("Friendly");
        Assert.Equal(new MinecraftVersionIdentity(expected, true, MinecraftVersionIdentitySource.ProfileId), version.GameVersionIdentity);
    }

    [Theory]
    [InlineData("a1.0")]
    [InlineData("b1.7.3")]
    [InlineData("c0.30_01c")]
    [InlineData("rd-132211")]
    [InlineData("inf-20100618")]
    [InlineData("23w13a_or_b")]
    [InlineData("25w14craftmine")]
    [InlineData("20w14∞")]
    [InlineData("1.RV-Pre1")]
    [InlineData("3D Shareware v1.34")]
    public async Task LegacyAndSpecialVanillaIdsAreNotTruncatedToANormalRelease(string id)
    {
        await WriteProfileAsync(id, new VersionJson { Id = id });
        var version = await new VersionRepository(_root).GetLocalVersionAsync(id);
        Assert.Equal(new MinecraftVersionIdentity(id, true, MinecraftVersionIdentitySource.ProfileId), version.GameVersionIdentity);
    }

    [Theory]
    [InlineData("WMCL")]
    [InlineData("PCL")]
    public async Task AManifestIsAnExplicitIdentitySource(string manifestDirectory)
    {
        await WriteProfileAsync("Friendly", new VersionJson { Id = "Friendly" });
        var path = Path.Combine(ProfileDirectory("Friendly"), manifestDirectory, "install.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """{"Components":{"minecraft":"1.20.1"}}""");
        var version = await new VersionRepository(_root).GetLocalVersionAsync("Friendly");
        Assert.Equal(new MinecraftVersionIdentity("1.20.1", true, MinecraftVersionIdentitySource.InstallManifest), version.GameVersionIdentity);
    }

    [Theory]
    [InlineData("{\"name\":\"1.20.1\",\"id\":\"hash\"}")]
    [InlineData("{\"name\":\"01234567890123456789012345678901ab\",\"id\":\"1.20.1\"}")]
    [InlineData("{\"id\":\"1.20.1\"}")]
    public async Task ClientMetadataWinsOverProfileNameGuesses(string metadata)
    {
        await WriteProfileAsync("Pack1.99", new VersionJson { Id = "1.98-FakeName" });
        await WriteClientMetadataAsync("Pack1.99", metadata);
        var version = await new VersionRepository(_root).GetLocalVersionAsync("Pack1.99");
        Assert.Equal(new MinecraftVersionIdentity("1.20.1", true, MinecraftVersionIdentitySource.ClientMetadata), version.GameVersionIdentity);
    }

    [Fact]
    public async Task InheritanceAndCloningPreserveUnreliableIdentityWithoutSerializingTheFlag()
    {
        await WriteProfileAsync("Parent1.20.1", new VersionJson { Id = "Friendly" });
        await WriteProfileAsync("Child", new VersionJson { Id = "Child", InheritsFrom = "Parent1.20.1", BaseGameVersion = "stale-child-value" });
        var version = await new VersionRepository(_root).GetLocalVersionAsync("Child");
        var expected = new MinecraftVersionIdentity("1.20.1", false, MinecraftVersionIdentitySource.FolderName);
        Assert.Equal(expected, version.GameVersionIdentity);
        var clone = VersionMerger.Clone(version);
        Assert.Equal(expected, clone.GameVersionIdentity);
        Assert.Equal("1.20.1", clone.BaseGameVersion);
        var serialized = JsonSerializer.Serialize(clone);
        Assert.DoesNotContain("GameVersionIdentity", serialized);
        Assert.DoesNotContain("IsReliable", serialized);
        Assert.DoesNotContain("FolderName", serialized);
    }

    [Fact]
    public async Task ConditionalGameArgumentsDoNotCreateAnExplicitIdentityAcrossTheCondition()
    {
        await WriteRawIdentityProfileAsync("Friendly",
            """{"id":"Friendly","arguments":{"game":["--fml.mcVersion",{"rules":[{"action":"allow","os":{"name":"windows"}}],"value":"1.19.4"},"1.20.1"]}}""");
        var version = await new VersionRepository(_root).GetLocalVersionAsync("Friendly");
        Assert.False(version.GameVersionIdentity!.IsReliable);
        Assert.Equal(MinecraftVersionIdentitySource.ProfileText, version.GameVersionIdentity.Source);
    }

    private async Task WriteRawIdentityProfileAsync(string folder, string json)
    {
        Directory.CreateDirectory(ProfileDirectory(folder));
        await File.WriteAllTextAsync(Path.Combine(ProfileDirectory(folder), folder + ".json"), json);
    }
}
