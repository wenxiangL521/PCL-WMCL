using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using WMCL.App.Services;
using WMCL.App.Services.Infrastructure;
using WMCL.App.ViewModels;
using WMCL.Launch.Auth;

namespace WMCL.App.Views;

internal sealed record SkinFileSelection(string? LocalPath, Func<Task<Stream>> OpenRead, IDisposable? Handle = null) : IDisposable
{
    public void Dispose() => Handle?.Dispose();
}

public partial class MsSkinWindow : Window
{
    private readonly Func<Task<SkinFileSelection?>> _pickSkin;
    private readonly Func<MicrosoftCape, CancellationToken, Task<Bitmap?>> _loadCape;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<string> _temporaryFiles = [];
    private CancellationTokenSource? _capeCancellation;
    private LaunchViewModel? _model;
    private Bitmap? _localPreview;
    private Bitmap? _capePreview;
    private bool _picking;
    private bool _applying;
    private bool _closed;
    private int _targetVersion;
    private SkinEditorWindow? _editorWindow;
    internal Task SkinEditorTask { get; private set; } = Task.CompletedTask;
    internal Task CapePreviewTask { get; private set; } = Task.CompletedTask;
    internal Task SkinSelectionTask { get; private set; } = Task.CompletedTask;
    internal Task SkinApplicationTask { get; private set; } = Task.CompletedTask;

    public MsSkinWindow() : this(null, null) { }

    internal MsSkinWindow(Func<Task<SkinFileSelection?>>? pickSkin,
        Func<MicrosoftCape, CancellationToken, Task<Bitmap?>>? loadCape)
    {
        InitializeComponent();
        _pickSkin = pickSkin ?? PickFromStorageAsync;
        _loadCape = loadCape ?? new SkinPreviewLoader().LoadCapeAsync;
        DataContextChanged += (_, _) => ObserveModel();
        Closed += (_, _) =>
        {
            _closed = true;
            _editorWindow?.Close(false);
            _lifetime.Cancel();
            _capeCancellation?.Cancel();
            if (_model is not null) _model.PropertyChanged -= OnModelChanged;
            CharacterPreview.Skin = null;
            CharacterPreview.Cape = null;
            _localPreview?.Dispose();
            _capePreview?.Dispose();
            if (!_applying) CleanTemporaryFiles();
        };
    }

    public MsSkinWindow(LaunchViewModel model) : this() => DataContext = model;

    private void ObserveModel()
    {
        _targetVersion++;
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = DataContext as LaunchViewModel;
        _capeCancellation?.Cancel();
        ClearLocalPreview();
        _editorWindow?.Close(false);
        UploadSelectionPanel.IsVisible = false;
        SkinActionStatusText.Text = "";
        if (_model is not null) _model.PropertyChanged += OnModelChanged;
        RefreshSkinPreview();
        CapePreviewTask = RefreshCapeAsync();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (_closed) return;
        if (change.PropertyName is nameof(LaunchViewModel.IsMicrosoftAccount) or nameof(LaunchViewModel.IsAuthlibAccount) or nameof(LaunchViewModel.PlayerName))
        {
            _targetVersion++;
            ClearLocalPreview();
            _editorWindow?.Close(false);
            UploadSelectionPanel.IsVisible = false;
            SkinActionStatusText.Text = "";
            CapePreviewTask = RefreshCapeAsync();
        }
        if (change.PropertyName is nameof(LaunchViewModel.AccountSkin) or nameof(LaunchViewModel.CurrentSkinVariant) or nameof(LaunchViewModel.SelectedSkinVariant))
            RefreshSkinPreview();
        if (change.PropertyName == nameof(LaunchViewModel.SelectedCape)) CapePreviewTask = RefreshCapeAsync();
    }

    private void RefreshSkinPreview()
    {
        var skin = _localPreview ?? _model?.AccountSkin;
        var selection = _model?.SelectedSkinVariant ?? "auto";
        var detected = skin is null ? "classic" : SkinModelDetector.Detect(skin);
        var resolved = skin is null ? (selection == "slim" ? "slim" : "classic") : SkinModelDetector.Resolve(skin, selection);
        CharacterPreview.Skin = skin;
        CharacterPreview.Variant = resolved;
        SkinModelHintText.Text = selection == "auto"
            ? $"自动识别：{resolved}（{(resolved == "slim" ? "细臂" : "粗臂")}）"
            : $"手动预览：{resolved}；贴图识别：{detected}";
    }

