using WMCL.Launch;
using WMCL.App.Services;

namespace WMCL.App.Tests;

public sealed class AppServicesInjectionTests
{
    [Fact]
    public void CoordinatorCanBeInjectedForIsolation()
    {
        var settings = new AppSettings
        {
            GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "inject-" + Guid.NewGuid().ToString("N"))
        };
        var isolated = new GameOperationCoordinator();
        var services = new AppServices(settings, saveSettings: _ => { }, coordinator: isolated);
        Assert.Same(isolated, services.GameOperations);
        Assert.NotSame(GameOperationCoordinator.Shared, services.GameOperations);
    }

    [Fact]
    public void LauncherAndTaskStoreCanBeInjected()
    {
        var settings = new AppSettings
        {
            GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "inject3-" + Guid.NewGuid().ToString("N"))
        };
        var launcher = new GameLauncher();
        var store = new TaskRunStore();
        var services = new AppServices(settings, saveSettings: _ => { },
            gameLauncher: launcher, taskRunStore: store);
        Assert.Same(launcher, services.GameLauncher);
        Assert.Same(store, services.TaskRunStore);
    }

    [Fact]
    public void SeparateCoordinatorsDoNotBlockEachOther()
    {
        var first = new GameOperationCoordinator();
        var second = new GameOperationCoordinator();
        using (first.AcquireExclusive("安装 A"))
        {
            // 同一实例会被挡住（抛异常）
            Assert.Throws<InvalidOperationException>(() => first.AcquireExclusive("启动 B"));
            // 不同实例互不影响（多实例隔离）
            using var other = second.AcquireExclusive("安装 B");
            Assert.NotNull(other);
        }
    }

    [Fact]
    public void DefaultUsesProcessWideCoordinator()
    {
        var settings = new AppSettings
        {
            GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "inject2-" + Guid.NewGuid().ToString("N"))
        };
        var services = new AppServices(settings, saveSettings: _ => { });
        Assert.Same(GameOperationCoordinator.Shared, services.GameOperations);
    }
}
