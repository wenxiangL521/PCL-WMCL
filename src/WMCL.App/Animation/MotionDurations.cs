namespace WMCL.App.Animation;

/// <summary>
/// 统一的动画时长 token。参考 PCL-ME 的 MotionDurations，
/// 所有页面/控件过渡从这里取值，避免每个控件各写一套时间。
/// </summary>
public static class MotionDurations
{
    public static readonly TimeSpan Interactive = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan PageTransition = TimeSpan.FromMilliseconds(160);
    public static readonly TimeSpan Entrance = TimeSpan.FromMilliseconds(220);
}
