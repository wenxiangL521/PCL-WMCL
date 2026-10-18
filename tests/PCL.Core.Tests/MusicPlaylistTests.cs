using PCL.Core.Music;

namespace PCL.Core.Tests;

public class MusicPlaylistTests
{
    [Fact]
    public void EnumeratesSupportedExtensionsInOrder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pcl-music-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "b.mp3"), "");
            File.WriteAllText(Path.Combine(folder, "a.ogg"), "");
            File.WriteAllText(Path.Combine(folder, "ignore.txt"), "");
            var files = MusicPlaylist.Enumerate(folder);
            Assert.Equal(["a.ogg", "b.mp3"], files.Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
