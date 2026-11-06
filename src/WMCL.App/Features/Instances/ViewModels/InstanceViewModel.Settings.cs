using WMCL.Core;
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.Modpack;
using WMCL.Download;
using WMCL.Launch;
using WMCL.Launch.Java;
using Avalonia.Threading;

namespace WMCL.App.Features.Instances;

public partial class InstanceViewModel
{
    [RelayCommand]
    private void SaveInstanceSettings()
    {
        if (SelectedInstance is null) return;
        try
        {
            using var operation = _services.GameOperations.Acquire(GameDirectory, "保存实例设置");
            SaveInstanceSettingsCore();
        }
        catch (Exception ex) { Status = "保存实例设置失败：" + ex.Message; }
    }

    private void SaveInstanceSettingsCore()
    {
        if (SelectedInstance is null) return;
        var saveIdentity = WMCL.App.ViewModels.Instances.InstanceIdentity.From(
            InstanceDescription, InstanceCategory, InstanceFavorite, InstanceLogoPath);
        var saveServer = WMCL.App.ViewModels.Instances.InstanceServerSettings.From(
            InstanceServer, (int)InstanceServerPort, InstanceAuthServer, InstanceAuthServerName,
            InstancePluginDirectory, InstancePreLaunchCommand, InstancePreLaunchWait);
        var windowState = WMCL.App.ViewModels.Instances.InstanceWindowState.From(
            UseGlobalWindow ? null : (int)InstanceWidth,
            UseGlobalWindow ? null : (int)InstanceHeight,
            _services.Settings.Width, _services.Settings.Height);
        InstanceSettingsStore.Save(
            Path.Combine(GameDirectory, "versions", SelectedInstance.Id),
            new InstanceSettings
            {
                MemoryMb = UseGlobalMemory ? null : (int)InstanceMemoryMb,
                JavaPath = UseGlobalJava || string.IsNullOrWhiteSpace(InstanceJavaPath)
                    ? null
                    : InstanceJavaPath.Trim(),
                ExtraJvmArgs = InstanceJvmArgs.Trim(),
                ExtraGameArgs = InstanceGameArgs.Trim(),
                InitialHeapMb = int.TryParse(InstanceInitialHeap, out var ih) && ih > 0 ? ih : 0,
                GcStrategy = InstanceGcStrategy.Trim(),
                Width = windowState.ToSettings().Width,
                Height = windowState.ToSettings().Height,
                Description = saveIdentity.TrimmedDescription,
                Category = saveIdentity.StoredCategory,
                VersionCardType = WMCL.App.ViewModels.Instances.VersionCardType.IndexOf(InstanceVersionCardType),
                // 窗口设置统一从状态对象写回
                LogoPath = InstanceLogoPath.Trim(),
                IsFavorite = InstanceFavorite,
                IsolationMode = WMCL.App.ViewModels.Instances.IsolationLabel.ToMode(InstanceIsolation),
                Server = saveServer.TrimmedServer,
                Port = saveServer.EffectivePort,
                AuthServer = saveServer.TrimmedAuthServer,
                AuthServerName = saveServer.TrimmedAuthServerName,
                PluginDirectory = saveServer.TrimmedPluginDirectory,
                PreLaunchCommand = saveServer.TrimmedPreLaunchCommand,
                PreLaunchWait = InstancePreLaunchWait
            });
        SelectedInstance.Description = InstanceDescription.Trim();
        SelectedInstance.Category = InstanceCategory == "未分类" ? "" : InstanceCategory;
        EnsureCategoryOptions(SelectedInstance.Category);
        SelectedInstance.IsFavorite = InstanceFavorite;
        if (File.Exists(InstanceLogoPath))
        {
            try { SelectedInstance.Logo = new Avalonia.Media.Imaging.Bitmap(InstanceLogoPath); }
            catch { SelectedInstance.Logo = null; }
        }
        else
        {
            SelectedInstance.Logo = null;
        }
        SortInstances();
        Status = $"已保存 {SelectedInstance.Id} 的实例设置";
        InstanceSettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ResetInstanceSettings()
    {
        if (SelectedInstance is null) return;
        try
        {
            using var operation = _services.GameOperations.Acquire(GameDirectory, "重置实例设置");
            InstanceSettingsStore.Reset(Path.Combine(GameDirectory, "versions", SelectedInstance.Id));
            LoadInstanceSettings(SelectedInstance);
            Status = $"已重置 {SelectedInstance.Id} 的实例设置";
        }
        catch (Exception ex) { Status = "重置实例设置失败：" + ex.Message; }
    }
}
