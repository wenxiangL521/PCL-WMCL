using PCL.Launch;

namespace PCL.Launch.Tests;

public class ServerLauncherTests
{
    [Fact]
    public async Task StartsStopsAndForwardsOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-server-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "fake-java.sh");
        await File.WriteAllTextAsync(script, """
            #!/bin/sh
            echo READY
            while read line; do
              echo "GOT:$line"
              [ "$line" = "stop" ] && exit 0
            done
            """);
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                                     UnixFileMode.UserExecute);
        await File.WriteAllBytesAsync(Path.Combine(root, "server.jar"), []);
        var output = new List<string>();
        var ready = new TaskCompletionSource();
        var exited = new TaskCompletionSource<int>();
        using var launcher = new ServerLauncher();
        launcher.OutputReceived += line =>
        {
            lock (output) output.Add(line);
            if (line == "READY") ready.TrySetResult();
        };
        launcher.Exited += code => exited.TrySetResult(code);
        try
        {
            launcher.Start(script, root, "server.jar");
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(launcher.IsRunning);
            Assert.True(await launcher.StopAsync(TimeSpan.FromSeconds(10)));
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains(output, line => line == "GOT:stop");
            Assert.False(launcher.IsRunning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
