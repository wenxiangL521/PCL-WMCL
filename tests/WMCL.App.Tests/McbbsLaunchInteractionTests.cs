using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed class McbbsLaunchInteractionTests
{
    [AvaloniaTheory]
    [InlineData("success")]
    [InlineData("not-required")]
    [InlineData("http")]
    [InlineData("hash")]
    [InlineData("unsupported")]
    [InlineData("cancel")]
    [InlineData("no-api")]
    public async Task LaunchButtonChecksRequiredContentBeforeTheNormalLaunchPipeline(string outcome)
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-launch-update-ui-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "versions", "Pack");
        Directory.CreateDirectory(Path.Combine(directory, "config"));
        var target = Path.Combine(directory, "config", "test.txt");
        await File.WriteAllTextAsync(target, "original");
        await File.WriteAllTextAsync(Path.Combine(directory, "Pack.json"), """{"id":"Pack","inheritsFrom":"Pack"}""");
        InstallManifestStore.Save(root, "Pack", new InstallManifest
        {
            ModpackKind = "MCBBS",
            ModpackManifestJson = Manifest("1.20.1", "original", outcome != "not-required", outcome != "no-api"),
            Components = new Dictionary<string, string> { ["minecraft"] = "1.20.1", ["local-pack"] = "Mcbbs" },
            ManagedFiles = [new("versions/Pack/config/test.txt", Hash("original"), "overrides")]
        });
        var originalInstall = await File.ReadAllTextAsync(InstallManifestStore.GetPath(root, "Pack"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remote = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            Interlocked.Increment(ref requests);
            if (request.RequestUri!.AbsolutePath.EndsWith("/manifest.json", StringComparison.Ordinal))
            {
                entered.TrySetResult();
                await remote.Task.WaitAsync(ct);
                return new HttpResponseMessage(outcome == "http" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                {
                    Content = new StringContent(Manifest(outcome == "unsupported" ? "1.21" : "1.20.1", "updated", true, true))
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(outcome == "hash" ? "invalid" : "updated")
            };
        }));
        var services = new AppServices(new AppSettings { GameDirectory = root },
            saveSettings: _ => { }, modpackUpdateHttp: http);
        using var main = new MainWindowViewModel(services);
        var viewModel = main.Launch;
        viewModel.IsMicrosoftAccount = true;
        viewModel.IsAuthlibAccount = false;
        viewModel.SelectedVersion = new VersionItem { Id = "Pack", IsInstalled = true };
        var view = new LaunchView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 850, Height = 600 };
        window.Show();
        try
        {
            await PumpAsync();
            var launch = view.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("launchButton"));
            Click(window, launch);
            var execution = viewModel.LaunchCommand.ExecutionTask!;
            if (outcome is not ("not-required" or "no-api"))
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await PumpAsync();
                Assert.True(viewModel.IsBusy);
                Assert.True(viewModel.IsCheckingModpackUpdate);
                Assert.False(viewModel.IsGameRunning);
                Assert.False(viewModel.CanLaunch);
                Assert.False(launch.IsEffectivelyEnabled);
                Assert.True(main.IsTaskActive);
                Assert.Equal("启动前检查整合包", main.TaskText);
                var task = Assert.Single(main.Tasks.Tasks, task => task.Title == "启动前检查整合包");
                Assert.True(task.HasCancel);
                Click(window, launch);
                Assert.Same(execution, viewModel.LaunchCommand.ExecutionTask);
                Assert.Equal(1, requests);
                if (outcome == "cancel")
                {
                    var cancel = view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "取消"));
                    Click(window, cancel);
                }
                else remote.SetResult();
            }
            await execution.WaitAsync(TimeSpan.FromSeconds(15));
            await PumpAsync();
            Assert.False(viewModel.IsBusy);
            Assert.False(viewModel.CanCancel);
            Assert.False(viewModel.IsCheckingModpackUpdate);
            Assert.False(viewModel.IsGameRunning);
            Assert.True(viewModel.CanLaunch);
            Assert.Equal(0, viewModel.Progress);
            Assert.Equal("", viewModel.ProgressText);
            Assert.DoesNotContain(main.Tasks.Tasks, task => task.Title == "启动前检查整合包");
            if (outcome is "success" or "not-required")
            {
                Assert.False(viewModel.HasModpackLaunchError);
                Assert.Contains("版本继承出现循环", viewModel.Status);
                Assert.Equal(outcome == "success" ? "updated" : "original", await File.ReadAllTextAsync(target));
                if (outcome == "success") Assert.Contains("启动前更新检查完成", viewModel.Log);
                else Assert.Equal(0, requests);
            }
            else
            {
                Assert.Contains(outcome == "cancel" ? "已取消整合包检查，未启动游戏" : "整合包启动前检查失败，未启动游戏", viewModel.Status);
                Assert.DoesNotContain("版本继承出现循环", viewModel.Status);
                Assert.Equal("original", await File.ReadAllTextAsync(target));
                Assert.Equal(originalInstall, await File.ReadAllTextAsync(InstallManifestStore.GetPath(root, "Pack")));
                Assert.Equal(outcome != "cancel", viewModel.HasModpackLaunchError);
                if (outcome == "unsupported") Assert.Contains("作为新实例导入", viewModel.ModpackLaunchError);
                if (outcome == "no-api") Assert.Equal(0, requests);
                if (outcome == "http")
                {
                    var retry = view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "重试启动"));
                    Assert.True(retry.IsEffectivelyVisible);
                    outcome = "success";
                    Click(window, retry);
                    await viewModel.LaunchCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(5));
                    await PumpAsync();
                    Assert.False(viewModel.HasModpackLaunchError);
                    Assert.Equal("updated", await File.ReadAllTextAsync(target));
                    Assert.Contains("版本继承出现循环", viewModel.Status);
                }
            }
        }
        finally
        {
            viewModel.CancelCommand.Execute(null);
            remote.TrySetResult();
            if (viewModel.LaunchCommand.ExecutionTask is { } pending) await pending;
            window.Close();
            Directory.Delete(root, true);
        }
    }

    private static string Manifest(string game, string content, bool required, bool api) => JsonSerializer.Serialize(new
    {
        manifestType = "minecraftModpack",
        manifestVersion = 1,
        forceUpdate = required,
        fileApi = api ? "https://example.invalid/pack" : null,
        addons = new[] { new { id = "game", version = game } },
        files = new[] { new { type = "addon", path = "config/test.txt", hash = Hash(content), force = false } }
    });

    private static string Hash(string content) => Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(content)));

    private static void Click(Window window, Control control)
    {
        var position = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        Assert.InRange(position.Y, 0, window.Bounds.Height);
        SendInput(window, "MouseMove", position, RawInputModifiers.None);
        SendInput(window, "MouseDown", position, MouseButton.Left, RawInputModifiers.None);
        SendInput(window, "MouseUp", position, MouseButton.Left, RawInputModifiers.None);
    }

    private static void SendInput(Window window, string method, params object?[] arguments) =>
        typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!
            .GetMethod(method)!.Invoke(window.PlatformImpl, arguments);

    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 10; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
