using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Core;
using WMCL.Download;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Modpack;

namespace WMCL.App.Tests;

public sealed class ModpackImportWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-pack-ui-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentDictionary<string, byte[]> _responses = new();
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly HttpClient _http;
    private readonly VersionRepository _repository;
    private readonly ModpackInstaller _modrinth;
    private readonly CurseForgeModpackInstaller _curseforge;
    private readonly LocalModpackInstaller _local;

    public ModpackImportWorkflowTests()
    {
        _http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            _requests.Enqueue(path);
            return _responses.TryGetValue(path, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        _repository = new VersionRepository(_root);
        var downloads = new DownloadManager(_http);
        var installer = new VersionInstaller(_repository, downloads);
        _modrinth = new ModpackInstaller(_repository, installer, downloads: downloads);
        _curseforge = new CurseForgeModpackInstaller(_repository, installer, client: new CurseForgeClient("key", _http), downloads: downloads);
        _local = new LocalModpackInstaller(_repository, installer,
            curseForge: new CurseForgeClient("key", _http), downloads: downloads);
        var vanilla = _repository.GetVersionDirectory("1.20.1");
        Directory.CreateDirectory(vanilla);
        var client = Encoding.UTF8.GetBytes("client fixture");
        _responses["/client.jar"] = client;
        File.WriteAllText(Path.Combine(vanilla, "1.20.1.json"), JsonSerializer.Serialize(new
        {
            id = "1.20.1",
            mainClass = "net.minecraft.client.main.Main",
            libraries = Array.Empty<object>(),
            downloads = new { client = new { url = "https://example.invalid/client.jar", sha1 = Hash(client), size = client.Length } }
        }));
        foreach (var id in new[] { 222, 223, 224 })
        {
            var fileName = id + ".jar";
            var bytes = Encoding.UTF8.GetBytes(fileName);
            _responses["/" + fileName] = bytes;
            _responses[$"/v1/mods/111/files/{id}"] = JsonSerializer.SerializeToUtf8Bytes(new
            {
                data = new
                {
                    id,
                    modId = 111,
                    displayName = id == 223 ? "Accepted" : id == 224 ? "Rejected" : "Required",
                    fileName,
                    fileLength = bytes.Length,
                    downloadUrl = "https://example.invalid/" + fileName,
                    hashes = new[] { new { algo = 1, value = Hash(bytes) } }
                }
            });
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task LocalAndRemoteCurseForgeImportsUseTheSameOptionalDecisions(bool local, bool includeOptional)
    {
        var prompts = new List<string>();
        var workflow = Workflow((title, message, _) =>
        {
            Assert.Equal("下载可选文件", title);
            prompts.Add(message);
            return Task.FromResult(message.Contains("Accepted", StringComparison.Ordinal));
        });
        var path = Pack();
        var id = local ? await workflow.InstallLocalAsync(path, "Imported", includeOptional)
            : await workflow.InstallCurseForgeAsync(path, "Imported", includeOptional);
        Assert.Equal(includeOptional ? 2 : 0, prompts.Count);
        var mods = Path.Combine(_repository.GetVersionDirectory(id), "mods");
        Assert.True(File.Exists(Path.Combine(mods, "222.jar")));
        Assert.Equal(includeOptional, File.Exists(Path.Combine(mods, "223.jar")));
        Assert.False(File.Exists(Path.Combine(mods, "224.jar")));
        Assert.DoesNotContain("/224.jar", _requests);
        if (!includeOptional)
        {
            Assert.DoesNotContain("/v1/mods/111/files/223", _requests);
            Assert.DoesNotContain("/v1/mods/111/files/224", _requests);
        }
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [Fact]
    public async Task LocalInstallCommandCapturesOptionalPolicyAndNameBeforeAwaitingDialogs()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = Workflow((_, message, _) =>
        {
            if (!message.Contains("Accepted", StringComparison.Ordinal)) return Task.FromResult(false);
            entered.SetResult();
            return pending.Task;
        });
        var services = new AppServices(new AppSettings { GameDirectory = _root });
        var viewModel = new ModpackViewModel(services, workflow)
        {
            LocalPath = Pack(),
            IncludeOptionalFiles = true,
            InstanceName = "Captured"
        };
        var installed = 0;
        viewModel.InstanceInstalled += (_, _) => installed++;
        var install = viewModel.InstallLocalCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(viewModel.IsBusy);
        viewModel.IncludeOptionalFiles = false;
        viewModel.InstanceName = "Changed";
        viewModel.LocalPath = "missing.zip";
        pending.SetResult(true);
        await install;
        Assert.Equal("整合包安装完成：Captured", viewModel.Status);
        Assert.Equal(1, installed);
        Assert.True(File.Exists(Path.Combine(_repository.GetVersionDirectory("Captured"), "mods", "223.jar")));
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Changed")));
    }

    [Fact]
    public async Task CancellingDuringOptionalChoiceStopsBeforeInstallingOrAskingAgain()
    {
        using var cancellation = new CancellationTokenSource();
        var prompts = 0;
        var workflow = Workflow((_, _, _) =>
        {
            prompts++;
            cancellation.Cancel();
            return Task.FromResult(true);
        });
        await Assert.ThrowsAsync<OperationCanceledException>(() => workflow.InstallLocalAsync(Pack(), "Cancelled", true, ct: cancellation.Token));
        Assert.Equal(1, prompts);
        Assert.DoesNotContain("/client.jar", _requests);
        Assert.False(Directory.Exists(_repository.GetVersionDirectory("Cancelled")));
    }

    [Fact]
    public async Task DisablingOptionalFilesAlsoAppliesToLocalModrinthPacks()
    {
        var path = Path.Combine(_root, "local.mrpack");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var nested = new StreamWriter(archive.CreateEntry("overrides/config/manifest.json").Open())) nested.Write("{}");
            using var writer = new StreamWriter(archive.CreateEntry("modrinth.index.json").Open());
            writer.Write("""
                {"formatVersion":1,"game":"minecraft","name":"Pack","versionId":"1","dependencies":{"minecraft":"1.20.1"},
                 "files":[{"path":"mods/optional.jar","env":{"client":"optional"},"hashes":{"sha1":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"downloads":["https://example.invalid/optional.jar"]}]}
                """);
        }
        var workflow = Workflow((_, _, _) => throw new InvalidOperationException("Should not ask"));
        var id = await workflow.InstallLocalAsync(path, "Modrinth", false);
        Assert.DoesNotContain("/optional.jar", _requests);
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    private ModpackImportWorkflow Workflow(Func<string, string, CancellationToken, Task<bool>> confirm) =>
        new(_modrinth, _curseforge, _local, confirm);

    [AvaloniaTheory]
    [InlineData("modrinth")]
    [InlineData("curseforge")]
    public async Task VersionSelectionUsesTheSharedFormatDispatcherAndCompletesTheImport(string format)
    {
        var path = format == "curseforge" ? Pack() : CreateModrinthPack();
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var workflow = Workflow((_, _, _) => Task.FromResult(false));
        var model = new VersionSelectViewModel(new LaunchViewModel(services), services, () => { }, () => { },
            createImports: root =>
            {
                Assert.Equal(_root, root);
                Assert.Contains("导入本地整合包", services.GameOperations.GetActiveOperation(root));
                return workflow;
            });
        var installed = 0;
        model.InstanceInstalled += (_, _) => installed++;
        await model.ImportLocalAsync(path);
        Assert.StartsWith("已导入实例：", model.ImportStatus);
        Assert.Equal(1, installed);
        Assert.False(model.IsImporting);
        Assert.Equal("导入整合包", model.ImportButtonText);
        Assert.Null(services.GameOperations.GetActiveOperation(_root));
    }

    [AvaloniaFact]
    public async Task VersionSelectionRejectsImportBeforeCreatingAWorkflowWhileTheRootIsBusy()
    {
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var model = new VersionSelectViewModel(new LaunchViewModel(services), services, () => { }, () => { },
            createImports: _ => throw new InvalidOperationException("Should not create workflow"));
        using var operation = services.GameOperations.Acquire(_root, "运行游戏 first");
        await model.ImportLocalAsync(Pack());
        Assert.Contains("运行游戏 first", model.ImportStatus);
        Assert.DoesNotContain("/client.jar", _requests);
        Assert.False(model.IsImporting);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VersionSelectionCapturesTheRootAndCancelsOptionalPromptsWithoutInstalling(bool switchRoot)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = Workflow(async (_, _, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return true;
        });
        var services = new AppServices(new AppSettings { GameDirectory = _root }, saveSettings: _ => { });
        var model = new VersionSelectViewModel(new LaunchViewModel(services), services, () => { }, () => { },
            createImports: root =>
            {
                Assert.Equal(_root, root);
                return workflow;
            });
        var installed = 0;
        model.InstanceInstalled += (_, _) => installed++;
        var importing = model.ImportLocalAsync(Pack());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(model.IsImporting);
        Assert.Equal("取消导入", model.ImportButtonText);
        Assert.Contains("导入本地整合包", services.GameOperations.GetActiveOperation(_root));
        if (switchRoot) services.SetGameDirectory(Path.Combine(_root, "different"));
        await model.ImportLocalAsync("duplicate.mrpack");
        model.CancelImportCommand.Execute(null);
        await importing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.IsImporting);
        Assert.Equal(0, installed);
        Assert.DoesNotContain("/client.jar", _requests);
        Assert.Null(services.GameOperations.GetActiveOperation(_root));
        if (!switchRoot) Assert.Equal("已取消导入", model.ImportStatus);
    }

    private string CreateModrinthPack()
    {
        var path = Path.Combine(_root, "selection.mrpack");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry("modrinth.index.json").Open());
        writer.Write("""
            {"formatVersion":1,"game":"minecraft","name":"Selection","versionId":"1","dependencies":{"minecraft":"1.20.1"},"files":[]}
            """);
        return path;
    }

    [Fact]
    public async Task LocalMcbbsImportCompletesDeclaredContentWithoutOptionalDialogs()
    {
        var path = Path.Combine(_root, "mcbbs.zip");
        var bytes = Encoding.UTF8.GetBytes("remote config");
        _responses["/content/overrides/config/test.txt"] = bytes;
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("wrapped/mcbbs.packmeta").Open());
            writer.Write(JsonSerializer.Serialize(new
            {
                addons = new[] { new { id = "game", version = "1.20.1" } },
                fileApi = "https://example.invalid/content",
                files = new object[]
                {
                    new { type = "addon", path = "config/test.txt", hash = Hash(bytes) },
                    new { type = "curse", projectID = 111, fileID = 222 }
                }
            }));
        }
        var workflow = Workflow((_, _, _) => throw new InvalidOperationException("MCBBS files are not optional"));
        var id = await workflow.InstallLocalAsync(path, "Mcbbs", includeOptional: false);
        var directory = _repository.GetVersionDirectory(id);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(directory, "config", "test.txt")));
        Assert.True(File.Exists(Path.Combine(directory, "mods", "222.jar")));
        Assert.True((await InstallManifestStore.VerifyAsync(_root, id)).IsHealthy);
    }

    [AvaloniaFact]
    public async Task CancellingPclConfirmationClosesTheModalAndPropagatesCancellation()
    {
        var owner = new Window { Width = 600, Height = 400 };
        owner.Show();
        try
        {
            using var cancellation = new CancellationTokenSource();
            var confirm = DialogService.ConfirmAsync(owner, "下载可选文件", "是否下载？", cancellation.Token);
            await Task.Delay(30);
            Assert.Single(owner.OwnedWindows);
            cancellation.Cancel();
            await Task.Delay(30);
            await Assert.ThrowsAsync<OperationCanceledException>(() => confirm);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            foreach (var window in owner.OwnedWindows.ToArray()) window.Close();
            owner.Close();
        }
    }

    private string Pack()
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
        writer.Write("""
            {"minecraft":{"version":"1.20.1"},"files":[{"projectID":111,"fileID":222,"required":true},
             {"projectID":111,"fileID":223,"required":false},{"projectID":111,"fileID":224,"required":false}]}
            """);
        return path;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA1.HashData(bytes));

    public void Dispose()
    {
        _http.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
