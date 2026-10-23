using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using WMCL.Core.Jobs;
using WMCL.Modding.Modpack;

namespace WMCL.Minecraft.Tests;

public sealed partial class ModpackImportIntegrationTests
{
    [Theory]
    [InlineData("no-manifest")]
    [InlineData("other-pack")]
    [InlineData("false")]
    [InlineData("omitted")]
    public async Task McbbsLaunchSkipsNetworkWhenNoRequiredPolicyIsInstalled(string policy)
    {
        const string id = "NoRequiredUpdate";
        if (policy != "no-manifest")
        {
            var index = McbbsIndex();
            if (policy != "omitted") index["forceUpdate"] = false;
            InstallManifestStore.Save(_root, id, new InstallManifest
            {
                ModpackKind = policy == "other-pack" ? "other" : "MCBBS",
                ModpackManifestJson = policy == "other-pack" ? "invalid non-MCBBS metadata" : index.ToJsonString()
            });
        }
        var manifestPath = InstallManifestStore.GetPath(_root, id);
        var before = File.Exists(manifestPath) ? await File.ReadAllTextAsync(manifestPath) : null;
        _requests.Clear();
        Assert.Null(await McbbsUpdater().PrepareLaunchAsync(id));
        Assert.Empty(_requests);
        Assert.Equal(before, File.Exists(manifestPath) ? await File.ReadAllTextAsync(manifestPath) : null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McbbsLaunchUpdatesRequiredPacksWithoutTreatingTopLevelForceAsFileForce(bool fileForce)
    {
        var original = McbbsIndex(TextAddon("config/user.txt", "original"), TextAddon("config/normal.txt", "old"));
        original["forceUpdate"] = true;
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(original, entries:
            [("config/user.txt", "original"), ("config/normal.txt", "old")]), "Required");
        var directory = _repository.GetVersionDirectory(id);
        await File.WriteAllTextAsync(Path.Combine(directory, "config/user.txt"), "user choice");
        var updatedUser = TextAddon("config/user.txt", "new user");
        updatedUser["force"] = fileForce;
        var remote = McbbsIndex(updatedUser, TextAddon("config/normal.txt", "new"));
        remote["forceUpdate"] = true;
        remote["version"] = "2.0";
        SetMcbbsRemote(remote);
        _requests.Clear();
        var result = Assert.IsType<McbbsUpdateResult>(await McbbsUpdater().PrepareLaunchAsync(id));
        Assert.Equal("2.0", result.Version);
        Assert.Equal(fileForce ? 2 : 1, result.Downloaded);
        Assert.Equal(fileForce ? [] : new[] { "config/user.txt" }, result.Preserved);
        Assert.Equal(fileForce ? "new user" : "user choice", await File.ReadAllTextAsync(Path.Combine(directory, "config/user.txt")));
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(directory, "config/normal.txt")));
        Assert.Contains("/pack/manifest.json", _requests);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
        Assert.Equal(0, (await McbbsUpdater().PrepareLaunchAsync(id))!.Downloaded);
    }

    [Fact]
    public async Task McbbsLaunchPersistsTheRemotePolicyForTheNextLaunch()
    {
        var original = McbbsIndex();
        original["forceUpdate"] = true;
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(original), "ChangedPolicy");
        var remote = McbbsIndex();
        remote["forceUpdate"] = false;
        SetMcbbsRemote(remote);
        Assert.NotNull(await McbbsUpdater().PrepareLaunchAsync(id));
        _requests.Clear();
        Assert.Null(await McbbsUpdater().PrepareLaunchAsync(id));
        Assert.Empty(_requests);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("malformed")]
    [InlineData("no-api")]
    [InlineData("hash")]
    [InlineData("unsupported")]
    [InlineData("cancel-download")]
    [InlineData("cancel-commit")]
    public async Task McbbsLaunchRequiredUpdateFailureKeepsTheOldFilesAndPolicy(string problem)
    {
        var original = McbbsIndex(TextAddon("config/a.txt", "old"));
        original["forceUpdate"] = true;
        if (problem == "no-api") original.Remove("fileApi");
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(original,
            entries: [("config/a.txt", "old")]), "RequiredFailure");
        var before = await File.ReadAllTextAsync(InstallManifestStore.GetPath(_root, id));
        var remote = McbbsIndex(TextAddon("config/a.txt", "new"));
        remote["forceUpdate"] = true;
        if (problem == "unsupported")
            remote["addons"] = new JsonArray(new JsonObject { ["id"] = "game", ["version"] = "1.21" });
        if (problem != "http") SetMcbbsRemote(remote);
        if (problem == "malformed") _responses["/pack/manifest.json"] = Encoding.UTF8.GetBytes("invalid");
        if (problem == "hash") _responses["/pack/overrides/config/a.txt"] = Encoding.UTF8.GetBytes("bad");
        using var cancellation = new CancellationTokenSource();
        var progress = new CallbackProgress(update =>
        {
            if (problem == "cancel-download" && update.Stage == "下载整合包更新" ||
                problem == "cancel-commit" && update.Stage == "应用整合包更新" && update.Detail == "config/a.txt")
                cancellation.Cancel();
        });
        await Assert.ThrowsAnyAsync<Exception>(() => McbbsUpdater().PrepareLaunchAsync(id, progress, cancellation.Token));
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(_repository.GetVersionDirectory(id), "config/a.txt")));
        Assert.Equal(before, await File.ReadAllTextAsync(InstallManifestStore.GetPath(_root, id)));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(_repository.GetVersionDirectory(id), "WMCL"), "update-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McbbsLaunchReadsLegacyPoliciesFromTheOriginalArchiveAndMigrates(bool required)
    {
        var index = McbbsIndex();
        index["forceUpdate"] = required;
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(index, "wrapped/"), "LegacyLaunch");
        var install = InstallManifestStore.Load(_root, id)!;
        install.ModpackKind = null;
        install.ModpackManifestJson = null;
        InstallManifestStore.Save(_root, id, install);
        SetMcbbsRemote(index);
        _requests.Clear();
        Assert.Equal(required, await McbbsUpdater().PrepareLaunchAsync(id) is not null);
        Assert.Equal(required, _requests.Contains("/pack/manifest.json"));
        Assert.Equal(required, InstallManifestStore.Load(_root, id)!.ModpackManifestJson is not null);
    }

    [Fact]
    public async Task McbbsLaunchFailsClearlyIfALegacyPolicyCannotBeRecovered()
    {
        InstallManifestStore.Save(_root, "LostLegacy", new InstallManifest
        {
            Components = new Dictionary<string, string> { ["local-pack"] = "Mcbbs" }
        });
        await Assert.ThrowsAsync<FileNotFoundException>(() => McbbsUpdater().PrepareLaunchAsync("LostLegacy"));
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task McbbsLaunchAndManualUpdatesShareTheSameInstanceGate()
    {
        var index = McbbsIndex();
        index["forceUpdate"] = true;
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(index), "SerializedLaunch");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var http = new HttpClient(new McbbsLaunchHandler(async (_, ct) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(ct);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(index.ToJsonString()) };
        }));
        var first = new McbbsModpackUpdater(_repository, http: http).PrepareLaunchAsync(id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = new McbbsModpackUpdater(_repository, http: http).UpdateAsync(id);
        using var cancellation = new CancellationTokenSource();
        var cancelled = new McbbsModpackUpdater(_repository, http: http).PrepareLaunchAsync(id, ct: cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(1, calls);
        release.SetResult();
        await Task.WhenAll((Task)first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
        Assert.NotNull(await new McbbsModpackUpdater(_repository, http: http).PrepareLaunchAsync(id));
        Assert.Equal(3, calls);
    }

    private sealed class McbbsLaunchHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    [Fact]
    public async Task McbbsLaunchReportsAnHttpTimeoutAsAFailureAndRetainsThePolicy()
    {
        var index = McbbsIndex();
        index["forceUpdate"] = true;
        var id = await McbbsService().InstallFromFileAsync(McbbsPack(index), "TimedOutLaunch");
        var before = await File.ReadAllTextAsync(InstallManifestStore.GetPath(_root, id));
        using var http = new HttpClient(new McbbsLaunchHandler((_, _) => throw new TaskCanceledException()));
        var failure = await Assert.ThrowsAsync<TimeoutException>(() =>
            new McbbsModpackUpdater(_repository, http: http).PrepareLaunchAsync(id));
        Assert.Contains("超时", failure.Message);
        Assert.Equal(before, await File.ReadAllTextAsync(InstallManifestStore.GetPath(_root, id)));
    }
}
