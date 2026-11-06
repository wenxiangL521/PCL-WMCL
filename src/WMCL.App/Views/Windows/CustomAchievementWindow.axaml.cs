using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace WMCL.App.Views;

public partial class CustomAchievementWindow : Window
{
    public CustomAchievementWindow()
    {
        InitializeComponent();
        TitleBox.TextChanged += (_, _) => PreviewTitle.Text = string.IsNullOrWhiteSpace(TitleBox.Text)
            ? "成就达成！" : TitleBox.Text;
        DescriptionBox.TextChanged += (_, _) => PreviewDescription.Text = string.IsNullOrWhiteSpace(DescriptionBox.Text)
            ? "完成了一项了不起的成就" : DescriptionBox.Text;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存成就图片",
            SuggestedFileName = $"achievement-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG 图片") { Patterns = ["*.png"] }]
        });
        if (file is null) return;
        using var bitmap = new RenderTargetBitmap(
            new Avalonia.PixelSize((int)PreviewToast.Bounds.Width, (int)PreviewToast.Bounds.Height),
            new Avalonia.Vector(96, 96));
        bitmap.Render(PreviewToast);
        await using var stream = await file.OpenWriteAsync();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
    }
}
