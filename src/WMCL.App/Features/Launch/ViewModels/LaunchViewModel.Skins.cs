using WMCL.App.ViewModels.Accounts;
using WMCL.App.ViewModels.Launching;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.App.Services.Infrastructure;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Core.Logging;
using WMCL.Launch;
using WMCL.Launch.Auth;
using WMCL.Launch.Crash;
using WMCL.Launch.Diagnostics;
using WMCL.Minecraft;
using WMCL.Modding.Modpack;

namespace WMCL.App.Features.Launch;

public partial class LaunchViewModel
{
    public bool CanUploadMicrosoftSkin => IsMicrosoftAccount && _microsoftAccount is not null && !IsUploadingSkin;

    public async Task<string> UploadMicrosoftSkinAsync(string pngPath)
    {
        if (IsUploadingSkin) return "正在上传皮肤，请等待当前操作完成。";
        if (!IsMicrosoftAccount || _microsoftAccount is not { } account)
            return SetUploadStatus("请先登录微软账号");
        var generation = _skinAccountGeneration;
        var variant = SelectedSkinVariant;
        try
        {
            if (!File.Exists(pngPath)) throw new FileNotFoundException("皮肤文件不存在。", pngPath);
            SkinImageValidator.ValidateMinecraftSkin(pngPath);
            if (string.IsNullOrWhiteSpace(account.AccessToken) || account.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("登录已过期，请重新登录微软账号后上传皮肤。");
            if (variant == "auto")
            {
                using var image = new Avalonia.Media.Imaging.Bitmap(pngPath);
                variant = SkinModelDetector.Detect(image);
            }
            else if (variant is not ("classic" or "slim")) throw new InvalidDataException("皮肤模型必须为自动识别、classic 或 slim。");
        }
        catch (Exception ex)
        {
            return SetUploadStatus("上传皮肤失败：" + ex.Message);
        }

        IsUploadingSkin = true;
        SetUploadStatus("正在上传皮肤：" + Path.GetFileName(pngPath));
        try
        {
            await _uploadMicrosoftSkin(account, pngPath, variant);
            if (!IsCurrentSkinAccount(account, generation)) return "账号已切换，原账号皮肤上传结果未应用到当前预览。";
            var loaded = await LoadMicrosoftSkinAsync(account);
            if (!IsCurrentSkinAccount(account, generation)) return "账号已切换，原账号皮肤上传结果未应用到当前预览。";
            return SetUploadStatus(loaded && HasAccountSkin ? "皮肤已上传并刷新" : "皮肤已上传，但预览尚未更新，请点击刷新皮肤重试。");
        }
        catch (Exception ex)
        {
            if (!IsCurrentSkinAccount(account, generation)) return "账号已切换，原账号皮肤上传失败未影响当前账号。";
            return SetUploadStatus("上传皮肤失败：" + ex.Message);
        }
        finally
        {
            IsUploadingSkin = false;
        }
    }

    private string SetUploadStatus(string message)
    {
        SkinUploadStatus = message;
        AccountStatus = message;
        return message;
    }

    private bool IsCurrentSkinAccount(MicrosoftAccount account, int generation) =>
        IsMicrosoftAccount && generation == _skinAccountGeneration && ReferenceEquals(_microsoftAccount, account);

    /// <summary>皮肤像素编辑器实例（懒加载）。</summary>
    public SkinEditorViewModel Editor { get; } = new();

    [ObservableProperty]
    private string _editorColorHex = "#555555FF";

    partial void OnEditorColorHexChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!value.StartsWith("#") && value.Length == 6)
            value = "#" + value;
        if (Color.TryParse(value, out var color))
            Editor.SelectedColor = color;
    }

    /// <summary>保存像素编辑器修改的皮肤。写入临时 PNG 后上传。</summary>
    public async Task<string> SaveEditedSkinAsync(SkinEditorViewModel editor)
    {
        if (!IsMicrosoftAccount || _microsoftAccount is not { } account) return "请先登录微软账号";
        var generation = _skinAccountGeneration;
        using var bitmap = editor.ToBitmap();
        if (bitmap is null) return "没有可保存的皮肤数据";
        var tempPath = Path.Combine(Path.GetTempPath(),
            "wmcl-skin-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create))
                bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            if (!IsCurrentSkinAccount(account, generation)) return "账号已切换，未上传原账号的编辑皮肤。";
            return await UploadMicrosoftSkinAsync(tempPath);
        }
        catch (Exception ex)
        {
            return "保存皮肤失败：" + ex.Message;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* 清理失败 */ }
        }
    }
    public AuthlibAuthService AuthlibService => _authlib;
    public event EventHandler<string>? VersionSettingsRequested;
    public event EventHandler? DownloadRequested;
    public bool IsOfflineAccount => !IsMicrosoftAccount && !IsAuthlibAccount;
    public bool CanOpenVersionSettings => !IsBusy && SelectedVersion is { IsInstalled: true };

    public ObservableCollection<VersionItem> Versions { get; } = [];
    /// <summary>启动页“版本选择”只展示已安装版本。</summary>
    public ObservableCollection<VersionItem> InstalledVersions { get; } = [];
    public bool HasInstalledVersions => InstalledVersions.Count > 0;
    public ObservableCollection<AuthlibAccount> AuthlibAccounts { get; } = [];

    /// <summary>保存过的离线角色名。</summary>
    public ObservableCollection<string> OfflineRoles { get; } = [];

    /// <summary>浮层里切换离线角色。</summary>
    public string? SelectedOfflineRole
    {
        get => PlayerName;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || !IsOfflineAccount) return;
            if (string.Equals(PlayerName, value, StringComparison.Ordinal)) return;
            _renamingFrom = PlayerName;
            PlayerName = value;
            _services.Settings.PlayerName = value;
            _services.SaveSettings();
            RefreshOfflineSkin();
        }
    }

    /// <summary>改名输入框绑定：把角色列表里的旧名字替换成新名字。</summary>
    public string OfflineRoleName
    {
        get => PlayerName;
        set
        {
            if (!IsOfflineAccount || string.IsNullOrWhiteSpace(value)) return;
            var old = PlayerName;
            if (string.Equals(old, value, StringComparison.Ordinal)) return;
            var roles = _services.Settings.OfflineRoles;
            roles.Remove(old);
            if (!roles.Contains(value)) roles.Add(value);
            PlayerName = value;
            _services.Settings.PlayerName = value;
            _services.SaveSettings();
            RefreshOfflineRoles();
            RefreshOfflineSkin();
        }
    }
}
