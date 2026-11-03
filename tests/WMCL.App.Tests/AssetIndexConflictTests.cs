using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WMCL.App.Services;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Launch;
using WMCL.Launch.Java;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed class AssetIndexConflictTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-index-conflict-" + Guid.NewGuid().ToString("N"));
    private const string OldAsset = "old translation";
    private const string NewAsset = "new translation";
    private const string Client = "fixture client";
    private static string Hash(string text) => Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(text)));
    private static string Index(string asset) => JsonSerializer.Serialize(new
    {
        objects = new Dictionary<string, object> { ["minecraft/lang/en_us.json"] = new { hash = Hash(asset), size = Encoding.UTF8.GetByteCount(asset) } }
    });
    private static string Alias => "34-" + Hash(Index(NewAsset));

    public AssetIndexConflictTests()
    {
        Directory.CreateDirectory(_root);
        Write(_root, "assets/indexes/34.json", Index(OldAsset));
        Write(_root, "assets/objects/" + Hash(OldAsset)[..2] + "/" + Hash(OldAsset), OldAsset);
        CreateExisting("Other", "34", Index(OldAsset));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DifferentSnapshotsWithTheSameIdCoexistAndBothPassOfflineLaunchVerification(bool existing)
    {
        if (existing) CreateExisting("Custom", "34", Index(OldAsset));
        var beforeOther = File.ReadAllBytes(Path.Combine(_root, "versions/Other/Other.json"));
        var beforeIndex = File.ReadAllBytes(Path.Combine(_root, "assets/indexes/34.json"));
        var timestamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(_root, "assets/indexes/34.json"), timestamp);
        await Install(existing, new Backend(), Report);
        var version = await new VersionRepository(_root).GetLocalVersionAsync("Custom");
        Assert.Equal(Alias, version.AssetIndex!.Id);
        Assert.Equal("34", version.Assets);
        Assert.Equal(Hash(Index(NewAsset)), version.AssetIndex.Sha1);
        Assert.Equal(Encoding.UTF8.GetByteCount(Index(NewAsset)), version.AssetIndex.Size);
        Assert.Equal("https://example.invalid/new-index.json", version.AssetIndex.Url);
        Assert.Equal(beforeIndex, File.ReadAllBytes(Path.Combine(_root, "assets/indexes/34.json")));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(Path.Combine(_root, "assets/indexes/34.json")));
        Assert.Equal(beforeOther, File.ReadAllBytes(Path.Combine(_root, "versions/Other/Other.json")));
        var manifest = InstallManifestStore.Load(_root, "Custom")!;
        Assert.Contains(manifest.ManagedFiles, file => file.RelativePath == "assets/indexes/" + Alias + ".json" && file.Sha1 == Hash(Index(NewAsset)));
        Assert.DoesNotContain(manifest.ManagedFiles, file => file.RelativePath == "assets/indexes/34.json");
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "Custom")).IsHealthy);
        using var handler = new OfflineHandler();
        using var http = new HttpClient(handler);
        var verification = new GameFileVerificationService(new VersionRepository(_root), new DownloadManager(http));
        Assert.Equal(0, (await verification.EnsureAsync("Other")).RepairedFiles);
        Assert.Equal(0, (await verification.EnsureAsync("Custom")).RepairedFiles);
        Assert.Equal(0, (await verification.EnsureAsync("Other")).RepairedFiles);
        Assert.Equal(0, handler.Requests);
        var launch = LaunchPlanBuilder.Build(new LaunchRequest
        {
            Version = version,
            VersionId = "Custom",
            GameDirectory = _root,
            Java = new JavaRuntime("/fixture/java", new Version(21, 0), 21, "x86_64", "/fixture", "Fixture"),
            Account = AccountInfo.Offline("Tester")
        });
        Assert.Contains(Alias, launch.Arguments);
        AssertClean();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnExistingVerifiedAliasIsReusedWithoutRewritingIt(bool existing)
    {
        if (existing) CreateExisting("Custom", "34", Index(OldAsset));
        var aliasPath = Write(_root, "assets/indexes/" + Alias + ".json", Index(NewAsset));
        var timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(aliasPath, timestamp);
        await Install(existing, new Backend(), Report);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(aliasPath));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "Custom")).IsHealthy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AliasCollisionRejectsDifferentBytesAndPreservesAllFiles(bool existing)
    {
        if (existing) CreateExisting("Custom", "34", Index(OldAsset));
        Write(_root, "assets/indexes/" + Alias + ".json", "wrong alias bytes");
        var before = Snapshot();
        await Assert.ThrowsAsync<IOException>(() => Install(existing, new Backend(), Report));
        Assert.Equal(before, Snapshot());
        AssertClean();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancellationRollsBackPublishedAliasesUnlessAnotherInstanceNowReferencesThem(bool existing, bool shared)
    {
        if (existing) CreateExisting("Custom", "34", Index(OldAsset));
        var before = Snapshot();
        using var cancellation = new CancellationTokenSource();
        void Cancel(CombinedInstallStep step, CombinedInstallStepState state, JobProgress progress)
        {
            if (step != CombinedInstallStep.Apply || (existing ? state != CombinedInstallStepState.Completed : progress.Stage != "发布实例")) return;
            Assert.True(File.Exists(Path.Combine(_root, "assets/indexes/" + Alias + ".json")));
            if (shared) CreateExisting("Concurrent", Alias, Index(NewAsset));
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Install(existing, new Backend(), Cancel, cancellation.Token));
        foreach (var (path, hash) in before) Assert.Equal(hash, FileUtil.Sha1(Path.Combine(_root, path)));
        Assert.Equal(shared, File.Exists(Path.Combine(_root, "assets/indexes/" + Alias + ".json")));
        Assert.Equal(existing, File.Exists(Path.Combine(_root, "versions/Custom/Custom.json")));
        AssertClean();
    }

    [Theory]
    [InlineData(false, "virtual")]
    [InlineData(true, "virtual")]
    [InlineData(false, "resources")]
    [InlineData(true, "resources")]
    [InlineData(false, "hash")]
    [InlineData(true, "size")]
    [InlineData(false, "unverified")]
    [InlineData(true, "missing")]
    [InlineData(false, "id-path")]
    [InlineData(true, "object-path")]
    [InlineData(false, "schema")]
    public async Task UnverifiedLegacyMissingOrMaliciousIndexesAreRejectedWithoutPublication(bool existing, string mode)
    {
        if (existing) CreateExisting("Custom", "34", Index(OldAsset));
        var before = Snapshot();
        var backend = new Backend { Mode = mode };
        void ChangeSchema(CombinedInstallStep step, CombinedInstallStepState state, JobProgress progress)
        {
            if (mode != "schema" || step != CombinedInstallStep.Manifest || state != CombinedInstallStepState.Completed) return;
            var path = InstallManifestStore.GetPath(backend.Root!, "Custom");
            var manifest = JsonNode.Parse(File.ReadAllText(path))!;
            manifest["SchemaVersion"] = 999;
            File.WriteAllText(path, manifest.ToJsonString());
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => Install(existing, backend, ChangeSchema));
        Assert.Equal(before, Snapshot());
        AssertClean();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryLibraryConflictsStillRejectTheWholeInstall(bool existing)
    {
        if (existing) CreateExisting("Custom", "34", Index(OldAsset));
        Write(_root, "libraries/fixture.jar", "old library");
        var before = Snapshot();
        await Assert.ThrowsAsync<IOException>(() => Install(existing, new Backend { Mode = "library" }, Report));
        Assert.Equal(before, Snapshot());
        AssertClean();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReferencedVerifiedFormattingDifferenceAlsoUsesAnAlias(bool existing)
    {
        if (existing) CreateExisting("Custom", "34", Index(OldAsset));
        var formatted = JsonNode.Parse(Index(NewAsset))!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        Write(_root, "assets/indexes/34.json", formatted);
        await Install(existing, new Backend(), Report);
        Assert.Equal(formatted, File.ReadAllText(Path.Combine(_root, "assets/indexes/34.json")));
        Assert.Equal(Alias, (await new VersionRepository(_root).GetLocalVersionAsync("Custom")).AssetIndex!.Id);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "Custom")).IsHealthy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnrelatedDifferentIndexIsNotSilentlyAliased(bool existing)
    {
        if (existing) CreateExisting("Custom", "34", Index(OldAsset));
        var before = Snapshot();
        await Assert.ThrowsAsync<IOException>(() => Install(existing, new Backend { Mode = "unrelated" }, Report));
        Assert.Equal(before, Snapshot());
        AssertClean();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingManagedIndexEntriesFollowTheAliasAndKeepTheirMetadata(bool existing)
    {
        if (existing) CreateExisting("Custom", "34", Index(OldAsset));
        var backend = new Backend();
        void RecordIndex(CombinedInstallStep step, CombinedInstallStepState state, JobProgress progress)
        {
            if (step != CombinedInstallStep.Manifest || state != CombinedInstallStepState.Completed) return;
            var manifest = InstallManifestStore.Load(backend.Root!, "Custom")!;
            manifest.ManagedFiles.Add(new("assets/indexes/34.json", Hash(Index(NewAsset)), "fixture-assets", "https://example.invalid/managed-index.json"));
            InstallManifestStore.Save(backend.Root!, "Custom", manifest);
        }
        await Install(existing, backend, RecordIndex);
        var aliasFile = Assert.Single(InstallManifestStore.Load(_root, "Custom")!.ManagedFiles,
            file => file.RelativePath == "assets/indexes/" + Alias + ".json");
        Assert.Equal("fixture-assets", aliasFile.Component);
        Assert.Equal("https://example.invalid/managed-index.json", aliasFile.Url);
        Assert.DoesNotContain(InstallManifestStore.Load(_root, "Custom")!.ManagedFiles,
            file => file.RelativePath == "assets/indexes/34.json");
        Assert.True((await InstallManifestStore.VerifyAsync(_root, "Custom")).IsHealthy);
    }

    private Task<CombinedInstallResult> Install(bool existing, Backend backend,
        Action<CombinedInstallStep, CombinedInstallStepState, JobProgress> report, CancellationToken ct = default)
    {
        var plan = CombinedInstallPlan.Create(_root, "26.3", false, "无", null, null, false, false,
            existing ? "" : "Custom", existingInstanceId: existing ? "Custom" : null);
        return existing
            ? new ExistingInstanceInstallService(_root, IsolationPolicy.Isolated, backend.ForRoot).InstallAsync(plan, report, ct)
            : new NewInstanceInstallService(_root, IsolationPolicy.Isolated, backend.ForRoot).InstallAsync(plan, report, ct);
    }

    private void CreateExisting(string id, string indexId, string index)
    {
        Write(_root, $"versions/{id}/{id}.json", Profile(id, indexId, index).ToJsonString());
        Write(_root, $"versions/{id}/{id}.jar", Client);
        InstallManifestStore.Save(_root, id, new InstallManifest
        {
            Components = new() { ["minecraft"] = "26.3" },
            ManagedFiles =
            [
                new($"versions/{id}/{id}.json", FileUtil.Sha1(Path.Combine(_root, $"versions/{id}/{id}.json")), "merged"),
                new("assets/indexes/" + indexId + ".json", Hash(index), "assets")
            ]
        });
    }

    private static JsonObject Profile(string id, string indexId, string index) => new()
    {
        ["id"] = id,
        ["clientVersion"] = "26.3",
        ["mainClass"] = "net.minecraft.client.main.Main",
        ["minecraftArguments"] = "--assetIndex ${assets_index_name}",
        ["assets"] = "34",
        ["assetIndex"] = new JsonObject
        {
            ["id"] = indexId,
            ["sha1"] = Hash(index),
            ["size"] = Encoding.UTF8.GetByteCount(index),
            ["url"] = "https://example.invalid/new-index.json"
        },
        ["downloads"] = new JsonObject
        {
            ["client"] = new JsonObject { ["path"] = id + ".jar", ["sha1"] = Hash(Client), ["size"] = Encoding.UTF8.GetByteCount(Client) }
        },
        ["libraries"] = new JsonArray()
    };

    private static string Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
    private SortedDictionary<string, string> Snapshot() => new(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_root, path), FileUtil.Sha1));
    private void AssertClean()
    {
        Assert.Empty(Directory.EnumerateDirectories(_root, ".wmcl-instance-install-*"));
        Assert.Empty(Directory.EnumerateDirectories(_root, ".wmcl-components-*"));
    }
    private static void Report(CombinedInstallStep step, CombinedInstallStepState state, JobProgress progress) { }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class Backend : ICombinedInstallBackend
    {
        public string? Root { get; private set; }
        public string? Mode { get; init; }
        public ICombinedInstallBackend ForRoot(string root) { Root = root; return this; }
        public Task InstallVanillaAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct)
        {
            var index = JsonNode.Parse(Index(NewAsset))!;
            if (Mode == "virtual") index["virtual"] = true;
            if (Mode == "resources") index["map_to_resources"] = true;
            if (Mode == "object-path") index["objects"] = new JsonObject { ["../escape"] = new JsonObject { ["hash"] = Hash(NewAsset), ["size"] = NewAsset.Length } };
            var indexText = Mode is "virtual" or "resources" or "object-path" ? index.ToJsonString() : Index(NewAsset);
            var profile = Profile(plan.Minecraft, Mode == "id-path" ? "../34" : "34", indexText);
            if (Mode == "hash") profile["assetIndex"]!["sha1"] = new string('a', 40);
            if (Mode == "size") profile["assetIndex"]!["size"] = 1;
            if (Mode == "unverified") profile["assetIndex"]!.AsObject().Remove("sha1");
            if (Mode == "unrelated") profile.Remove("assetIndex");
            Write(Root!, $"versions/{plan.Minecraft}/{plan.Minecraft}.json", profile.ToJsonString());
            Write(Root!, $"versions/{plan.Minecraft}/{plan.Minecraft}.jar", Client);
            if (Mode != "missing") Write(Root!, "assets/indexes/34.json", indexText);
            Write(Root!, "assets/objects/" + Hash(NewAsset)[..2] + "/" + Hash(NewAsset), NewAsset);
            if (Mode == "library") Write(Root!, "libraries/fixture.jar", "new library");
            return Task.CompletedTask;
        }
        public Task CompleteInstanceAsync(string instanceId, IProgress<JobProgress> progress, CancellationToken ct) => Task.CompletedTask;
        public Task<string> InstallLoaderAsync(CombinedInstallPlan plan, string stagingRoot, IProgress<JobProgress> progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> DownloadOptiFineAsync(CombinedInstallPlan plan, IProgress<JobProgress> progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> InstallModAsync(string project, CombinedInstallPlan plan, string modsDirectory, IProgress<JobProgress> progress, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
