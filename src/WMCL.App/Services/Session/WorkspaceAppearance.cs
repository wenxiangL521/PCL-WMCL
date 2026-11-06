using Avalonia.Media.Imaging;

namespace WMCL.App.Services;

/// <summary>
/// 主窗口的背景图 / 自定义 Logo 与背景参数。加载失败时降级为无图，
/// 不再让 MainWindowViewModel 直接处理文件探测与 Bitmap 解码。
/// </summary>
public sealed class WorkspaceAppearance
{
    private static readonly string[] BackgroundExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    public WorkspaceAppearance(AppSettings settings)
    {
        LogoImage = LoadImage(settings.CustomLogoPath?.Trim());
        LogoText = string.IsNullOrWhiteSpace(settings.CustomLogoText)
            ? "WMCL"
            : settings.CustomLogoText.Trim();
        BackgroundImage = LoadImage(ResolveBackground(settings));
        Opacity = Math.Clamp(settings.BackgroundImageOpacity, 0, 100) / 100d;
        Blur = Math.Clamp(settings.BackgroundImageBlur, 0, 30);
        ColorOverlay = settings.BackgroundColorOverlay;
    }

    public Bitmap? LogoImage { get; }

    /// <summary>标题栏文字：自定义文字或默认 WMCL。</summary>
    public string LogoText { get; }

    public Bitmap? BackgroundImage { get; }

    public double Opacity { get; }

    public double Blur { get; }

    public bool ColorOverlay { get; }

    private static Bitmap? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveBackground(AppSettings settings)
    {
        var folder = settings.BackgroundImageFolder?.Trim();
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            var images = Directory.EnumerateFiles(folder)
                .Where(file => BackgroundExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .ToList();
            if (images.Count > 0) return images[Random.Shared.Next(images.Count)];
        }
        return settings.BackgroundImagePath?.Trim();
    }
}
