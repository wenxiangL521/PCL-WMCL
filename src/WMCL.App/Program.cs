using WMCL.Core;
using Avalonia;
using Avalonia.Media;
using WMCL.App.Services;
using WMCL.App.Services.Infrastructure;

namespace WMCL.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupOptions.Parse(args);
        WmclPaths.EnsureCreated();
        var settings = SettingsService.Load();
        // 系统代理模式下先检测再 Apply：SystemProxyAddress 必须在 Apply 前写入，
        // 否则 Apply 拿到 null 会清空代理环境变量、静默直连。
        // 启动路径一次性同步等待（Process 检测不依赖 UI 上下文，无死锁风险）。
        var proxyMode = ProxyManager.ParseMode(settings.ProxyMode);
        if (proxyMode == ProxyMode.SystemProxy)
            ProxyManager.SystemProxyAddress = ProxyManager.NormalizeAddress(
                ProxyManager.DetectSystemProxyAsync().GetAwaiter().GetResult());
        var config = new ProxyConfig(
            proxyMode, settings.ProxyUrl,
            settings.ProxyUsername, settings.ProxyPassword,
            settings.ProxyBypassList);
        ProxyManager.Apply(config);

        // 回收站自动清理：仅在启用回收站且清理天数 > 0 时执行
        if (settings.EnableTrash && settings.TrashAutoCleanDays > 0)
        {
            try
            {
                var cleaned = TrashManager.AutoClean(settings.TrashAutoCleanDays);
                if (cleaned > 0)
                    System.Diagnostics.Trace.WriteLine($"回收站自动清理：移除 {cleaned} 个过期实例");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"回收站自动清理失败：{ex.Message}");
            }
        }

        BuildAvaloniaApp().WithWaylandIfAvailable().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Wayland 会话默认启用原生后端，初始化失败时自动回退到 UsePlatformDetect 配好的 X11；
    /// WMCL_PLATFORM=x11 可强制回退。
    /// </summary>
    private static AppBuilder WithWaylandIfAvailable(this AppBuilder builder) =>
        WMCL.App.Services.Platform.WindowBackend.ShouldUseWayland
            ? builder.UseWaylandWithFallback()
            : builder;

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            // 内置鸿蒙只覆盖拉丁字符，中文回退系统中文字体（带 hinting，小字号更清晰）
            .With(new FontManagerOptions
            {
                DefaultFamilyName = UiFontService.Resolve(SettingsService.Load())
            })
            .LogToTrace();
}
