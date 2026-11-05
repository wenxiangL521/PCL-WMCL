using Avalonia.Controls;
using Avalonia.VisualTree;
using WMCL.App.Services;

namespace WMCL.App.Tests;

public sealed class OfflineSkinStoreTests
{
    [Fact]
    public void SavesAndLoadsSkinPerPlayerName()
    {
        var name = "Offline_" + Guid.NewGuid().ToString("N")[..8];
        Assert.False(OfflineSkinStore.Exists(name));

        var path = OfflineSkinStore.Save(name, [1, 2, 3, 4]);
        Assert.True(File.Exists(path));
        Assert.True(OfflineSkinStore.Exists(name));
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(path));

        OfflineSkinStore.Delete(name);
        Assert.False(OfflineSkinStore.Exists(name));
    }

    [Fact]
    public void SanitizesPlayerNameIntoFileName()
    {
        var path = OfflineSkinStore.PathFor("a/b:c*d");
        Assert.DoesNotContain('/', Path.GetFileName(path));
        Assert.DoesNotContain(':', Path.GetFileName(path));
    }

    [Fact]
    public void ApplyingWithoutSkinReportsFalse()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "skin-apply-" + Guid.NewGuid().ToString("N"));
        Assert.False(OfflineSkinStore.TryApplyToInstance("NoSkin_" + Guid.NewGuid().ToString("N")[..6], root));
    }

    [Fact]
    public void ApplyingCopiesToCustomSkinLoaderDirectory()
    {
        var name = "Apply_" + Guid.NewGuid().ToString("N")[..8];
        OfflineSkinStore.Save(name, [9, 9, 9]);
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "skin-inst-" + Guid.NewGuid().ToString("N"));
        Assert.True(OfflineSkinStore.TryApplyToInstance(name, root));
        Assert.True(File.Exists(Path.Combine(root, "CustomSkinLoader", "LocalSkin", "skins", name + ".png")));
        OfflineSkinStore.Delete(name);
    }
}

public sealed class OfflineSkinWindowLayoutTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void WindowReusesPreviewAndModelControls()
    {
        var window = new WMCL.App.Views.OfflineSkinWindow("TestPlayer");
        try
        {
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var preview = window.GetVisualDescendants().OfType<WMCL.App.Controls.SkinModelPreview>().FirstOrDefault();
            Assert.NotNull(preview);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "正面"));
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "背面"));
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "编辑皮肤"));
        }
        finally { window.Close(); }
    }
}
