using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Launch.Auth;

namespace WMCL.App.Tests;

public sealed class MicrosoftSkinUploadViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-skin-upload-vm-" + Guid.NewGuid().ToString("N"));
    private string SkinFile => Path.Combine(_root, "chosen.png");
    private static MicrosoftAccount Account(string name = "First") => new(name, name + "-uuid", "token", "refresh", DateTimeOffset.UtcNow.AddHours(1));

    public MicrosoftSkinUploadViewModelTests() => Directory.CreateDirectory(_root);

    [AvaloniaFact]
    public async Task UploadShowsBusyAndUsesTheChosenVariantExactlyOnce()
    {
        CopyValidSkin();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var downloads = 0;
        var account = Account();
        var model = Model(async (captured, path, variant) =>
        {
            calls++;
            Assert.Same(account, captured);
            Assert.Equal(SkinFile, path);
            Assert.Equal("slim", variant);
            await release.Task;
        }, _ => { downloads++; return Task.FromResult<string?>(SkinFile); });
        model.SetMicrosoftAccount(account);
        model.SelectedSkinVariant = "slim";
        var before = downloads;
        var upload = model.UploadMicrosoftSkinAsync(SkinFile);
        Assert.True(model.IsUploadingSkin);
        Assert.False(model.CanUploadMicrosoftSkin);
        Assert.Contains("正在上传", model.SkinUploadStatus);
        Assert.Contains("正在上传", await model.UploadMicrosoftSkinAsync(SkinFile));
        Assert.Equal(1, calls);
        release.SetResult();
        Assert.Equal("皮肤已上传并刷新", await upload);
        Assert.False(model.IsUploadingSkin);
        Assert.True(model.CanUploadMicrosoftSkin);
        Assert.Equal(before + 1, downloads);
        Assert.Equal(SkinFile, model.AccountSkinPath);
        Assert.Equal("皮肤已上传并刷新", model.AccountStatus);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionAfterAnAccountChangeCannotReplaceTheCurrentSkinOrStatus(bool fail)
    {
        CopyValidSkin();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloads = 0;
        var model = Model(async (_, _, _) =>
        {
            await release.Task;
            if (fail) throw new IOException("old account failed");
        }, _ => { downloads++; return Task.FromResult<string?>(SkinFile); });
        model.SetMicrosoftAccount(Account("First"));
        var operation = model.UploadMicrosoftSkinAsync(SkinFile);
        model.SetMicrosoftAccount(Account("Second"));
        var currentBitmap = model.AccountSkin;
        var currentStatus = model.AccountStatus;
        var before = downloads;
        release.SetResult();
        Assert.Contains("账号已切换", await operation);
        Assert.Equal("Second", model.PlayerName);
        Assert.Equal(before, downloads);
        Assert.Same(currentBitmap, model.AccountSkin);
        Assert.Equal(currentStatus, model.AccountStatus);
        Assert.Equal("", model.SkinUploadStatus);
        Assert.False(model.IsUploadingSkin);
    }

    [AvaloniaFact]
    public async Task SwitchingOfflineDoesNotUseTheStoredMicrosoftAccountForUploads()
    {
        CopyValidSkin();
        var calls = 0;
        var model = Model((_, _, _) => { calls++; return Task.CompletedTask; });
        model.SetMicrosoftAccount(Account());
        model.SelectOfflineAccountCommand.Execute(null);
        Assert.Equal("请先登录微软账号", await model.UploadMicrosoftSkinAsync(SkinFile));
        Assert.Equal(0, calls);
        Assert.False(model.CanUploadMicrosoftSkin);
    }

    [AvaloniaTheory]
    [InlineData("bad-png")]
    [InlineData("missing-file")]
    [InlineData("expired")]
    [InlineData("bad-variant")]
    public async Task InvalidUploadIsVisibleAndRejectedBeforeTheInjectedNetworkCall(string problem)
    {
        CopyValidSkin();
        var calls = 0;
        var model = Model((_, _, _) => { calls++; return Task.CompletedTask; });
        model.SetMicrosoftAccount(problem == "expired" ? Account() with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) } : Account());
        if (problem == "bad-png") await File.WriteAllTextAsync(SkinFile, "not PNG");
        if (problem == "bad-variant") model.SelectedSkinVariant = "unsupported";
        var result = await model.UploadMicrosoftSkinAsync(problem == "missing-file" ? SkinFile + ".missing" : SkinFile);
        Assert.StartsWith("上传皮肤失败", result);
        Assert.Equal(result, model.AccountStatus);
        Assert.Equal(result, model.SkinUploadStatus);
        Assert.False(model.IsUploadingSkin);
        Assert.Equal(0, calls);
    }

    [AvaloniaFact]
    public async Task UploadAndPreviewRefreshFailuresNeverClaimThatTheSkinWasUnchangedOrSilentlySucceeded()
    {
        CopyValidSkin();
        var model = Model((_, _, _) => throw new IOException("remote upload error"));
        model.SetMicrosoftAccount(Account());
        var result = await model.UploadMicrosoftSkinAsync(SkinFile);
        Assert.Contains("remote upload error", result);
        Assert.Equal(result, model.AccountStatus);
        Assert.False(model.IsUploadingSkin);

        var refresh = Model((_, _, _) => Task.CompletedTask,
            _ => throw new IOException("preview refresh error"));
        refresh.SetMicrosoftAccount(Account("Refresh"));
        result = await refresh.UploadMicrosoftSkinAsync(SkinFile);
        Assert.Contains("已上传", result);
        Assert.Contains("预览尚未更新", result);
        Assert.DoesNotContain("并刷新", result);
    }

    [AvaloniaFact]
    public async Task ActiveVariantMetadataDoesNotOverrideTheUsersChosenUploadVariant()
    {
        CopyValidSkin();
        var model = Model((_, _, _) => Task.CompletedTask,
            details: _ => Task.FromResult(new MicrosoftSkinDownload(SkinFile, "SLIM")));
        model.SelectedSkinVariant = "classic";
        model.SetMicrosoftAccount(Account());
        Assert.Equal("slim", model.CurrentSkinVariant);
        Assert.Equal("classic", model.SelectedSkinVariant);
        await model.RefreshSkinCommand.ExecuteAsync(null);
        Assert.Equal("slim", model.CurrentSkinVariant);
        Assert.Equal("classic", model.SelectedSkinVariant);
    }

    private LaunchViewModel Model(Func<MicrosoftAccount, string, string, Task> upload,
        Func<MicrosoftAccount, Task<string?>>? download = null,
        Func<MicrosoftAccount, Task<MicrosoftSkinDownload>>? details = null) => new(
            new AppServices(new AppSettings { AccountType = "offline", GameDirectory = _root }, saveSettings: _ => { }),
            null, null, Path.Combine(_root, Guid.NewGuid().ToString("N")),
            download ?? (_ => Task.FromResult<string?>(null)), _ => Task.FromResult<string?>(null),
            uploadMicrosoftSkin: upload, downloadMicrosoftSkinWithInfo: details);

    private void CopyValidSkin()
    {
        using var input = AssetLoader.Open(new Uri("avares://WMCL.App/Assets/Skins/Steve.png"));
        using var output = File.Create(SkinFile);
        input.CopyTo(output);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
