using PCL.Download;

namespace PCL.Minecraft.Tests;

public class DownloadMonitorTests
{
    [Fact]
    public void ReportsSpeedAndRemainingFiles()
    {
        TransferSnapshot? seen = null;
        EventHandler<TransferSnapshot> handler = (_, snapshot) =>
        {
            if (snapshot.CurrentFile == "monitor-test") seen = snapshot;
        };
        DownloadMonitor.Updated += handler;
        try
        {
            DownloadMonitor.Report(new DownloadProgress(
                TotalFiles: 10,
                CompletedFiles: 4,
                TotalBytes: 1000,
                DownloadedBytes: 400,
                BytesPerSecond: 2048,
                CurrentFile: "monitor-test",
                MaxThreads: 8));

            Assert.NotNull(seen);
            Assert.Equal(6, seen!.RemainingFiles);
            Assert.Equal(2048, seen.BytesPerSecond);
            Assert.Equal(8, seen.MaxThreads);
        }
        finally
        {
            DownloadMonitor.Updated -= handler;
        }
    }
}
