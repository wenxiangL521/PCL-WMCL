using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Launch.Auth;

namespace WMCL.App.Tests;

public sealed class MicrosoftDeviceCodeActionsTests
{
    private static MicrosoftAccount Account => new("Player", "uuid", "access-token", "refresh-token", DateTimeOffset.UtcNow.AddHours(1));
    private static AppServices Services() => new(new AppSettings { MicrosoftClientId = "public-client" }, saveSettings: _ => { });

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeviceCodeAndUriCanArriveSeparatelyAndAutomaticActionsRunExactlyOnce(bool uriFirst)
    {
        var auth = new ControlledAuth();
        var actions = new List<string>();
        using var model = new MicrosoftLoginViewModel(Services(), auth.LoginAsync,
            (code, _) => { actions.Add("copy:" + code); return Task.CompletedTask; },
            (url, _) => { actions.Add("open:" + url); return Task.CompletedTask; });
        var login = model.StartCommand.ExecuteAsync(null);
        try
        {
            auth.Progress!.Report(uriFirst
                ? new MicrosoftAuthProgress("等待授权", VerificationUri: "https://microsoft.com/link")
                : new MicrosoftAuthProgress("等待授权", UserCode: "ABCD-EFGH"));
            Pump();
            Assert.Empty(actions);
            auth.Progress.Report(uriFirst
                ? new MicrosoftAuthProgress("等待微软登录", UserCode: "ABCD-EFGH")
                : new MicrosoftAuthProgress("等待微软登录", VerificationUri: "https://microsoft.com/link"));
            Pump();
            await model.AutomaticActionsTask;
            Assert.Equal(["copy:ABCD-EFGH", "open:https://microsoft.com/link"], actions);
            Assert.Equal("等待微软登录", model.Status);
            Assert.Contains("已复制", model.ClipboardStatus);
            Assert.Contains("已打开", model.BrowserStatus);
            auth.Progress.Report(new MicrosoftAuthProgress("授权轮询", "ABCD-EFGH", "https://microsoft.com/link"));
            Pump();
            Assert.Equal(2, actions.Count);
            Assert.Equal("授权轮询", model.Status);
            await model.CopyUserCodeCommand.ExecuteAsync(null);
            await model.OpenVerificationCommand.ExecuteAsync(null);
            Assert.Equal(4, actions.Count);
        }
        finally { model.CancelCommand.Execute(null); auth.Completion.TrySetCanceled(); await login; }
    }

    [AvaloniaFact]
    public async Task BrowserWaitsForClipboardCompletionAndManualActionsRecoverPlatformFailures()
    {
        var auth = new ControlledAuth();
        var clipboard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var copies = 0;
        var opens = 0;
        using var model = new MicrosoftLoginViewModel(Services(), auth.LoginAsync,
            (_, _) => ++copies == 1 ? clipboard.Task : Task.CompletedTask,
            (_, _) => ++opens == 1 ? Task.FromException(new IOException("browser unavailable")) : Task.CompletedTask);
        var login = model.StartCommand.ExecuteAsync(null);
        try
        {
            auth.Progress!.Report(new MicrosoftAuthProgress("等待授权", "CODE", "https://microsoft.com/link"));
            Pump();
            Assert.Equal(1, copies);
            Assert.Equal(0, opens);
            clipboard.SetResult();
            await model.AutomaticActionsTask;
            Assert.Equal(1, opens);
            Assert.Contains("失败", model.BrowserStatus);
            await model.OpenVerificationCommand.ExecuteAsync(null);
            await model.CopyUserCodeCommand.ExecuteAsync(null);
            Assert.Equal(2, opens);
            Assert.Equal(2, copies);
            Assert.Contains("已打开", model.BrowserStatus);
            Assert.Contains("已复制", model.ClipboardStatus);
            Assert.Equal("等待授权", model.Status);
        }
        finally { clipboard.TrySetResult(); model.CancelCommand.Execute(null); auth.Completion.TrySetCanceled(); await login; }
    }

