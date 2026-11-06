using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.IO;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Fabric;
using WMCL.Modding.Local;
using WMCL.Modding.OptiFine;
using WMCL.Modding.Forgelike;
using WMCL.Modding.Modrinth;
using WMCL.Modding.LiteLoader;

namespace WMCL.App.Features.Mods;

public partial class ModsViewModel
{
    public async Task PrepareGameVersionAsync(string gameVersion, bool forceReload = false)
    {
        if (forceReload)
            _loaderLoadKey = _forgelikeLoadKey = _optiFineLoadKey = null;
        _preparingCatalogGame = true;
        try { SelectedVanillaInstance = null; }
        finally { _preparingCatalogGame = false; }
        _componentGameRoot = _services.Settings.GameDirectory;
        ComponentTargetInfo = "Minecraft " + gameVersion;
        SelectedGameVersion = gameVersion;
        await Task.WhenAll(LoadLoadersAsync(), LoadForgelikeVersionsAsync(), LoadOptiFineVersionsAsync());
    }

    private Task LoadForgelikeVersionsAsync()
    {
        var key = _componentGameRoot + ":" + SelectedForgelikeKind + ":" + SelectedGameVersion;
        if (key == _forgelikeLoadKey && _forgelikeLoadTask is not null) return _forgelikeLoadTask;
        _forgelikeLoadKey = key;
        return _forgelikeLoadTask = LoadForgelikeVersionsCoreAsync();
    }

    private async Task LoadForgelikeVersionsCoreAsync()
    {
        var game = SelectedGameVersion;
        var root = _componentGameRoot;
        var request = ++_forgelikeRequest;
        _forgelikeCatalogError = null;
        ForgelikeVersions.Clear();
        SelectedForgelikeVersion = null;
        IsForgelikeBusy = _forgelikeCts is not null;
        if (string.IsNullOrWhiteSpace(game)) return;

        var kind = SelectedForgelikeKind == "NeoForge"
            ? ForgelikeKind.NeoForge
            : ForgelikeKind.Forge;
        IsForgelikeBusy = true;
        if (_forgelikeCts is null) ForgelikeStatus = $"正在获取 {SelectedForgelikeKind} {game} 版本…";
        try
        {
            var versions = await _getForgelikeVersions(kind, game);
            if (request != _forgelikeRequest ||
                game != SelectedGameVersion || !IsCurrentComponentRoot(root) ||
                kind != (SelectedForgelikeKind == "NeoForge"
                    ? ForgelikeKind.NeoForge
                    : ForgelikeKind.Forge))
                return;
            ForgelikeVersions.Clear();
            foreach (var version in versions.Take(60))
                ForgelikeVersions.Add(version);
            SelectedForgelikeVersion = versions.Contains(SelectedForgelikeVersion) ? SelectedForgelikeVersion : ForgelikeVersions.FirstOrDefault();
            if (SelectedForgelikeVersion is not null && !ForgelikeVersions.Contains(SelectedForgelikeVersion)) ForgelikeVersions.Add(SelectedForgelikeVersion);
            if (_forgelikeCts is null)
                ForgelikeStatus = ForgelikeVersions.Count == 0
                    ? $"没有找到 {SelectedForgelikeKind} {game} 的版本"
                    : $"共 {versions.Count} 个版本";
        }
        catch (Exception ex)
        {
            if (request == _forgelikeRequest && game == SelectedGameVersion && IsCurrentComponentRoot(root) &&
                kind == (SelectedForgelikeKind == "NeoForge" ? ForgelikeKind.NeoForge : ForgelikeKind.Forge))
            {
                _forgelikeLoadKey = null;
                _forgelikeCatalogError = kind == ForgelikeKind.Forge && ex.Message == "无" ? null : SelectedForgelikeKind + "：" + ex.Message;
                if (_forgelikeCts is null) ForgelikeStatus = "获取版本失败：" + ex.Message;
            }
        }
        finally
        {
            if (request == _forgelikeRequest)
            {
                IsForgelikeBusy = _forgelikeCts is not null;
                UpdateForgelikeCommands();
            }
        }
    }