    private async Task RefreshCapeAsync()
    {
        _capeCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _capeCancellation = cancellation;
        var model = _model;
        var cape = model?.IsMicrosoftAccount == true ? model.SelectedCape : null;
        CharacterPreview.Cape = null;
        _capePreview?.Dispose();
        _capePreview = null;
        PreviewStatusText.Text = "";
        try
        {
            if (cape is null || string.IsNullOrWhiteSpace(cape.Id) || string.IsNullOrWhiteSpace(cape.Url)) return;
            PreviewStatusText.Text = "正在加载披风预览…";
            var bitmap = await _loadCape(cape, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested || !ReferenceEquals(model, _model) ||
                !ReferenceEquals(cape, model?.SelectedCape))
            {
                bitmap?.Dispose();
                return;
            }
            _capePreview = bitmap;
            CharacterPreview.Cape = bitmap;
            PreviewStatusText.Text = bitmap is null ? "披风暂无纹理预览" : "披风已预览，转至背面查看";
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!_closed && !cancellation.IsCancellationRequested)
                PreviewStatusText.Text = "披风预览加载失败，可重新选择；不影响账号已有披风。";
        }
        finally
        {
            if (ReferenceEquals(_capeCancellation, cancellation)) _capeCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task<SkinFileSelection?> PickFromStorageAsync()
    {
        if (!StorageProvider.CanOpen) throw new InvalidOperationException("系统文件选择器不可用，请在下方输入本地PNG路径。");
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择皮肤 PNG（64×64 或 64×32）",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG") { Patterns = ["*.png"], MimeTypes = ["image/png"] }]
        });
        var file = files.FirstOrDefault();
        return file is null ? null : new SkinFileSelection(file.TryGetLocalPath(), file.OpenReadAsync, file);
    }

    private void OnUploadSkin(object? sender, RoutedEventArgs e) => SkinSelectionTask = SelectSkinAsync();

    private async Task SelectSkinAsync()
    {
        if (_closed || _picking || _applying || _model?.CanUploadMicrosoftSkin != true) return;
        UploadSelectionPanel.IsVisible = true;
        SkinActionStatusText.Text = "请选择PNG；选择器不可用时也可直接输入本地路径。";
        _picking = true;
        var model = _model;
        var targetVersion = _targetVersion;
        try
        {
            using var file = await _pickSkin();
            if (_closed || targetVersion != _targetVersion || !ReferenceEquals(model, _model) || !model.IsMicrosoftAccount) return;
            if (file is null)
            {
                SkinActionStatusText.Text = "未选择文件；可重新选择或输入PNG路径后预览。";
                return;
            }
            var path = file.LocalPath;
            if (path is null)
            {
                path = Path.Combine(Path.GetTempPath(), "wmcl-picked-skin-" + Guid.NewGuid().ToString("N") + ".png");
                _temporaryFiles.Add(path);
                await using var source = await file.OpenRead();
                await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[8192];
                int count;
                long size = 0;
                while ((count = await source.ReadAsync(buffer, _lifetime.Token)) > 0)
                {
                    size += count;
                    if (size > 4 * 1024 * 1024) throw new InvalidDataException("皮肤PNG文件过大。");
                    await target.WriteAsync(buffer.AsMemory(0, count), _lifetime.Token);
                }
            }
            if (_closed || targetVersion != _targetVersion) return;
            SkinPathBox.Text = path;
            PrepareLocalPreview();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!_closed) SkinActionStatusText.Text = "选择皮肤失败：" + exception.Message + " 可直接输入本地PNG路径。";
        }
        finally { _picking = false; }
    }

    private bool PrepareLocalPreview()
    {
        try
        {
            var path = SkinPathBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("请先选择或输入皮肤PNG路径。");
            SkinImageValidator.ValidateMinecraftSkin(path);
            var preview = new Bitmap(path);
            CharacterPreview.Skin = null;
            _localPreview?.Dispose();
            _localPreview = preview;
            RefreshSkinPreview();
            SkinActionStatusText.Text = "已加载本地预览，尚未上传；确认模型后点击“上传并应用”。";
            return true;
        }
        catch (Exception exception)
        {
            SkinActionStatusText.Text = "无法预览皮肤：" + exception.Message;
            return false;
        }
    }

    private void OnPreviewFile(object? sender, RoutedEventArgs e) => PrepareLocalPreview();
    private void OnConfirmUpload(object? sender, RoutedEventArgs e) => SkinApplicationTask = ApplySelectedSkinAsync();

    private async Task ApplySelectedSkinAsync()
    {
        if (_closed || _applying || _model?.CanUploadMicrosoftSkin != true || !PrepareLocalPreview()) return;
        var model = _model;
        _applying = true;
        try
        {
            await model.UploadMicrosoftSkinAsync(SkinPathBox.Text!.Trim());
            if (!_closed && ReferenceEquals(model, _model)) SkinActionStatusText.Text = model.SkinUploadStatus;
        }
        finally
        {
            _applying = false;
            if (_closed) CleanTemporaryFiles();
        }
    }

    private void ClearLocalPreview()
    {
        CharacterPreview.Skin = null;
        _localPreview?.Dispose();
        _localPreview = null;
        RefreshSkinPreview();
    }

    private void OnCancelSelection(object? sender, RoutedEventArgs e)
    {
        UploadSelectionPanel.IsVisible = false;
        SkinPathBox.Text = "";
        ClearLocalPreview();
        SkinActionStatusText.Text = "已取消本地预览，账号皮肤未更改。";
    }

    private void OnEditSkin(object? sender, RoutedEventArgs e) => SkinEditorTask = OpenEditorAsync();

    private async Task OpenEditorAsync()
    {
        if (_editorWindow is { } existing) { existing.Activate(); return; }
        if (_model is not { } model) return;
        var skin = _localPreview ?? model.AccountSkin;
        if (skin is null) { SkinActionStatusText.Text = "当前没有可用皮肤，请先选择PNG或刷新。"; return; }
        var target = _targetVersion;
        var variant = CharacterPreview.Variant;
        var editor = new SkinEditorWindow(skin, variant);
        _editorWindow = editor;
        try
        {
            var accepted = await editor.ShowDialog<bool>(this);
            if (!accepted || _closed || target != _targetVersion || !ReferenceEquals(model, _model)) return;
            using var bitmap = editor.Editor.ToBitmap();
            if (bitmap is null) return;
            var path = Path.Combine(Path.GetTempPath(), "wmcl-edited-preview-" + Guid.NewGuid().ToString("N") + ".png");
            _temporaryFiles.Add(path);
            bitmap.Save(path);
            model.SelectedSkinVariant = variant;
            SkinPathBox.Text = path;
            UploadSelectionPanel.IsVisible = true;
            PrepareLocalPreview();
            SkinActionStatusText.Text = "编辑结果已应用到本地预览，尚未上传；确认后点击“上传并应用”。";
        }
        catch (Exception exception)
        {
            if (!_closed) SkinActionStatusText.Text = "编辑器错误：" + exception.Message;
        }
        finally { if (ReferenceEquals(_editorWindow, editor)) _editorWindow = null; }
    }

    private void OnFront(object? sender, RoutedEventArgs e) { CharacterPreview.ResetView(); CharacterPreview.Yaw = 0; }
    private void OnBack(object? sender, RoutedEventArgs e) { CharacterPreview.ResetView(); CharacterPreview.Yaw = 180; }
    private void OnResetView(object? sender, RoutedEventArgs e) => CharacterPreview.ResetView();

    private async void OnSaveSkin(object? sender, RoutedEventArgs e)
    {
        if (_model is not { HasAccountSkinFile: true } model) return;
        try
        {
            if (!StorageProvider.CanSave) throw new InvalidOperationException("系统保存选择器不可用。");
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存皮肤文件",
                SuggestedFileName = model.PlayerName + "-skin.png",
                DefaultExtension = "png",
                FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }]
            });
            if (file is null || _closed) return;
            await using var source = File.OpenRead(model.AccountSkinPath!);
            await using var output = await file.OpenWriteAsync();
            if (output.CanSeek) output.SetLength(0);
            await source.CopyToAsync(output, _lifetime.Token);
            SkinActionStatusText.Text = "皮肤文件已保存。";
        }
        catch (Exception exception) { if (!_closed) SkinActionStatusText.Text = "保存皮肤失败：" + exception.Message; }
    }
    private void OnRedeemCdkey(object? sender, RoutedEventArgs e) => OpenWeb("https://www.minecraft.net/zh-hans/redeem");
    private void OnCloset(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_model?.AuthlibSkinClosetUrl)) OpenWeb(_model.AuthlibSkinClosetUrl);
    }
    private void OpenWeb(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception exception) { SkinActionStatusText.Text = "打开网页失败：" + exception.Message; }
    }
    private void CleanTemporaryFiles()
    {
        foreach (var path in _temporaryFiles)
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        _temporaryFiles.Clear();
    }
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
