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
    public async Task LoadAsync(string? preferredVersion = null)
    {
        QueueMicrosoftValidation();
        var root = _services.Settings.GameDirectory;
        await _versionLoadGate.WaitAsync();
        var ownsBusy = false;
        IsVersionLoading = true;
        try
        {
            if (IsBusy || _services.Settings.GameDirectory != root) return;
            IsBusy = true;
            ownsBusy = true;
            Status = "正在获取版本列表…";
            var items = await _loadVersions(root);
            if (_services.Settings.GameDirectory != root) return;
            var favorites = _services.GetFavoriteVersions(root)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hidden = _services.GetHiddenVersions(root)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                item.IsFavorite = favorites.Contains(item.Id);
                item.IsHidden = hidden.Contains(item.Id) || item.CardTypeOverride == VersionCardPlacement.Hidden;
            }
            _all.Clear();
            _all.AddRange(items);
            ApplyFilter(preferredVersion ?? SelectedVersion?.Id);
            Status = items.Count == 0
                ? "没有可用版本"
                : InstalledVersions.Count == 0
                    ? "尚未安装游戏，请先到下载页安装"
                    : $"共 {InstalledVersions.Count} 个已安装版本";
        }
        catch (Exception ex)
        {
            if (_services.Settings.GameDirectory == root) Status = "加载版本列表失败：" + ex.Message;
        }
        finally
        {
            if (ownsBusy)
            {
                IsBusy = false;
                UpdateCommands();
            }
            IsVersionLoading = false;
            _versionLoadGate.Release();
        }
    }

    /// <summary>从版本选择浮层跳转到下载页。</summary>


    partial void OnSelectedVersionChanged(VersionItem? value)
    {
        if (!string.IsNullOrWhiteSpace(value?.Id)) SaveSelectedVersion(value.Id);
        ModpackLaunchError = "";
        GameFileCheckError = "";
        OnPropertyChanged(nameof(IsSelectedVersionUsable));
        OnPropertyChanged(nameof(SelectedVersionTitle));
        OnPropertyChanged(nameof(LaunchButtonSubtitle));
        OnPropertyChanged(nameof(CanOpenVersionSettings));
        OnPropertyChanged(nameof(AuthlibSkinClosetUrl));
        OnPropertyChanged(nameof(CanOpenAuthlibSkinCloset));
        VersionInfo = value is null
            ? "选择一个版本"
            : $"{value.Id} · {value.TypeText}{(value.IsLatest ? " · 最新" : "")} · {FormatDate(value.ReleaseTime)}";
        UpdateCommands();
    }

    partial void OnIsBusyChanged(bool value)
    {
        UpdateCommands();
        OnPropertyChanged(nameof(CanOpenVersionSettings));
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (IsBusy) return;
        if (!HasInstalledVersions && !IsSelectedVersionUsable)
        {
            Status = "请先到下载页安装 Minecraft";
            DownloadRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (!IsSelectedVersionUsable)
        {
            Status = "请先选择一个已安装版本（未安装版本请到下载页安装）";
            return;
        }

        var target = SelectedVersion!;
        var gameRoot = _services.Settings.GameDirectory;
        IsBusy = true;
        CanCancel = true;
        Progress = 0;
        ProgressText = "";
        Log = "";
        ModpackLaunchError = "";
        GameFileCheckError = "";
        GameFileCheckOutcome = "";
        HasCrashReport = false;
        CrashTitle = "";
        CrashDetails = "";
        _lastCrashReport = null;
        _lastCrashSnapshot = null;
        Status = $"正在准备启动 {target.Id}…";
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var launchCancellation = _cts;

        try
        {
            using var operation = _services.GameOperations.Acquire(gameRoot, "启动或运行游戏 " + target.Id);
            IsCheckingModpackUpdate = true;
            try
            {
                var updateProgress = new Progress<JobProgress>(update =>
                {
                    if (!ReferenceEquals(_cts, launchCancellation) ||
                        !IsCheckingModpackUpdate || launchCancellation.IsCancellationRequested) return;
                    Status = "启动前必须完成整合包更新：" + update.Stage;
                    Progress = (update.Percent ?? 0) * 100;
                    ProgressText = FormatProgress(update);
                });
                var updater = _modpackUpdater ?? _services.CreateModpackUpdater(gameRoot);
                var result = await updater.PrepareLaunchAsync(target.Id, updateProgress, _cts.Token);
                _cts.Token.ThrowIfCancellationRequested();
                if (result is not null)
                    AppendLog($"[整合包] 启动前更新检查完成：版本 {result.Version ?? "未知"}，下载 {result.Downloaded}，删除 {result.Deleted}，保留用户修改 {result.Preserved.Count}");
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                Status = "已取消整合包检查，未启动游戏";
                return;
            }
            catch (Exception ex)
            {
                Status = "整合包启动前检查失败，未启动游戏";
                ModpackLaunchError = target.Id + "：" + ex.Message;
                AppendLog("[整合包] 启动前检查失败：" + ex.Message);
                return;
            }
            finally
            {
                IsCheckingModpackUpdate = false;
                Progress = 0;
                ProgressText = "";
            }
            if (!string.Equals(gameRoot, _services.Settings.GameDirectory, StringComparison.Ordinal))
                throw new InvalidOperationException("游戏目录已切换，请重新选择实例后启动。");

            if (!await EnsurePurchaseNoticeAsync(ct => DialogService.ConfirmAsync(
                    "支持正版 Minecraft",
                    "Minecraft 是付费游戏。如果你还没有购买，请先通过官方渠道购买正版。\n\n"
                    + "继续启动表示你已知晓；你可以在“关于”页打开正版购买页面。", "继续启动", ct), _cts.Token))
            {
                Status = "已取消启动（请支持正版 Minecraft）";
                return;
            }

            IsCheckingGameFiles = true;
            Status = "正在校验游戏文件…";
            try
            {
                var fileProgress = new Progress<JobProgress>(update =>
                {
                    if (!ReferenceEquals(_cts, launchCancellation) || !IsCheckingGameFiles ||
                        launchCancellation.IsCancellationRequested) return;
                    Status = "启动前校验：" + update.Stage;
                    Progress = Math.Clamp(update.Percent ?? 0, 0, 1) * 100;
                    ProgressText = FormatProgress(update);
                });
                await _services.EnsureGameFilesAsync(gameRoot, target.Id, fileProgress, launchCancellation.Token);
                launchCancellation.Token.ThrowIfCancellationRequested();
                GameFileCheckOutcome = "游戏文件校验完成";
                AppendLog("[校验] 游戏文件检查与补全完成");
            }
            catch (OperationCanceledException) when (launchCancellation.IsCancellationRequested)
            {
                Status = "已取消游戏文件校验，未启动游戏";
                GameFileCheckOutcome = Status;
                return;
            }
            catch (Exception ex)
            {
                Status = "游戏文件校验或补全失败，未启动游戏：" + ex.Message;
                GameFileCheckError = target.Id + "：" + ex.Message;
                GameFileCheckOutcome = Status;
                AppendLog("[校验] " + GameFileCheckError);
                return;
            }
            finally
            {
                IsCheckingGameFiles = false;
                Progress = 0;
                ProgressText = "";
            }
            if (!string.Equals(gameRoot, _services.Settings.GameDirectory, StringComparison.Ordinal))
                throw new InvalidOperationException("游戏目录已切换，请重新选择实例后启动。");

            var preferences = new LaunchPreferences
            {
                IsolationMode = _services.Settings.IsolationMode,
                JavaPath = _services.Settings.JavaPath,
                MemoryMb = (int)MemoryMb,
                Width = _services.Settings.Width,
                Height = _services.Settings.Height,
                ExtraJvmArgs = _services.Settings.ExtraJvmArgs,
                ExtraGameArgs = _services.Settings.ExtraGameArgs,
                ProcessPriority = _services.Settings.ProcessPriority,
                HighPerformanceGpu = _services.Settings.HighPerformanceGpu,
                GpuIndex = _services.Settings.GpuIndex
            };
            var javaProgress = new Progress<JobProgress>(update =>
            {
                Status = update.Percent is null
                    ? update.Stage
                    : $"{update.Stage} {update.Percent * 100:0}%";
            });
            var prepared = await _services.LaunchPreparation.PrepareAsync(
                gameRoot, target.Id, preferences, progress: javaProgress, ct: _cts.Token);
            var instanceSettings = prepared.InstanceSettings;
            var java = prepared.Java;
            var instanceDirectory = prepared.GameDirectory;
            if (prepared.JavaWarning is { } javaWarning)
            {
                WMCL.Core.Logging.Log.Warn(javaWarning);
                Status = javaWarning;
            }

            var requiredAuthServer = instanceSettings.AuthServer.Trim();
            if (requiredAuthServer.Length > 0)
            {
                var match = _authlibAccounts.FirstOrDefault(item =>
                                SameServer(item.Server, requiredAuthServer))
                            ?? (_authlibAccount is not null &&
                                SameServer(_authlibAccount.Server, requiredAuthServer)
                                ? _authlibAccount
                                : null);
                if (match is null || !SameServer(match.Server, requiredAuthServer))
                    throw new InvalidOperationException(
                        $"该实例需要第三方认证服务器 {requiredAuthServer}，请先在启动页使用“第三方”登录该服务器。");
                _authlibAccount = match;
                ApplyAuthlibAccount(match);
            }

            AccountInfo account;
            AuthlibAccount? launchAuthlib = null;
            if (_authlibAccount is not null && IsAuthlibAccount)
            {
                Status = "正在验证第三方登录…";
                launchAuthlib = _authlibAccount;
                if (!await _authlib.ValidateAsync(launchAuthlib, _cts.Token))
                {
                    Status = "第三方登录已过期，正在刷新…";
                    launchAuthlib = await _authlib.RefreshAsync(launchAuthlib, _cts.Token);
                    _authlibAccount = launchAuthlib;
                    _authlibAccounts.RemoveAll(item =>
                        SameServer(item.Server, launchAuthlib.Server) &&
                        string.Equals(item.Uuid, launchAuthlib.Uuid, StringComparison.OrdinalIgnoreCase));
                    _authlibAccounts.Add(launchAuthlib);
                    ApplyAuthlibAccount(launchAuthlib);
                    AuthlibAccountStore.SaveAccount(launchAuthlib, _authlibAccountPath);
                }
                if (requiredAuthServer.Length > 0)
                    launchAuthlib = launchAuthlib with
                    {
                        Server = AuthlibAuthService.NormalizeServer(requiredAuthServer)
                    };
                account = new AccountInfo(
                    launchAuthlib.Name, launchAuthlib.Uuid,
                    launchAuthlib.AccessToken, "mojang");
            }
            else if (IsMicrosoftAccount && _microsoftAccount is not null)
            {
                if (_microsoftAccount.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    var clientId = MicrosoftLoginViewModel.ResolveClientId();
                    if (string.IsNullOrWhiteSpace(clientId))
                        throw new InvalidOperationException(
                            "微软登录已过期，且未配置 Client ID，请重新登录。");

                    Status = "正在刷新微软登录…";
                    var refreshProgress = new Progress<MicrosoftAuthProgress>(update =>
                        Status = update.Stage);
                    _microsoftAccount = await _auth.RefreshAsync(
                        clientId, _microsoftAccount.RefreshToken,
                        refreshProgress, _cts.Token);
                    ApplyMicrosoftAccount(_microsoftAccount);
                }

                account = AccountInfo.FromMicrosoft(_microsoftAccount);
            }
            else
            {
                account = AccountInfo.Offline(PlayerName);
            }

            var jvmPrefix = new List<string>();
            if (launchAuthlib is not null)
            {
                Status = "正在准备 authlib-injector…";
                var injector = await _injector.EnsureAsync(
                    new Progress<JobProgress>(update => Status = update.Stage), _cts.Token);
                jvmPrefix.Add($"-javaagent:{injector}={launchAuthlib.Server}");
            }
            var request = prepared.CreateRequest(account, preferences, jvmPrefix: jvmPrefix);

            if (!string.IsNullOrWhiteSpace(_services.Settings.PreLaunchCommand))
            {
                Status = "正在执行全局启动前命令…";
                await PreLaunchCommandRunner.RunAsync(
                    _services.Settings.PreLaunchCommand,
                    gameRoot, instanceDirectory, target.Id, java,
                    _services.Settings.PreLaunchWait, AppendLog, _cts.Token);
            }
            if (!string.IsNullOrWhiteSpace(instanceSettings.PreLaunchCommand))
            {
                Status = "正在执行实例启动前命令…";
                await PreLaunchCommandRunner.RunAsync(
                    instanceSettings.PreLaunchCommand,
                    gameRoot, instanceDirectory, target.Id, java,
                    instanceSettings.PreLaunchWait, AppendLog, _cts.Token);
            }

            Status = $"已启动（Java {java.MajorVersion}），等待游戏退出…";
            var crashSession = CrashSession.Capture(gameRoot, target.Id, instanceDirectory);
            IsGameRunning = true;
            int exitCode;
            try
            {
                exitCode = await _services.GameLauncher.LaunchAsync(
                    request,
                    new Progress<string>(AppendLog),
                    _cts.Token);
            }
            finally
            {
                IsGameRunning = false;
            }

            // 离线账号：若实例装了 CustomSkinLoader，把本地皮肤同步过去
            if (IsOfflineAccount) OfflineSkinStore.TryApplyToInstance(PlayerName, instanceDirectory);

            var report = CrashAnalyzer.AnalyzeGameDirectory(
                instanceDirectory, exitCode, Log, crashSession);
            ApplyCrashResult(CrashDiagnosticSnapshot.Capture(crashSession, report, exitCode, Log));
        }
        catch (OperationCanceledException)
        {
            Status = "已取消启动";
        }
        catch (Exception ex)
        {
            if (WMCL.App.ViewModels.Accounts.AccountStatusText.IsCredentialExpired(ex))
            {
                MicrosoftLoginExpired = true;
                Status = "启动失败：" + WMCL.App.ViewModels.Accounts.AccountStatusText.Expired(ex.Message);
            }
            else
            {
                Status = "启动失败：" + ex.Message;
            }
        }
        finally
        {
            IsCheckingModpackUpdate = false;
            IsCheckingGameFiles = false;
            Progress = 0;
            ProgressText = "";
            IsBusy = false;
            CanCancel = false;
            _cts.Dispose();
            _cts = null;
        }
    }

}
