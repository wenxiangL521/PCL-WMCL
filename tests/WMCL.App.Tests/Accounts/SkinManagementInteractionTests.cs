using System.Net;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Launch.Auth;

namespace WMCL.App.Tests;

[Trait("Kind", "UiInteraction")]
public sealed class SkinManagementInteractionTests : IDisposable
{
    private readonly string _root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "skin-management-" + Guid.NewGuid().ToString("N"));
    private string SkinPath => Path.Combine(_root, "skin.png");
    private static MicrosoftAccount Account => new("SkinUser", "uuid", "token", "refresh", DateTimeOffset.MaxValue);

    public SkinManagementInteractionTests()
    {
        Directory.CreateDirectory(_root);
        using var stream = AssetLoader.Open(new Uri("avares://WMCL.App/Assets/Skins/Steve.png"));
        using var target = File.Create(SkinPath);
        stream.CopyTo(target);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectingSkinPreviewsFirstAndOnlyConfirmationUploads(bool portalFile)
    {
        var uploads = 0;
        var picks = 0;
        string? uploadedPath = null;
        var model = Model((_, path, variant) =>
        {
            uploads++;
            uploadedPath = path;
            Assert.True(File.Exists(path));
            Assert.Equal("slim", variant);
            return Task.CompletedTask;
        });
        using var file = new SkinFileSelection(portalFile ? null : SkinPath, () => Task.FromResult<Stream>(File.OpenRead(SkinPath)));
        var window = new MsSkinWindow(() => { picks++; return Task.FromResult<SkinFileSelection?>(file); }, (_, _) => Task.FromResult<Bitmap?>(null))
        { DataContext = model };
        try
        {
            window.Show();
            await PumpAsync();
            model.SelectedSkinVariant = "slim";
            window.FindControl<Button>("MsUploadButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.SkinSelectionTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, picks);
            Assert.True(window.FindControl<Border>("UploadSelectionPanel")!.IsVisible);
            var preview = window.FindControl<SkinModelPreview>("CharacterPreview")!;
            Assert.NotNull(preview.Skin);
            Assert.Equal("slim", preview.Variant);
            Assert.Equal(0, uploads);
            Assert.Contains("尚未上传", window.FindControl<TextBlock>("SkinActionStatusText")!.Text);
            window.FindControl<Button>("MsConfirmUploadButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.SkinApplicationTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, uploads);
            Assert.Contains("已上传", model.SkinUploadStatus);
            Assert.NotNull(uploadedPath);
        }
        finally { window.Close(); }
        if (portalFile) Assert.False(File.Exists(uploadedPath));
        Assert.True(File.Exists(SkinPath));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PickerCancellationOrFailureShowsFallbackRatherThanSilentlyDoingNothing(bool throws)
    {
        var uploads = 0;
        var model = Model((_, _, _) => { uploads++; return Task.CompletedTask; });
        var window = new MsSkinWindow(() => throws ? Task.FromException<SkinFileSelection?>(new IOException("portal unavailable"))
            : Task.FromResult<SkinFileSelection?>(null), (_, _) => Task.FromResult<Bitmap?>(null))
        { DataContext = model };
        try
        {
            window.Show();
            window.FindControl<Button>("MsUploadButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.SkinSelectionTask;
            Assert.True(window.FindControl<Border>("UploadSelectionPanel")!.IsVisible);
            Assert.Contains(throws ? "选择皮肤失败" : "未选择文件", window.FindControl<TextBlock>("SkinActionStatusText")!.Text);
            window.FindControl<TextBox>("SkinPathBox")!.Text = SkinPath;
            window.FindControl<Button>("MsConfirmUploadButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.SkinApplicationTask;
            Assert.Equal(1, uploads);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateFileSelectionCannotModifyAClosedOrChangedAccount(bool close)
    {
        var pending = new TaskCompletionSource<SkinFileSelection?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = Model((_, _, _) => throw new InvalidOperationException("must not upload"));
        var window = new MsSkinWindow(() => pending.Task, (_, _) => Task.FromResult<Bitmap?>(null)) { DataContext = model };
        using var file = new SkinFileSelection(SkinPath, () => Task.FromResult<Stream>(File.OpenRead(SkinPath)));
        window.Show();
        try
        {
            window.FindControl<Button>("MsUploadButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (close) window.Close();
            else model.SelectOfflineAccountCommand.Execute(null);
            pending.SetResult(file);
            await window.SkinSelectionTask;
            Assert.True(string.IsNullOrEmpty(window.FindControl<TextBox>("SkinPathBox")!.Text));
            if (!close) Assert.False(window.FindControl<Border>("UploadSelectionPanel")!.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("light", 620)]
    [InlineData("dark", 820)]
    public async Task FullCharacterCapeAndPixelEditorRenderInsideTheStyledWindow(string theme, double width)
    {
        var previousTheme = Application.Current!.RequestedThemeVariant;
        ThemeService.Apply(theme);
        var model = Model((_, _, _) => Task.CompletedTask);
        var window = new MsSkinWindow(() => Task.FromResult<SkinFileSelection?>(null), (_, _) => Task.FromResult<Bitmap?>(CapeBitmap()))
        { DataContext = model, Width = width, Height = 640 };
        try
        {
            window.Show();
            await PumpAsync();
            var cape = new MicrosoftCape("preview", "测试披风", "https://textures.minecraft.net/texture/fake", false);
            model.Capes.Add(cape);
            model.SelectedCape = cape;
            await window.CapePreviewTask;
            var preview = window.FindControl<SkinModelPreview>("CharacterPreview")!;
            Assert.NotNull(preview.Skin);
            Assert.NotNull(preview.Cape);
            foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(button => button is not Avalonia.Controls.Primitives.ToggleButton && button.IsEffectivelyVisible))
                Assert.Contains("skinAction", button.Classes);
            var back = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "背面"));
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(-180, preview.Yaw);
            await PumpAsync();
            Capture(window, theme + "-cape-back");
            var edit = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "编辑皮肤"));
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await PumpAsync();
            var editor = Assert.IsType<SkinEditorWindow>(Assert.Single(window.OwnedWindows));
            var canvas = editor.FindControl<SkinPixelEditor>("LargePixelCanvas")!;
            Assert.True(canvas.IsEffectivelyVisible);
            Assert.True(canvas.Bounds.Width >= 500);
            Assert.True(canvas.Bounds.Height >= 400);
            Assert.True(editor.Editor.HasSkin);
            Assert.Contains(editor.Editor.Pixels, color => color is { A: > 0 });
            editor.Editor.SelectedColor = Colors.Red;
            editor.Editor.PaintPixel(8, 8);
            Capture(editor, theme + "-large-editor");
            editor.FindControl<Button>("AcceptEditButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.SkinEditorTask;
            Assert.NotSame(model.AccountSkin, preview.Skin);
            Assert.True(window.FindControl<Border>("UploadSelectionPanel")!.IsVisible);
            Assert.Contains("尚未上传", window.FindControl<TextBlock>("SkinActionStatusText")!.Text);
            model.SelectedCape = new MicrosoftCape("", "无披风", null, false);
            await window.CapePreviewTask;
            Assert.Null(preview.Cape);
        }
        finally
        {
            window.Close();
            ThemeService.Apply(previousTheme == ThemeVariant.Dark ? "dark" : "light");
            Application.Current.RequestedThemeVariant = previousTheme;
        }
    }

    [AvaloniaFact]
    public async Task CancellingTheLargeEditorLeavesTheAccountAndParentPreviewUnchanged()
    {
        var uploads = 0;
        var model = Model((_, _, _) => { uploads++; return Task.CompletedTask; });
        var window = new MsSkinWindow(() => Task.FromResult<SkinFileSelection?>(null), (_, _) => Task.FromResult<Bitmap?>(null)) { DataContext = model };
        try
        {
            window.Show();
            await PumpAsync();
            var preview = window.FindControl<SkinModelPreview>("CharacterPreview")!;
            var original = preview.Skin;
            var edit = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "编辑皮肤"));
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await PumpAsync();
            var editor = Assert.IsType<SkinEditorWindow>(Assert.Single(window.OwnedWindows));
            editor.Editor.SelectedColor = Colors.Red;
            editor.Editor.PaintPixel(8, 8);
            editor.Close(false);
            await window.SkinEditorTask;
            Assert.Same(original, preview.Skin);
            Assert.Same(original, model.AccountSkin);
            Assert.Equal(0, uploads);
            Assert.False(window.FindControl<Border>("UploadSelectionPanel")!.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ChangingCapeSelectionDiscardsThePreviousTextureResponse()
    {
        var first = new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = Model((_, _, _) => Task.CompletedTask);
        var window = new MsSkinWindow(null, (cape, _) => cape.Id == "first" ? first.Task : Task.FromResult<Bitmap?>(CapeBitmap())) { DataContext = model };
        try
        {
            window.Show();
            model.SelectedCape = new MicrosoftCape("first", "first", "first", false);
            var previous = window.CapePreviewTask;
            model.SelectedCape = new MicrosoftCape("second", "second", "second", false);
            await window.CapePreviewTask;
            var preview = window.FindControl<SkinModelPreview>("CharacterPreview")!;
            var current = preview.Cape;
            first.SetResult(CapeBitmap());
            await previous;
            Assert.Same(current, preview.Cape);
        }
        finally { first.TrySetResult(null); window.Close(); }
    }

    private LaunchViewModel Model(Func<MicrosoftAccount, string, string, Task> upload)
    {
        var model = new LaunchViewModel(new AppServices(new AppSettings { AccountType = "offline" }, saveSettings: _ => { }),
            null, null, Path.Combine(_root, "accounts"), _ => Task.FromResult<string?>(SkinPath), _ => Task.FromResult<string?>(null),
            getMicrosoftCapes: _ => Task.FromResult<IReadOnlyList<MicrosoftCape>>([]), uploadMicrosoftSkin: upload);
        model.SetMicrosoftAccount(Account);
        return model;
    }

    private static Bitmap CapeBitmap()
    {
        var bitmap = new WriteableBitmap(new PixelSize(64, 32), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var pixels = bitmap.Lock();
        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 64; x++) Marshal.WriteInt32(pixels.Address, y * pixels.RowBytes + x * 4, unchecked((int)0xFF2979FF));
        return bitmap;
    }
    private static void Capture(Window window, string name)
    {
        var path = Environment.GetEnvironmentVariable("WMCL_SKIN_WINDOW_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(path, name + ".png"), PngBitmapEncoderOptions.Default);
    }
    private static async Task PumpAsync()
    {
        for (var index = 0; index < 5; index++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

}
