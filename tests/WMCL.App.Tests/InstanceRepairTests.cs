using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Minecraft;

namespace WMCL.App.Tests;

public sealed class InstanceRepairTests
{
    [AvaloniaFact]
    public async Task InstanceRepairPreservesPlayedSavesAndExplainsTheVerificationPolicy()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-repair-ui-" + Guid.NewGuid().ToString("N"));
        var instance = Path.Combine(root, "versions", "Pack");
        var world = Path.Combine(instance, "saves", "World");
        Directory.CreateDirectory(world);
        var archive = Path.Combine(root, "pack.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("overrides/saves/World/level.dat").Open())) writer.Write("original");
            using var region = new StreamWriter(zip.CreateEntry("overrides/saves/World/region/r.0.0.mca").Open());
            region.Write("original region");
        }
        await File.WriteAllTextAsync(Path.Combine(instance, "Pack.json"),
            """{"id":"Pack","clientVersion":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[]}""");
        await File.WriteAllTextAsync(Path.Combine(world, "level.dat"), "player progress");
        Directory.CreateDirectory(Path.Combine(world, "region"));
        var regionPath = Path.Combine(world, "region", "r.0.0.mca");
        await File.WriteAllTextAsync(regionPath, "original region");
        InstallManifestStore.Save(root, "Pack", new InstallManifest
        {
            SourceArchive = archive,
            OverridesRoot = instance,
            OverridesPrefixes = ["overrides/"],
            ManagedFiles = [new("versions/Pack/saves/World/level.dat", Hash("original"), "overrides"),
                new("versions/Pack/saves/World/region/r.0.0.mca", Hash("original region"), "overrides")]
        });
        var services = new AppServices(new AppSettings { GameDirectory = root });
        using var viewModel = new InstanceViewModel(services);
        var view = new InstanceView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 1000, Height = 800 };
        window.Show();
        try
        {
            viewModel.SelectedInstance = new VersionItem { Id = "Pack", IsInstalled = true };
            view.FindControl<ListBox>("SectionList")!.SelectedIndex = 4;
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                block => block.Text == "已有存档文件仅检查是否存在，修复不会覆盖游玩进度。");
            Assert.Contains(view.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "修复游戏与整合包文件"));
            await viewModel.VerifyManifestCommand.ExecuteAsync(null);
            Assert.Contains("清单正常", viewModel.ManifestStatus);
            Assert.Contains("仅检查是否存在", viewModel.ManifestStatus);
            File.Delete(regionPath);
            await viewModel.RepairManifestCommand.ExecuteAsync(null);
            Assert.Contains("修复完成，清单正常", viewModel.ManifestStatus);
            Assert.Contains("保留 2 个已有存档文件", viewModel.ManifestStatus);
            Assert.Equal("player progress", await File.ReadAllTextAsync(Path.Combine(world, "level.dat")));
            Assert.Equal("original region", await File.ReadAllTextAsync(regionPath));
        }
        finally
        {
            window.Close();
            Directory.Delete(root, true);
        }
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(text)));

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepairKeepsItsTargetAndAllowsCancellation(bool cancel)
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-repair-switch-" + Guid.NewGuid().ToString("N"));
        var originalRoot = Path.Combine(root, "original");
        var nextRoot = Path.Combine(root, "next");
        var original = Path.Combine(originalRoot, "versions", "Pack");
        var next = Path.Combine(nextRoot, "versions", "Other");
        Directory.CreateDirectory(original);
        Directory.CreateDirectory(next);
        var profile = """{"id":"Pack","clientVersion":"1.20.1","mainClass":"net.minecraft.client.main.Main","libraries":[]}""";
        await File.WriteAllTextAsync(Path.Combine(next, "Other.json"), "keep other instance");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var address = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/profile.json";
        InstallManifestStore.Save(originalRoot, "Pack", new InstallManifest
        {
            ManagedFiles = [new("versions/Pack/Pack.json", Hash(profile), "merged", address)]
        });
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var server = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
            received.SetResult();
            await release.Task.WaitAsync(timeout.Token);
            var body = Encoding.UTF8.GetBytes(profile);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), timeout.Token);
            await stream.WriteAsync(body, timeout.Token);
        });
        var services = new AppServices(new AppSettings { GameDirectory = originalRoot }, saveSettings: _ => { });
        using var viewModel = new InstanceViewModel(services);
        viewModel.SelectedInstance = new VersionItem { Id = "Pack", IsInstalled = true };
        Task? repair = null;
        try
        {
            repair = viewModel.RepairManifestCommand.ExecuteAsync(null);
            await received.Task.WaitAsync(timeout.Token);
            if (cancel)
                viewModel.RepairManifestCancelCommand.Execute(null);
            else
            {
                services.SetGameDirectory(nextRoot);
                viewModel.SelectedInstance = new VersionItem { Id = "Other", IsInstalled = true };
                viewModel.ManifestStatus = "selected other instance";
            }
            release.SetResult();
            await repair.WaitAsync(timeout.Token);
            try { await server; }
            catch (IOException) when (cancel) { }
            if (cancel)
            {
                Assert.False(File.Exists(Path.Combine(original, "Pack.json")));
                Assert.Equal("已取消文件修复", viewModel.ManifestStatus);
            }
            else
            {
                Assert.Equal(profile, await File.ReadAllTextAsync(Path.Combine(original, "Pack.json")));
                Assert.Equal("selected other instance", viewModel.ManifestStatus);
                Assert.True((await InstallManifestStore.VerifyAsync(originalRoot, "Pack")).IsHealthy);
            }
            Assert.Equal("keep other instance", await File.ReadAllTextAsync(Path.Combine(next, "Other.json")));
            Assert.False(Directory.Exists(Path.Combine(nextRoot, "versions", "Pack")));
        }
        finally
        {
            release.TrySetResult();
            if (repair is not null) await repair;
            listener.Stop();
            try { await server; }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
            catch (IOException) when (cancel) { }
            Directory.Delete(root, true);
        }
    }
}
