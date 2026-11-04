using WMCL.App.ViewModels.Mods;

namespace WMCL.App.Tests;

public sealed class LocalModsControllerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wmcl-modctl-" + Guid.NewGuid().ToString("N"));

    public LocalModsControllerTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "sodium.jar"), "x");
        File.WriteAllText(Path.Combine(_dir, "iris.jar"), "x");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void ScanListsJarsAndBuildsView()
    {
        var controller = new LocalModsController();
        controller.Scan(_dir);
        Assert.Equal(2, controller.Mods.Count);
        Assert.Equal(2, controller.View.Count);
        Assert.Contains("2 个 Mod", controller.Status);
        Assert.NotNull(controller.Selected);
    }

    [Fact]
    public void FilterNarrowsViewButKeepsSource()
    {
        var controller = new LocalModsController();
        controller.Scan(_dir);
        controller.Filter = "sodium";
        Assert.Single(controller.View);
        Assert.Equal(2, controller.Mods.Count);
        Assert.Contains("显示 1/2", controller.Status);

        controller.Filter = "";
        Assert.Equal(2, controller.View.Count);
    }

    [Fact]
    public void SetEnabledRenamesDisabledJarsAndRescans()
    {
        var controller = new LocalModsController();
        controller.Scan(_dir);
        var target = controller.Mods.First(mod => mod.FileName == "sodium.jar");
        Assert.Equal(1, controller.SetEnabled(_dir, [target], false));
        Assert.Contains("已禁用", controller.Status);
        Assert.True(File.Exists(Path.Combine(_dir, "sodium.jar.disabled")));
        Assert.False(File.Exists(Path.Combine(_dir, "sodium.jar")));

        var disabled = controller.Mods.First(mod => mod.FileName == "sodium.jar.disabled");
        Assert.Equal(1, controller.SetEnabled(_dir, [disabled], true));
        Assert.True(File.Exists(Path.Combine(_dir, "sodium.jar")));
    }

    [Fact]
    public void DeleteRemovesFileAndRescans()
    {
        var controller = new LocalModsController();
        controller.Scan(_dir);
        var target = controller.Mods.First(mod => mod.FileName == "iris.jar");
        Assert.Equal(1, controller.Delete(_dir, [target]));
        Assert.Contains("已删除", controller.Status);
        Assert.False(File.Exists(Path.Combine(_dir, "iris.jar")));
        Assert.Single(controller.Mods);
    }

    [Fact]
    public void ValidateRejectsModsFromAnotherDirectory()
    {
        var controller = new LocalModsController();
        controller.Scan(_dir);
        var other = Path.Combine(Path.GetTempPath(), "wmcl-other-" + Guid.NewGuid().ToString("N"));
        Assert.Empty(controller.Validate(other, controller.Mods));
        Assert.Equal(2, controller.Validate(_dir, controller.Mods).Length);
    }

    [Fact]
    public async Task ScanAsyncPopulatesSameResult()
    {
        var controller = new LocalModsController();
        await controller.ScanAsync(_dir);
        Assert.Equal(2, controller.Mods.Count);
    }
}
