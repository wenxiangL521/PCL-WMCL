namespace WMCL.App.ViewModels.Instances;

/// <summary>实例窗口尺寸设置（null 表示跟随全局）。</summary>
public readonly record struct InstanceWindowState(bool UseGlobal, decimal Width, decimal Height)
{
    public static InstanceWindowState From(int? width, int? height, int fallbackWidth, int fallbackHeight) =>
        new(width is null && height is null,
            width ?? fallbackWidth,
            height ?? fallbackHeight);

    /// <summary>写回设置：跟随全局时两者都为 null。</summary>
    public (int? Width, int? Height) ToSettings() =>
        UseGlobal ? (null, null) : ((int)Width, (int)Height);

    public bool CanEdit => !UseGlobal;
}
