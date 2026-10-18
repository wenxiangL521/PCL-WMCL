using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WMCL.App.ViewModels;

public sealed partial class VersionItem : ObservableObject
{
    public required string Id { get; init; }
    public string Type { get; init; } = "release";
    public string? ReleaseTime { get; init; }
    public bool IsLatest { get; init; }

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _category = "";


    [ObservableProperty]
    private Bitmap? _logo;

    public bool HasLogo => Logo is not null;

    partial void OnLogoChanged(Bitmap? value) => OnPropertyChanged(nameof(HasLogo));

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
    public string FavoriteMark => IsFavorite ? "★" : "";
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(StatusText));

    partial void OnIsFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavoriteMark));

    partial void OnDescriptionChanged(string value) =>
        OnPropertyChanged(nameof(HasDescription));

    private static string FormatTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "未知时间";
        return DateTimeOffset.TryParse(value, out var time)
            ? time.LocalDateTime.ToString("yyyy-MM-dd")
            : value;
    }
}