    [AvaloniaFact]
    public async Task SynchronouslyCompletedLoginDiscardsDeviceProgressAlreadyQueuedForTheUi()
    {
        var actions = 0;
        using var model = new MicrosoftLoginViewModel(Services(), (_, progress, _) =>
        {
            progress.Report(new MicrosoftAuthProgress("等待授权", "CODE", "https://microsoft.com/link"));
            return Task.FromResult(Account);
        }, (_, _) => { actions++; return Task.CompletedTask; }, (_, _) => { actions++; return Task.CompletedTask; });
        await model.StartCommand.ExecuteAsync(null);
        Pump();
        Assert.Equal(0, actions);
        Assert.Equal("", model.UserCode);
        Assert.Equal(Account.Name, model.Account?.Name);
    }

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PlatformFailuresAreSeparateAndClipboardFailureStillOpensTheBrowser(bool copyFails, bool browserFails)
    {
        var auth = new ControlledAuth();
        var copied = 0;
        var opened = 0;
        using var model = new MicrosoftLoginViewModel(Services(), auth.LoginAsync,
            (_, _) =>
            {
                copied++;
                return copyFails ? Task.FromException(new IOException("platform error")) : Task.CompletedTask;
            }, (_, _) =>
            {
                opened++;
                return browserFails ? Task.FromException(new IOException("platform error")) : Task.CompletedTask;
            });
        var login = model.StartCommand.ExecuteAsync(null);
        try
        {
            auth.Progress!.Report(new MicrosoftAuthProgress("等待微软登录", "CODE", "https://www.microsoft.com/devicelogin"));
            Pump();
            await model.AutomaticActionsTask;
            Assert.Equal(1, copied);
            Assert.Equal(1, opened);
            Assert.Equal(copyFails, model.ClipboardStatus.Contains("失败"));
            Assert.Equal(browserFails, model.BrowserStatus.Contains("失败"));
            Assert.Equal("等待微软登录", model.Status);
        }
        finally { model.CancelCommand.Execute(null); auth.Completion.TrySetCanceled(); await login; }
    }