    [RelayCommand]
    private async Task InstallForgelikeAsync()
    {
        if (!CanInstallForgelike ||
            SelectedGameVersion is null ||
            SelectedForgelikeVersion is null) return;

        var kind = SelectedForgelikeKind == "NeoForge"
            ? ForgelikeKind.NeoForge
            : ForgelikeKind.Forge;
        var game = SelectedGameVersion;
        var version = SelectedForgelikeVersion;
        var root = _componentGameRoot;

        IsForgelikeBusy = true;
        CanInstallForgelike = false;
        ForgelikeProgress = 0;
        ForgelikeProgressText = "";
        ForgelikeStatus = $"正在安装 {SelectedForgelikeKind} {version}…";
        _forgelikeCts?.Dispose();
        _forgelikeCts = new CancellationTokenSource();
        IsInstallingForgelike = true;

        try
        {
            EnsureCurrentComponentRoot(root);
            using var operation = _services.GameOperations.Acquire(root, "安装 " + kind);
            var progress = new Progress<JobProgress>(update =>
            {
                ForgelikeProgress = (update.Percent ?? 0) * 100;
                ForgelikeProgressText = FormatProgress(update);
            });
            var log = new Progress<string>(line =>
            {
                if (line.Contains("Downloading", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Patching", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Error", StringComparison.OrdinalIgnoreCase))
                    ForgelikeStatus = line;
            });

            var id = await _installComponent(new(root, game, kind.ToString(), version), progress, log, _forgelikeCts.Token);

            ForgelikeStatus = $"{kind} 安装完成：{id}";
            ForgelikeProgress = 100;
            await SelectInstalledComponentAsync(root, id);
            VersionInstalled?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            ForgelikeStatus = "已取消安装";
        }
        catch (Exception ex)
        {
            LastTaskFailure = TaskFailureInfo.From(ex, $"安装 {SelectedForgelikeKind}");
            _lastRetryPayload = TaskRetryPayload.Create(TaskRetryKind.ModInstall, root ?? "", game,
                options: new Dictionary<string, string> { ["kind"] = SelectedForgelikeKind, ["loader"] = version });
            ForgelikeStatus = "安装失败：" + ex.Message;
        }
        finally
        {
            IsInstallingForgelike = false;
            IsForgelikeBusy = false;
            ForgelikeProgress = 0;
            ForgelikeProgressText = "";
            _forgelikeCts.Dispose();
            _forgelikeCts = null;
            UpdateForgelikeCommands();
        }
    }

    [RelayCommand]
    private void CancelForgelike()
    {
        var cancellation = _forgelikeCts;
        if (cancellation is null) return;
        ForgelikeStatus = "正在取消…";
        cancellation.Cancel();
    }

    private void UpdateModCommands()
    {
        CanInstallMod = !IsModBusy &&
                        !IsResolvingModDependencies && _modInstallPlan is not null &&
                        SelectedMod is not null &&
                        _modLoader is "fabric" or "quilt" or "forge" or "neoforge" &&
                        !string.IsNullOrWhiteSpace(ModsDirectory);
    }

    private async Task UpdateModTargetAsync()
    {
        var request = ++_modTargetRequest;
        InvalidateModUpdates();
        var id = SelectedModInstance;
        var gameRoot = _services.Settings.GameDirectory;
        _modGameVersion = null;
        _modLoader = "vanilla";
        ModsDirectory = "";
        SelectedLocalMod = null;
        LocalMods.Clear();
        OnPropertyChanged(nameof(IsModDisabled));
        UpdateModCommands();
        if (string.IsNullOrWhiteSpace(id))
        {
            ModTargetInfo = "未选择实例";
            return;
        }

        try
        {
            var version = await new VersionRepository(gameRoot).GetVersionAsync(id);
            if (request != _modTargetRequest || _services.Settings.GameDirectory != gameRoot) return;
            var gameDirectory = IsolationPolicy.GetGameDirectory(
                gameRoot, id, version, _services.Settings.IsolationMode);
            ModsDirectory = Path.Combine(gameDirectory, "mods");
            _modTargetRoot = gameRoot;
            if (version.GameVersionIdentity is { IsReliable: false })
            {
                ModTargetInfo = "无法精准识别 Minecraft 基座，请先在实例修改页确认版本信息";
                return;
            }
            _modGameVersion = MinecraftVersionUtil.GetBaseGameVersion(id, version);
            _modLoader = MinecraftVersionUtil.GetLoader(version);
            ModTargetInfo = $"{id} · Minecraft {_modGameVersion} · {_modLoader}";
        }
        catch (Exception ex)
        {
            if (request == _modTargetRequest && _services.Settings.GameDirectory == gameRoot)
                ModTargetInfo = "读取实例失败：" + ex.Message;
        }
        finally
        {
            if (request == _modTargetRequest && _services.Settings.GameDirectory == gameRoot)
            {
                OnPropertyChanged(nameof(IsModDisabled));
                UpdateModCommands();
                await RefreshLocalModsAsync();
                _modVersionLoad = LoadModVersionsAsync(SelectedMod);
            }
        }
    }

