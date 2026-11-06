using WMCL.App.Services;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class MusicPlaybackAvailabilityTests
{
    [Fact]
    public void AvailabilityProbeNeverThrows()
    {
        // Windows 走 MCI，必然可用；其它平台取决于 ffplay 是否存在，只要求探测本身不抛异常
        var supported = MusicService.IsSupported;
        if (OperatingSystem.IsWindows()) Assert.True(supported);
    }

    [Fact]
    public void StartingWithoutPlaybackBackendIsSilent()
    {
        var service = new MusicService();
        var folder = Path.Combine(WMCL.Tests.TestEnvironment.Root, "music-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.mp3"), "x");
        service.Start(folder, 50);   // 无播放器时不应抛异常
        service.Stop();
        service.Dispose();
    }
}
