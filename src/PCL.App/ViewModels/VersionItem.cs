using CommunityToolkit.Mvvm.ComponentModel;

namespace PCL.App.ViewModels;

public sealed partial class VersionItem : ObservableObject
{
    public required string Id { get; init; }
    public string Type { get; init; } = "release";
    public string? ReleaseTime { get; init; }
    public bool IsLatest { get; init; }

    [ObservableProperty]
    private bool _isInstalled;

    public string TypeText => Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "旧测试版",
        "old_alpha" => "旧预览版",
        _ => Type
    };

    public string Subtitle => $"{TypeText} · {FormatTime(ReleaseTime)}";

    public string StatusText => IsInstalled ? "已安装" : "";

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(StatusText));

    private static string FormatTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "未知时间";
        return DateTimeOffset.TryParse(value, out var time)
            ? time.LocalDateTime.ToString("yyyy-MM-dd")
            : value;
    }
}
