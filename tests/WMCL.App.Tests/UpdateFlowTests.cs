using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class UpdateFlowTests
{
    [AvaloniaFact]
    public async Task InvalidManifestIsNotReportedAsLatestVersion()
    {
        using var server = new ManifestServer("""{"version":"","url":"https://example.invalid/a.tar.gz"}""");
        var model = await CheckAsync(server.Url);
        Assert.Contains("更新清单无效", model.Status);
        Assert.False(model.CanDownloadUpdate);
    }

    [AvaloniaFact]
    public async Task NewerManifestEnablesDownloadAndBadVersionExplainsComparison()
    {
        using (var server = new ManifestServer("""{"version":"99.0.0","url":"https://example.invalid/a.tar.gz"}"""))
        {
            var model = await CheckAsync(server.Url);
            Assert.Contains("发现新版本 v99.0.0", model.Status);
            Assert.True(model.CanDownloadUpdate);
        }

        using (var server = new ManifestServer("""{"version":"next","url":"https://example.invalid/a.tar.gz"}"""))
        {
            var model = await CheckAsync(server.Url);
            Assert.Contains("无法检查更新", model.Status);
            Assert.False(model.CanDownloadUpdate);
        }
    }

    private static async Task<UpdateViewModel> CheckAsync(string url)
    {
        var services = new AppServices(new AppSettings { UpdateManifestUrl = url }, saveSettings: _ => { });
        var model = new UpdateViewModel(services);
        await model.CheckCommand.ExecuteAsync(null);
        return model;
    }

    private sealed class ManifestServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _serve;
        private readonly string _json;

        public ManifestServer(string json)
        {
            _json = json;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}/latest.json";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _serve = ServeAsync();
        }

        public string Url { get; }

        private async Task ServeAsync()
        {
            try
            {
                while (_listener.IsListening)
                {
                    var context = await _listener.GetContextAsync();
                    var bytes = Encoding.UTF8.GetBytes(_json);
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
            try { _serve.Wait(TimeSpan.FromSeconds(2)); } catch { }
        }
    }
}