    [AvaloniaFact]
    public async Task RetryAllowsOneNewAutomaticAttemptAndRejectsProgressFromThePreviousSession()
    {
        var first = new ControlledAuth();
        var second = new ControlledAuth();
        var requests = 0;
        var copies = new List<string>();
        var opens = 0;
        using var model = new MicrosoftLoginViewModel(Services(),
            (id, progress, ct) => (++requests == 1 ? first : second).LoginAsync(id, progress, ct),
            (code, _) => { copies.Add(code); return Task.CompletedTask; },
            (_, _) => { opens++; return Task.CompletedTask; });
        var initial = model.StartCommand.ExecuteAsync(null);
        first.Progress!.Report(new MicrosoftAuthProgress("等待第一次授权", "FIRST", "https://microsoft.com/link"));
        Pump();
        first.Completion.SetException(new TimeoutException("expired"));
        await initial;
        Assert.True(model.CanRetry);
        var retry = model.StartCommand.ExecuteAsync(null);
        try
        {
            Assert.Equal("", model.UserCode);
            first.Progress.Report(new MicrosoftAuthProgress("迟到旧会话", "OLD", "https://microsoft.com/link"));
            Pump();
            Assert.Equal("", model.UserCode);
            second.Progress!.Report(new MicrosoftAuthProgress("等待第二次授权", "SECOND", "https://microsoft.com/link"));
            Pump();
            await model.AutomaticActionsTask;
            Assert.Equal(["FIRST", "SECOND"], copies);
            Assert.Equal(2, opens);
        }
        finally { model.CancelCommand.Execute(null); second.Completion.TrySetCanceled(); await retry; }
    }

    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("dispose")]
    [InlineData("success")]
    public async Task LateProgressAndPendingClipboardContinuationCannotTriggerBrowserAfterSessionEnds(string terminal)
    {
        var auth = new ControlledAuth();
        var clipboard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opens = 0;
        var copies = 0;
        using var model = new MicrosoftLoginViewModel(Services(), auth.LoginAsync,
            (_, _) => { copies++; return clipboard.Task; },
            (_, _) => { opens++; return Task.CompletedTask; });
        var login = model.StartCommand.ExecuteAsync(null);
        auth.Progress!.Report(new MicrosoftAuthProgress("等待授权", "CODE", "https://microsoft.com/link"));
        Pump();
        Assert.Equal(1, copies);
        if (terminal == "dispose") model.Dispose();
        else if (terminal == "cancel") model.CancelCommand.Execute(null);
        if (terminal == "success") auth.Completion.SetResult(Account);
        else auth.Completion.SetCanceled();
        await login;
        var previousStatus = model.Status;
        auth.Progress.Report(new MicrosoftAuthProgress("不应显示", "LATE", "https://microsoft.com/link"));
        Pump();
        clipboard.SetResult();
        await model.AutomaticActionsTask;
        Assert.Equal(0, opens);
        Assert.Equal(1, copies);
        Assert.Equal("CODE", model.UserCode);
        Assert.Equal(previousStatus, model.Status);
        Assert.Equal("", model.ClipboardStatus);
        await model.OpenVerificationCommand.ExecuteAsync(null);
        await model.CopyUserCodeCommand.ExecuteAsync(null);
        Assert.Equal(0, opens);
        Assert.Equal(1, copies);
    }

    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("dispose")]
    [InlineData("success")]
    public async Task ProgressQueuedBeforeTerminalStateIsDiscardedBeforeAnyPlatformAction(string terminal)
    {
        var auth = new ControlledAuth();
        var actions = 0;
        using var model = new MicrosoftLoginViewModel(Services(), auth.LoginAsync,
            (_, _) => { actions++; return Task.CompletedTask; }, (_, _) => { actions++; return Task.CompletedTask; });
        var login = model.StartCommand.ExecuteAsync(null);
        if (terminal != "success")
            auth.Progress!.Report(new MicrosoftAuthProgress("排队的设备码", "LATE", "https://microsoft.com/link"));
        if (terminal == "cancel") model.CancelCommand.Execute(null);
        else if (terminal == "dispose") model.Dispose();
        if (terminal == "success") auth.Completion.SetResult(Account);
        else auth.Completion.SetCanceled();
        await login;
        if (terminal == "success")
            auth.Progress!.Report(new MicrosoftAuthProgress("排队的设备码", "LATE", "https://microsoft.com/link"));
        Pump();
        Assert.Equal(0, actions);
        Assert.Equal("", model.UserCode);
    }

    [AvaloniaFact]
    public async Task UntrustedAddressIsNotOpenedAutomaticallyOrManually()
    {
        var auth = new ControlledAuth();
        var actions = 0;
        using var model = new MicrosoftLoginViewModel(Services(), auth.LoginAsync,
            (_, _) => { actions++; return Task.CompletedTask; }, (_, _) => { actions++; return Task.CompletedTask; });
        var login = model.StartCommand.ExecuteAsync(null);
        try
        {
            auth.Progress!.Report(new MicrosoftAuthProgress("等待授权", "CODE", "https://microsoft.com.evil.invalid/link"));
            Pump();
            Assert.Equal(0, actions);
            Assert.False(model.HasVerificationUri);
            Assert.Contains("阻止", model.BrowserStatus);
            model.VerificationUri = "file:///unsafe";
            await model.OpenVerificationCommand.ExecuteAsync(null);
            Assert.Equal(0, actions);
            Assert.Equal("等待授权", model.Status);
        }
        finally { model.CancelCommand.Execute(null); auth.Completion.TrySetCanceled(); await login; }
    }

    [AvaloniaFact]
    public async Task ReattachingTheSameViewDoesNotRequestOrOpenAnotherDeviceCode()
    {
        var auth = new ControlledAuth();
        var requests = 0;
        var copies = 0;
        var opens = 0;
        using var model = new MicrosoftLoginViewModel(Services(), (id, progress, ct) =>
        {
            requests++;
            return auth.LoginAsync(id, progress, ct);
        }, (_, _) => { copies++; return Task.CompletedTask; }, (_, _) => { opens++; return Task.CompletedTask; });
        var view = new MicrosoftLoginView { DataContext = model };
        var window = new Window { Content = view, Width = 560, Height = 480 };
        try
        {
            window.Show();
            Pump();
            auth.Progress!.Report(new MicrosoftAuthProgress("等待授权", "CODE", "https://microsoft.com/link"));
            Pump();
            await model.AutomaticActionsTask;
            window.Content = null;
            Pump();
            window.Content = view;
            Pump();
            await model.EnsureStartedAsync();
            Assert.Equal(1, requests);
            Assert.Equal(1, copies);
            Assert.Equal(1, opens);
            Assert.Equal("等待授权", model.Status);
        }
        finally
        {
            model.CancelCommand.Execute(null);
            auth.Completion.TrySetCanceled();
            await model.StartCommand.ExecutionTask!;
            window.Close();
        }
    }

    [Theory]
    [InlineData("https://microsoft.com/link", true)]
    [InlineData("https://www.microsoft.com/link", true)]
    [InlineData("https://microsoft.com/devicelogin", true)]
    [InlineData("https://www.microsoft.com/devicelogin", true)]
    [InlineData("http://microsoft.com/link", false)]
    [InlineData("file:///login", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://microsoft.com.evil.invalid/link", false)]
    [InlineData("https://microsoft.com@evil.invalid/link", false)]
    [InlineData("https://user@microsoft.com/link", false)]
    [InlineData("https://microsoft.com:8443/link", false)]
    [InlineData("https://microsoft.com/link?redirect=evil", false)]
    [InlineData("https://microsoft.com/link#unsafe", false)]
    [InlineData("https://microsoft.com/other", false)]
    public void OnlyFixedOfficialHttpsVerificationAddressesAreAllowed(string address, bool allowed) =>
        Assert.Equal(allowed, MicrosoftLoginViewModel.TryGetOfficialVerificationUri(address, out _));

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private sealed class ControlledAuth
    {
        public IProgress<MicrosoftAuthProgress>? Progress { get; private set; }
        public TaskCompletionSource<MicrosoftAccount> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<MicrosoftAccount> LoginAsync(string clientId, IProgress<MicrosoftAuthProgress> progress, CancellationToken ct)
        {
            Progress = progress;
            return Completion.Task;
        }
    }
}
