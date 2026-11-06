using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using WMCL.App.Services;
using WMCL.App.Services.Infrastructure;

namespace WMCL.App.Views;

/// <summary>
/// 离线账号皮肤管理：复用与微软皮肤窗口一致的 3D 预览、模型识别与像素编辑器，
/// 但只读写本地文件（OfflineSkinStore）。
/// </summary>
public partial class OfflineSkinWindow : Window
{
    private readonly string _playerName;
    private Bitmap? _bitmap;
    private string _variant = "auto";

    public OfflineSkinWindow() : this("player") { }

    public OfflineSkinWindow(string playerName)
    {
        _playerName = string.IsNullOrWhiteSpace(playerName) ? "player" : playerName.Trim();
        InitializeComponent();
        PlayerNameText.Text = _playerName;
        ModelBox.SelectedIndex = 0;
        ModelBox.SelectionChanged += (_, _) =>
        {
            _variant = (ModelBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "auto";
            UpdatePreview();
        };
        Opened += (_, _) => Refresh();
        Closed += (_, _) =>
        {
            CharacterPreview.Skin = null;
            _bitmap?.Dispose();
            _bitmap = null;
        };
    }

    private void Refresh()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        var path = OfflineSkinStore.PathFor(_playerName);
        if (File.Exists(path))
        {
            try { _bitmap = new Bitmap(path); }
            catch (Exception ex) { StatusText.Text = "读取皮肤失败：" + ex.Message; }
        }
        EditButton.IsEnabled = _bitmap is not null;
        UpdatePreview();
        StatusText.Text = _bitmap is null
            ? "还没有设置离线皮肤，点「修改皮肤」导入 PNG。"
            : "当前皮肤：" + path;
    }

    private void UpdatePreview()
    {
        CharacterPreview.Skin = _bitmap;
        if (_bitmap is null)
        {
            CharacterPreview.Variant = _variant == "slim" ? "slim" : "classic";
            SkinModelHintText.Text = "还没有皮肤贴图。";
            return;
        }
        var detected = SkinModelDetector.Detect(_bitmap);
        var resolved = SkinModelDetector.Resolve(_bitmap, _variant);
        CharacterPreview.Variant = resolved;
        SkinModelHintText.Text = _variant == "auto"
            ? $"自动识别：{resolved}（{(resolved == "slim" ? "细臂" : "粗臂")}）"
            : $"手动预览：{resolved}；贴图识别：{detected}";
    }

    private async void OnPickSkin(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择皮肤文件",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG 皮肤") { Patterns = ["*.png"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            OfflineSkinStore.Save(_playerName, buffer.ToArray());
            Refresh();
        }
        catch (Exception ex)
        {
            StatusText.Text = "导入失败：" + ex.Message;
        }
    }

    private async void OnEditSkin(object? sender, RoutedEventArgs e)
    {
        if (_bitmap is null) return;
        var editor = new SkinEditorWindow(new Bitmap(OfflineSkinStore.PathFor(_playerName)), _variant);
        if (await editor.ShowDialog<bool>(this))
        {
            using var bitmap = editor.Editor.ToBitmap()!;
            using var buffer = new MemoryStream();
            bitmap.Save(buffer);
            OfflineSkinStore.Save(_playerName, buffer.ToArray());
            Refresh();
        }
    }

    private void OnSaveSkin(object? sender, RoutedEventArgs e)
    {
        if (_bitmap is null) return;
        var path = OfflineSkinStore.PathFor(_playerName);
        using var buffer = new MemoryStream();
        _bitmap!.Save(buffer);
        OfflineSkinStore.Save(_playerName, buffer.ToArray());
        StatusText.Text = "已保存到：" + path;
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => Refresh();

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        OfflineSkinStore.Delete(_playerName);
        Refresh();
    }

    private void OnFront(object? sender, RoutedEventArgs e) { CharacterPreview.ResetView(); CharacterPreview.Yaw = 0; }
    private void OnBack(object? sender, RoutedEventArgs e) { CharacterPreview.ResetView(); CharacterPreview.Yaw = 180; }
    private void OnResetView(object? sender, RoutedEventArgs e) => CharacterPreview.ResetView();
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
