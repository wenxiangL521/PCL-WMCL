using Avalonia;
using Avalonia.Controls;

namespace WMCL.App.Services.Platform;

/// <summary>
/// 窗口后端判定：是否为 Wayland 会话、是否实际跑在原生 Wayland 上、是否被强制回退 X11。
/// 判定只在这里做一次，避免 Program / App / MainWindow 三处各写一份环境变量判断。
/// </summary>
public static class WindowBackend
{
    private static bool? _nativeWayland;

    /// <summary>是否为 Wayland 会话（XDG_SESSION_TYPE 或 WAYLAND_DISPLAY）。</summary>
    public static bool IsWaylandSession =>
        OperatingSystem.IsLinux() &&
        (string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase) ||
         !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")));

    /// <summary>WMCL_PLATFORM=x11 时强制回退 X11。</summary>
    public static bool ForcedX11 =>
        string.Equals(Environment.GetEnvironmentVariable("WMCL_PLATFORM"), "x11", StringComparison.OrdinalIgnoreCase);

    /// <summary>是否应启用原生 Wayland（Wayland 会话且未被强制回退）。</summary>
    public static bool ShouldUseWayland => OperatingSystem.IsLinux() && IsWaylandSession && !ForcedX11;

    /// <summary>
    /// 当前窗口是否实际使用原生 Wayland 后端。首次调用后缓存：
    /// 窗口的 PlatformImpl 只有在创建后才存在，之后不会在两种后端之间切换。
    /// </summary>
    public static bool IsNativeWayland(TopLevel? topLevel = null)
    {
        if (_nativeWayland is { } cached) return cached;
        if (topLevel?.PlatformImpl is { } impl)
        {
            _nativeWayland = impl.GetType().FullName?.Contains("Avalonia.Wayland", StringComparison.Ordinal) == true;
            return _nativeWayland.Value;
        }
        // 还没有窗口时按会话推断：Wayland 会话且未强制 X11 视为原生 Wayland。
        return ShouldUseWayland;
    }
}
