using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WMCL.App.ViewModels;
using WMCL.Launch.Auth;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class AuthlibLoginLifecycleTests
{
    private static AuthlibAccount Account(string name) =>
        new("https://example.invalid/api/yggdrasil", name, "uuid-" + name, "token", "client");

    [AvaloniaFact]
    public async Task SuccessfulLoginSurvivesTheHostDisposingTheViewModel()
    {
        var raised = 0;
        using var model = new AuthlibLoginViewModel((_, _, _, _) => Task.FromResult(Account("Player")))
        {
            UserName = "Player",
            Password = "secret"
        };
        // 真实宿主在 LoginSucceeded 中立即释放登录 VM；旧实现随后在 finally 访问已置空字段而崩溃。
        model.LoginSucceeded += (_, _) =>
        {
            raised++;
            model.Dispose();
        };
        await model.LoginCommand.ExecuteAsync(null);
        Assert.Equal(1, raised);
        Assert.Equal("Player", model.Account!.Name);
        Assert.Contains("登录成功", model.Status);
        Assert.False(model.IsBusy);
    }

    [AvaloniaFact]
    public async Task FailedLoginKeepsThePanelOpenWithAMessage()
    {
        using var model = new AuthlibLoginViewModel((_, _, _, _) =>
            Task.FromException<AuthlibAccount>(new InvalidOperationException("认证服务器返回 403")));
        await model.LoginCommand.ExecuteAsync(null);
        Assert.Null(model.Account);
        Assert.Contains("认证服务器返回 403", model.Status);
        Assert.False(model.IsBusy);
    }

    [AvaloniaFact]
    public async Task CancelDuringLoginReportsCancellationWithoutThrowing()
    {
        var pending = new TaskCompletionSource<AuthlibAccount>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var model = new AuthlibLoginViewModel(async (_, _, _, ct) =>
        {
            await using var registration = ct.Register(() => pending.TrySetCanceled(ct));
            return await pending.Task;
        });
        var login = model.LoginCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        model.CancelCommand.Execute(null);
        await login;
        Assert.Contains("已取消", model.Status);
        Assert.False(model.IsBusy);
    }

    [AvaloniaFact]
    public async Task DisposingAfterLoginDoesNotBreakALaterAttempt()
    {
        var calls = 0;
        using var model = new AuthlibLoginViewModel((_, _, _, _) =>
            Task.FromResult(Account("P" + ++calls)));
        model.LoginSucceeded += (_, _) => model.Dispose();
        await model.LoginCommand.ExecuteAsync(null);
        model.Dispose();
        Assert.Equal(1, calls);
    }
}
