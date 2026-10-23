using System.IO.Compression;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WMCL.App.Views;

namespace WMCL.App.Tests;

public sealed class JarInJarWindowTests
{
    [AvaloniaFact]
    public async Task ListsNestedJarMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "wmcl-jij-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "outer.jar");
        try
        {
            using (var outer = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var name in new[] { "inner.jar", "inner2.jar" })
                {
                    var nested = outer.CreateEntry("META-INF/jars/" + name);
                    using var output = nested.Open();
                    using var memory = new MemoryStream();
                    using (var inner = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        var entry = inner.CreateEntry("fabric.mod.json");
                        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                        writer.Write("""{"id":"inner","name":"Inner Mod","version":"1.0"}""");
                    }
                    memory.Position = 0;
                    memory.CopyTo(output);
                }
            }

            var window = new JarInJarWindow(path, "Outer");
            window.Show();
            try
            {
                for (var index = 0; index < 20; index++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(10);
                }
                var list = window.FindControl<ListBox>("EntryList")!;
                Assert.Equal(2, list.ItemCount);
                var status = window.FindControl<TextBlock>("StatusText")!;
                Assert.Contains("共 2 个内嵌 Jar", status.Text);
                Assert.Contains("1 条告警", status.Text);
                Assert.True(window.FindControl<Border>("WarningsPanel")!.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
