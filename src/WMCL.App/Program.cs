using Avalonia;
using WMCL.App.Services;
using WMCL.Core;

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

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
