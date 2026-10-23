namespace WMCL.App.Animation;

/// <summary>
/// 统一的动画时长 token。参考 PCL-ME 的 MotionDurations，
/// 所有页面/控件过渡从这里取值，避免每个控件各写一套时间。
/// </summary>
public static class MotionDurations
{
    public static TimeSpan Interactive => Duration(120);
    public static TimeSpan InstanceName => Duration(150);
    public static TimeSpan PageTransition => Duration(160);
    public static TimeSpan Entrance => Duration(220);
    public static TimeSpan Modal => Duration(180);

    private static TimeSpan Duration(int milliseconds) =>
        Motion.ReducedMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(milliseconds);
}
