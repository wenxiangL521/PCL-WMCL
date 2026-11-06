using WMCL.Link;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.App.Services.Platform;
using WMCL.App.Services.Infrastructure;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Download;
using WMCL.Launch.Java;
using WMCL.Minecraft;

namespace WMCL.App.Features.Settings;

public partial class SettingsViewModel
{
    [RelayCommand]
    private async Task ExportConfigAsync()
    {
        try
        {
            var storage = GetStorageProvider();
            if (storage is null)
            {
                ConfigStatus = "无法访问文件选择器";
                return;
            }
            var suggested = $"WMCL-Config-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "导出配置",
                SuggestedFileName = suggested,
                FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = ["*.json"] }]
            });
            if (file is null) return;
            var outputPath = file.Path.LocalPath;
            SettingsService.ExportToFile(_services.Settings, outputPath);
            ConfigStatus = $"已导出配置到：{outputPath}";
        }
        catch (Exception ex)
        {
            ConfigStatus = "导出配置失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ImportConfigAsync()
    {
        try
        {
            var storage = GetStorageProvider();
            if (storage is null)
            {
                ConfigStatus = "无法访问文件选择器";
                return;
            }
            var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择配置文件",
                AllowMultiple = false,
                FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = ["*.json"] }]
            });
            if (files.Count == 0) return;
            var imported = SettingsService.ImportFromFile(files[0].Path.LocalPath);
            // 重新加载到当前 VM（Settings 是 get-only，LoadFromSettings 用反射复制）
            LoadFromSettings(imported);
            ConfigStatus = "配置已导入并已应用（游戏目录保持不变；启动命令、Java/JVM 参数、自定义主页、外部程序及更新源等执行类设置不导入，请在本机手动配置）";
        }
        catch (Exception ex)
        {
            ConfigStatus = "导入配置失败：" + ex.Message;
        }
    }

    /// <summary>获取当前窗口的 StorageProvider（无文件选择器环境返回 null）。</summary>
    private static Avalonia.Platform.Storage.IStorageProvider? GetStorageProvider()
    {
        var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
        { MainWindow: { } win }
            ? win
            : null;
        return Avalonia.Controls.TopLevel.GetTopLevel(mainWindow)?.StorageProvider;
    }

    private void LoadFromSettings(AppSettings settings)
    {
        AppSettingsImportPolicy.Validate(settings);
        var wasSuppressed = _suppressAutoSave;
        _suppressAutoSave = true;
        _autoSaveTimer.Stop();
        try
        {
            // Settings 是 get-only，用反射把所有字段复制到现有对象
            // 跳过游戏目录相关属性，避免导入配置时意外改变游戏目录
            foreach (var prop in typeof(AppSettings).GetProperties())
            {
                if (AppSettingsImportPolicy.IsProtectedProperty(prop.Name)) continue;
                if (prop.Name is "GameDirectory" or "GameFolders" or "GameFolderNames"
                    or "FavoriteVersions" or "HiddenVersions"
                    or "FavoriteDownloadComponents" or "ResourceFavoriteFolders"
                    or "FavoriteVersionsByFolder" or "HiddenVersionsByFolder")
                    continue;
                // 导入的敏感字段为空时（导出已脱敏）保留本地已配置的值。
                if (prop.Name is "ProxyPassword" && string.IsNullOrWhiteSpace(prop.GetValue(settings) as string))
                    continue;
                if (prop.CanRead && prop.CanWrite)
                    prop.SetValue(_services.Settings, prop.GetValue(settings));
            }
            // 重新绑定 VM 属性
            GameDirectory = _services.Settings.GameDirectory;
            MemoryMb = _services.Settings.MemoryMb;
            WindowWidth = _services.Settings.Width;
            WindowHeight = _services.Settings.Height;
            GlobalJvmArgs = _services.Settings.ExtraJvmArgs;
            GlobalGameArgs = _services.Settings.ExtraGameArgs;
            PreLaunchCommand = _services.Settings.PreLaunchCommand;
            PreLaunchWait = _services.Settings.PreLaunchWait;
            PlayerName = _services.Settings.PlayerName;
            AfterLaunch = ToAfterLaunchLabel(_services.Settings.LauncherAfterLaunch);
            Priority = ToPriorityLabel(_services.Settings.ProcessPriority);
            Isolation = ToIsolationLabel(_services.Settings.IsolationMode);
            HighPerformanceGpu = _services.Settings.HighPerformanceGpu;
            GpuIndex = _services.Settings.GpuIndex ?? "";
            ProxyUrl = _services.Settings.ProxyUrl ?? "";
            ProxyMode = string.IsNullOrWhiteSpace(_services.Settings.ProxyMode) ? "none" : _services.Settings.ProxyMode;
            ProxyUsername = _services.Settings.ProxyUsername ?? "";
            ProxyPassword = _services.Settings.ProxyPassword ?? "";
            ProxyBypassList = string.IsNullOrWhiteSpace(_services.Settings.ProxyBypassList)
                ? "localhost,127.0.0.1,::1"
                : _services.Settings.ProxyBypassList;
            ProxyStatus = ProxyManager.Describe();
            EnableTrash = _services.Settings.EnableTrash;
            TrashAutoCleanDays = _services.Settings.TrashAutoCleanDays;
            DownloadSpeedLimitMbps = (decimal)_services.Settings.DownloadSpeedLimitMbps;
            DownloadThreads = (decimal)_services.Settings.DownloadThreads;
            CustomLogoPath = _services.Settings.CustomLogoPath ?? "";
            CustomLogoText = _services.Settings.CustomLogoText ?? "";
            CustomHomePagePath = _services.Settings.CustomHomePagePath ?? "";
            MusicFolder = _services.Settings.MusicFolder ?? "";
            MusicVolume = (decimal)_services.Settings.MusicVolume;
            MusicStart = _services.Settings.MusicStart;
            MusicPauseOnGame = _services.Settings.MusicPauseOnGame;
            BackgroundImagePath = _services.Settings.BackgroundImagePath ?? "";
            BackgroundImageFolder = _services.Settings.BackgroundImageFolder ?? "";
            BackgroundColorOverlay = _services.Settings.BackgroundColorOverlay;
            BackgroundImageOpacity = (decimal)_services.Settings.BackgroundImageOpacity;
            BackgroundImageBlur = (decimal)_services.Settings.BackgroundImageBlur;
            JavaPath = _services.Settings.JavaPath ?? "";
            Mirror = _services.Settings.Mirror;
            UpdateChannel = ToChannelLabel(_services.Settings.UpdateChannel);
            UpdateMode = ToModeLabel(_services.Settings.UpdateMode);
            Theme = ToThemeLabel(_services.Settings.Theme);
            UiFont = string.IsNullOrWhiteSpace(_services.Settings.UiFontFamily)
                ? UiFontService.DefaultLabel
                : _services.Settings.UiFontFamily;
            AccentHue = _services.Settings.AccentHue;
            AccentSaturation = _services.Settings.AccentSaturation;
            AccentLightAdjust = _services.Settings.AccentLightAdjust;
            FontScale = _services.Settings.FontScale;
            ApplicationOpacity = Math.Clamp(_services.Settings.ApplicationOpacity, 0, 100);
            WindowBackdropBlur = _services.Settings.WindowBackdropBlur;
            ApplyShortcuts(_services.Settings.CustomShortcuts);
            ConfigStatus = "配置已导入";
            Status = ConfigStatus;
        }
        finally
        {
            _suppressAutoSave = wasSuppressed;
        }
        RunSave(applyGameDirectory: false);
    }

    private void ApplyShortcuts(string? json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            var dto = System.Text.Json.JsonSerializer.Deserialize<ShortcutDto>(json);
            if (dto is null) return;
            ShortcutTab1 = dto.Tab1;
            ShortcutTab2 = dto.Tab2;
            ShortcutTab3 = dto.Tab3;
            ShortcutTab4 = dto.Tab4;
            ShortcutTab5 = dto.Tab5;
            ShortcutSearch = dto.Search;
        }
        catch { /* 保留默认快捷键 */ }
    }

    private sealed record ShortcutDto(
        string Tab1, string Tab2, string Tab3, string Tab4, string Tab5, string Search);

    public void Dispose()
    {
        _autoSaveTimer.Stop();
        _javaInstallCts?.Cancel();
        _javaInstallCts?.Dispose();
        _javaInstallCts = null;
    }
}


public sealed record ThemePreset(string Name, int Hue, int Saturation, int LightAdjust);

/// <summary>“功能隐藏”分组中的一项，勾选即写入设置。</summary>
public partial class HiddenFeatureItem(FeatureDefinition definition, FeatureVisibility features) : ObservableObject
{
    public string Key => definition.Key;
    public string Label => definition.Label;

    public bool IsHidden
    {
        get => features.IsHidden(Key);
        set
        {
            if (value == IsHidden) return;
            features.SetHidden(Key, value);
            OnPropertyChanged();
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(IsHidden));
}

public sealed record HiddenFeatureGroup(string Title, IReadOnlyList<HiddenFeatureItem> Items);
