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
using WMCL.Core;
using WMCL.Minecraft;
using WMCL.Modding.Modpack;

namespace WMCL.App.Tests;

public sealed class McbbsUpdateInteractionTests
{
    [AvaloniaTheory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("unsupported")]
    public async Task InstanceUpdateButtonsExecuteAndCancelProductionService(string outcome)
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-update-ui-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "versions", "Pack");
        Directory.CreateDirectory(Path.Combine(directory, "config"));
        var target = Path.Combine(directory, "config", "test.txt");
        await File.WriteAllTextAsync(target, "original");
        await File.WriteAllTextAsync(Path.Combine(directory, "Pack.json"),
            """{"id":"Pack","clientVersion":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[]}""");
        var original = Manifest("1.20.1", "original");
        InstallManifestStore.Save(root, "Pack", new InstallManifest
        {
            ModpackKind = "MCBBS",
            ModpackManifestJson = original,
            Components = new Dictionary<string, string> { ["minecraft"] = "1.20.1", ["local-pack"] = "Mcbbs" },
            ManagedFiles = [new("versions/Pack/config/test.txt", Hash("original"), "overrides")]
        });
        var originalInstall = await File.ReadAllTextAsync(InstallManifestStore.GetPath(root, "Pack"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remote = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/manifest.json", StringComparison.Ordinal))
            {
                entered.TrySetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(await remote.Task.WaitAsync(ct))
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("updated") };
        }));
        var services = new AppServices(new AppSettings { GameDirectory = root });
        using var viewModel = new InstanceViewModel(services, new McbbsModpackUpdater(services.Repository, http: http));
        var view = new InstanceView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 1000, Height = 800 };
        window.Show();
        try
        {
            Assert.False(viewModel.IsMcbbsModpack);
            Assert.False(viewModel.UpdateModpackContentCommand.CanExecute(null));
            viewModel.SelectedInstance = new VersionItem { Id = "Pack", IsInstalled = true };
            await PumpAsync();
            Assert.True(viewModel.IsMcbbsModpack);
            view.FindControl<ListBox>("SectionList")!.SelectedIndex = 4;
            var update = view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "更新整合包内容"));
            await ScrollToAsync(update);
            Assert.True(update.IsEnabled, viewModel.ManifestStatus);
            Click(window, update);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await PumpAsync();
            var execution = viewModel.UpdateModpackContentCommand.ExecutionTask!;
            Assert.True(viewModel.IsUpdatingModpack);
            Assert.False(viewModel.CanRepairManifest);
            Assert.False(update.IsEffectivelyEnabled);
            if (outcome == "cancel")
            {
                var cancel = view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "取消整合包更新"));
                Assert.True(cancel.IsVisible);
                await ScrollToAsync(cancel);
                Click(window, cancel);
            }
            else remote.SetResult(Manifest(outcome == "unsupported" ? "1.21" : "1.20.1", "updated"));
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(50);
            Assert.False(viewModel.IsUpdatingModpack);
            Assert.True(viewModel.CanRepairManifest);
            Assert.Contains(outcome switch
            {
                "success" => "更新完成：下载 1",
                "cancel" => "更新已取消",
                _ => "更新失败："
            }, viewModel.ManifestStatus);
            Assert.Equal(outcome == "success" ? "updated" : "original", await File.ReadAllTextAsync(target));
            if (outcome != "success")
                Assert.Equal(originalInstall, await File.ReadAllTextAsync(InstallManifestStore.GetPath(root, "Pack")));
            viewModel.SelectedInstance = null;
            Assert.False(viewModel.IsMcbbsModpack);
            Assert.False(viewModel.UpdateModpackContentCommand.CanExecute(null));
        }
        finally
        {
            viewModel.UpdateModpackContentCommand.Cancel();
            if (viewModel.UpdateModpackContentCommand.ExecutionTask is { } pending) await pending;
            window.Close();
            Directory.Delete(root, true);
        }
    }

    private static string Manifest(string game, string content) => JsonSerializer.Serialize(new
    {
        manifestType = "minecraftModpack",
        manifestVersion = 1,
        name = "Test",
        version = "2.0",
        fileApi = "https://example.invalid/pack",
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

    private static async Task ScrollToAsync(Control control)
    {
        await PumpAsync();
        var scroll = control.GetVisualAncestors().OfType<ScrollViewer>().First();
        var position = control.TranslatePoint(default, scroll)!.Value;
        scroll.Offset = new Vector(scroll.Offset.X,
            Math.Clamp(scroll.Offset.Y + position.Y - 100, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
        await PumpAsync();
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