    private Task LoadLoadersAsync()
    {
        var key = _componentGameRoot + ":" + SelectedFabricKind + ":" + SelectedGameVersion;
        if (key == _loaderLoadKey && _loaderLoadTask is not null) return _loaderLoadTask;
        _loaderLoadKey = key;
        return _loaderLoadTask = LoadLoadersCoreAsync();
    }

    private async Task LoadLoadersCoreAsync()
    {
        var game = SelectedGameVersion;
        var root = _componentGameRoot;
        var request = ++_loaderRequest;
        _loaderCatalogError = null;
        Loaders.Clear();
        _loaderInfos.Clear();
        SelectedLoader = null;
        IsBusy = _cts is not null;
        if (string.IsNullOrWhiteSpace(game)) return;

        var kind = SelectedFabricKind;
        IsBusy = true;
        if (_cts is null) Status = $"正在获取 {kind} {game} 的 Loader 列表…";
        try
        {
            var loaders = await _getLoaders(kind, game);
            if (request != _loaderRequest || game != SelectedGameVersion || !IsCurrentComponentRoot(root) ||
                kind != SelectedFabricKind) return;
            _loaderInfos.Clear();
            _loaderInfos.AddRange(loaders);

            Loaders.Clear();
            foreach (var loader in loaders.Take(30))
                Loaders.Add(loader.Version);

            SelectedLoader = loaders.Any(loader => loader.Version == SelectedLoader) ? SelectedLoader
                : (loaders.FirstOrDefault(loader => loader.Stable) ?? loaders.FirstOrDefault())?.Version;
            if (SelectedLoader is not null && !Loaders.Contains(SelectedLoader)) Loaders.Add(SelectedLoader);
            if (_cts is null)
                Status = loaders.Count == 0
                    ? $"{kind} 没有支持 Minecraft {game} 的版本"
                    : $"共 {loaders.Count} 个 {kind} Loader，默认选择稳定版";
        }
        catch (Exception ex)
        {
            if (request == _loaderRequest && game == SelectedGameVersion && kind == SelectedFabricKind && IsCurrentComponentRoot(root))
            {
                _loaderLoadKey = null;
                _loaderCatalogError = SelectedFabricKind + "：" + ex.Message;
                if (_cts is null) Status = $"获取 {kind} 版本失败：" + ex.Message;
            }
        }
        finally
        {
            if (request == _loaderRequest)
            {
                IsBusy = _cts is not null;
                UpdateCommands();
            }
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private async Task InstallFabricAsync()
    {
        if (!CanInstall || SelectedGameVersion is null || SelectedLoader is null) return;

        var game = SelectedGameVersion;
        var loader = SelectedLoader;
        var kind = SelectedFabricKind;
        var root = _componentGameRoot;
        IsBusy = true;
        CanCancel = true;
        Progress = 0;
        ProgressText = "";
        Status = $"正在安装 {kind} {loader}（{game}）…";
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            EnsureCurrentComponentRoot(root);
            using var operation = _services.GameOperations.Acquire(root, "安装 " + kind);
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                ProgressText = FormatProgress(update);
            });
            var id = await _installComponent(new(root, game, kind, loader), progress, null, _cts.Token);
            Status = $"{kind} 安装完成：{id}";
            Progress = 100;
            await SelectInstalledComponentAsync(root, id);
            VersionInstalled?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            Status = $"已取消 {kind} 安装";
        }
        catch (Exception ex)
        {
            LastTaskFailure = TaskFailureInfo.From(ex, $"安装 {kind}");
            _lastRetryPayload = TaskRetryPayload.Create(TaskRetryKind.ModInstall, root ?? "", game,
                options: new Dictionary<string, string> { ["kind"] = kind, ["loader"] = loader });
            Status = $"{kind} 安装失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            CanCancel = false;
            Progress = 0;
            ProgressText = "";
            _cts.Dispose();
            _cts = null;
            UpdateCommands();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        var cancellation = _cts;
        if (cancellation is null) return;
        Status = "正在取消…";
        cancellation.Cancel();
    }

    private FabricInstaller FabricInstallerFor(string kind) => kind switch
    {
        "Quilt" => _services.Quilt,
        "LegacyFabric" => _services.LegacyFabric,
        _ => _fabric
    };
}
