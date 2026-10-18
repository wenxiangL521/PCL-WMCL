using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Core.Jobs;
using PCL.Launch;
using PCL.Launch.Auth;
using PCL.Launch.Crash;
using PCL.Launch.Java;
using PCL.Minecraft;

namespace PCL.App.ViewModels;

public partial class LaunchViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly VersionCatalog _catalog;
    private readonly List<VersionItem> _all = [];
    private CancellationTokenSource? _cts;
    private readonly MicrosoftAuthService _auth = new();
    private readonly AuthlibAuthService _authlib = new();
    private readonly AuthlibInjectorService _injector = new();
    private readonly AuthlibSkinService _skin = new();
    private MicrosoftAccount? _microsoftAccount;
    private AuthlibAccount? _authlibAccount;
    private readonly List<AuthlibAccount> _authlibAccounts;

    public LaunchViewModel(AppServices services)
    {
        _services = services;
        _catalog = new VersionCatalog(services.Repository);
        _playerName = services.Settings.PlayerName;
        _memoryMb = services.Settings.MemoryMb;

        _authlibAccounts = AuthlibAccountStore.LoadAll().ToList();
        var stored = MicrosoftAccountStore.Load();
        if (stored is not null)
        {
            ApplyMicrosoftAccount(stored);
        }
        else
        {
            var storedAuthlib = _authlibAccounts.LastOrDefault();
            if (storedAuthlib is not null) ApplyAuthlibAccount(storedAuthlib);
        }
    }

    public AppServices Services => _services;
    public AuthlibAuthService AuthlibService => _authlib;
    public event EventHandler<string>? VersionSettingsRequested;
    public bool IsOfflineAccount => !IsMicrosoftAccount && !IsAuthlibAccount;
    public bool CanOpenVersionSettings => !IsBusy && SelectedVersion is { IsInstalled: true };

    public ObservableCollection<VersionItem> Versions { get; } = [];
    public ObservableCollection<string> Filters { get; } = ["全部", "已安装", "正式版", "快照"];
    public string GameDirectory => _services.Settings.GameDirectory;
    public bool HasVersions => Versions.Count > 0;
    public string SelectedVersionTitle => SelectedVersion?.Id ?? "选择一个版本";

    [ObservableProperty]
    private string _selectedFilter = "全部";

    [ObservableProperty]
    private VersionItem? _selectedVersion;

    [ObservableProperty]
    private string _playerName = "";

    [ObservableProperty]
    private decimal _memoryMb = 4096;

    [ObservableProperty]
    private string _status = "就绪";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private string _log = "";

    [ObservableProperty]
    private string _versionInfo = "选择一个版本";

    [ObservableProperty]
    private bool _canInstall;

    [ObservableProperty]
    private bool _canLaunch;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private bool _isMicrosoftAccount;

    [ObservableProperty]
    private bool _isAuthlibAccount;

    [ObservableProperty]
    private Bitmap? _accountSkin;

    [ObservableProperty]
    private string _accountStatus = "离线账户";

    public bool HasAccountSkin => AccountSkin is not null;

    partial void OnAccountSkinChanged(Bitmap? value) =>
        OnPropertyChanged(nameof(HasAccountSkin));

    [ObservableProperty]
    private bool _hasCrashReport;

    [ObservableProperty]
    private string _crashTitle = "";

    [ObservableProperty]
    private string _crashDetails = "";

    public string PlayerInitial =>
        string.IsNullOrWhiteSpace(PlayerName) ? "?" : PlayerName[..1].ToUpperInvariant();

    partial void OnPlayerNameChanged(string value) =>
        OnPropertyChanged(nameof(PlayerInitial));

    partial void OnIsMicrosoftAccountChanged(bool value) =>
        OnPropertyChanged(nameof(IsOfflineAccount));

    partial void OnIsAuthlibAccountChanged(bool value) =>
        OnPropertyChanged(nameof(IsOfflineAccount));

    public void SetMicrosoftAccount(MicrosoftAccount account) => ApplyMicrosoftAccount(account);

    public void SetAuthlibAccount(AuthlibAccount account)
    {
        _authlibAccounts.RemoveAll(item =>
            SameServer(item.Server, account.Server) &&
            string.Equals(item.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase));
        _authlibAccounts.Add(account);
        AuthlibAccountStore.SaveAccount(account);
        ApplyAuthlibAccount(account);
    }

    private void ApplyMicrosoftAccount(MicrosoftAccount account)
    {
        _microsoftAccount = account;
        PlayerName = account.Name;
        IsAuthlibAccount = false;
        IsMicrosoftAccount = true;
        AccountSkin = null;
        AccountStatus = $"微软账户 · {account.Name}";
    }

    private void ApplyAuthlibAccount(AuthlibAccount account)
    {
        _authlibAccount = account;
        PlayerName = account.Name;
        IsMicrosoftAccount = false;
        IsAuthlibAccount = true;
        AccountStatus = $"第三方账户 · {account.Name}";
        AccountSkin = null;
        _ = LoadSkinAsync(account);
    }

    private async Task LoadSkinAsync(AuthlibAccount account)
    {
        try
        {
            var path = await _skin.DownloadSkinAsync(account);
            if (_authlibAccount is null ||
                !string.Equals(_authlibAccount.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase))
                return;
            AccountSkin = path is null ? null : new Bitmap(path);
        }
        catch
        {
            AccountSkin = null;
        }
    }

    [RelayCommand]
    private void LogoutMicrosoft()
    {
        _microsoftAccount = null;
        MicrosoftAccountStore.Clear();
        IsMicrosoftAccount = false;
        if (!IsAuthlibAccount)
        {
            AccountStatus = "离线账户";
            PlayerName = "Steve";
        }
    }

    [RelayCommand]
    private void LogoutAuthlib()
    {
        if (_authlibAccount is not null)
        {
            AuthlibAccountStore.Remove(_authlibAccount.Server, _authlibAccount.Uuid);
            _authlibAccounts.RemoveAll(item =>
                SameServer(item.Server, _authlibAccount.Server) &&
                string.Equals(item.Uuid, _authlibAccount.Uuid, StringComparison.OrdinalIgnoreCase));
        }
        _authlibAccount = _authlibAccounts.LastOrDefault();
        AccountSkin = null;
        if (_authlibAccount is not null)
        {
            ApplyAuthlibAccount(_authlibAccount);
            return;
        }
        IsAuthlibAccount = false;
        if (!IsMicrosoftAccount)
        {
            AccountStatus = "离线账户";
            PlayerName = "Steve";
        }
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        Status = "正在获取版本列表…";
        try
        {
            var items = await _catalog.LoadAsync();
            _all.Clear();
            _all.AddRange(items);
            ApplyFilter();
            Status = items.Count == 0 ? "没有可用版本" : $"共 {items.Count} 个版本";
        }
        catch (Exception ex)
        {
            Status = "加载版本列表失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            UpdateCommands();
        }
    }

    partial void OnSelectedFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedVersionChanged(VersionItem? value)
    {
        OnPropertyChanged(nameof(SelectedVersionTitle));
        OnPropertyChanged(nameof(CanOpenVersionSettings));
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
    private void OpenVersionSettings()
    {
        if (SelectedVersion is { IsInstalled: true } version)
            VersionSettingsRequested?.Invoke(this, version.Id);
    }

    private static bool SameServer(string left, string right)
    {
        try
        {
            return string.Equals(
                AuthlibAuthService.NormalizeServer(left),
                AuthlibAuthService.NormalizeServer(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void UpdateCommands()
    {
        CanInstall = !IsBusy && SelectedVersion is { IsInstalled: false };
        CanLaunch = !IsBusy && SelectedVersion is { IsInstalled: true };
    }

    private void ApplyFilter()
    {
        Versions.Clear();
        foreach (var item in _all.Where(MatchesFilter))
            Versions.Add(item);

        if (SelectedVersion is null || !Versions.Contains(SelectedVersion))
        {
            SelectedVersion = Versions.FirstOrDefault(v => v.IsInstalled)
                              ?? Versions.FirstOrDefault(v => v.Type == "release" && v.IsLatest)
                              ?? Versions.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasVersions));
    }

    private bool MatchesFilter(VersionItem item) => SelectedFilter switch
    {
        "已安装" => item.IsInstalled,
        "正式版" => item.Type == "release",
        "快照" => item.Type == "snapshot",
        _ => true
    };

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (SelectedVersion is null || IsBusy) return;

        var target = SelectedVersion;
        IsBusy = true;
        CanCancel = true;
        Progress = 0;
        ProgressText = "";
        Status = $"正在安装 {target.Id}…";
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                ProgressText = FormatProgress(update);
            });

            var result = await _services.Installer.InstallAsync(
                target.Id,
                new InstallOptions
                {
                    Mirror = _services.Mirror,
                    MaxConcurrency = 16
                },
                progress,
                _cts.Token);

            target.IsInstalled = true;
            Status = $"安装完成：{result.Files} 个文件，{FormatBytes(result.Bytes)}";
            Progress = 100;
        }
        catch (OperationCanceledException)
        {
            Status = "已取消安装";
        }
        catch (Exception ex)
        {
            Status = "安装失败：" + ex.Message;
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
    private async Task LaunchAsync()
    {
        if (SelectedVersion is null || IsBusy) return;

        var target = SelectedVersion;
        IsBusy = true;
        CanCancel = true;
        Log = "";
        HasCrashReport = false;
        CrashTitle = "";
        CrashDetails = "";
        Status = $"正在准备启动 {target.Id}…";
        _cts = new CancellationTokenSource();

        try
        {
            var version = await _services.Repository.GetVersionAsync(target.Id, _cts.Token);
            var required = version.JavaVersion?.MajorVersion
                           ?? JavaLocator.InferMajorVersion(target.Id);

            var gameRoot = _services.Settings.GameDirectory;
            var instanceSettings = InstanceSettingsStore.Load(
                Path.Combine(gameRoot, "versions", target.Id));
            var effectiveJavaPath = string.IsNullOrWhiteSpace(instanceSettings.JavaPath)
                ? _services.Settings.JavaPath
                : instanceSettings.JavaPath;

            var javaProgress = new Progress<JobProgress>(update =>
            {
                Status = update.Percent is null
                    ? update.Stage
                    : $"{update.Stage} {update.Percent * 100:0}%";
            });

            JavaRuntime java;
            if (!string.IsNullOrWhiteSpace(effectiveJavaPath))
            {
                java = await _services.JavaLocator.ProbePathAsync(
                           effectiveJavaPath, _cts.Token)
                       ?? await _services.JavaProvisioner.EnsureAsync(
                           required, javaProgress, _cts.Token);
            }
            else
            {
                java = await _services.JavaProvisioner.EnsureAsync(
                    required, javaProgress, _cts.Token);
            }

            var instanceDirectory = MinecraftVersionUtil.GetGameDirectory(
                gameRoot, target.Id, version);
            Directory.CreateDirectory(instanceDirectory);

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
                    AuthlibAccountStore.SaveAccount(launchAuthlib);
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
            else if (_microsoftAccount is not null)
            {
                if (_microsoftAccount.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    var clientId = _services.Settings.MicrosoftClientId?.Trim();
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

            var extraJvmArguments = CommandLineSplitter
                .Split(_services.Settings.ExtraJvmArgs)
                .Concat(CommandLineSplitter.Split(instanceSettings.ExtraJvmArgs))
                .ToList();
            var extraGameArguments = CommandLineSplitter
                .Split(_services.Settings.ExtraGameArgs)
                .Concat(CommandLineSplitter.Split(instanceSettings.ExtraGameArgs))
                .ToList();
            if (!string.IsNullOrWhiteSpace(instanceSettings.Server))
            {
                extraGameArguments.Add("--server");
                extraGameArguments.Add(instanceSettings.Server.Trim());
                extraGameArguments.Add("--port");
                extraGameArguments.Add((instanceSettings.Port ?? 25565).ToString());
            }

            if (launchAuthlib is not null)
            {
                Status = "正在准备 authlib-injector…";
                var injector = await _injector.EnsureAsync(
                    new Progress<JobProgress>(update => Status = update.Stage), _cts.Token);
                extraJvmArguments.Insert(0, $"-javaagent:{injector}={launchAuthlib.Server}");
            }

            var request = new LaunchRequest
            {
                Version = version,
                VersionId = target.Id,
                GameDirectory = instanceDirectory,
                MinecraftDirectory = gameRoot,
                Java = java,
                Account = account,
                MemoryMb = instanceSettings.MemoryMb ?? (int)MemoryMb,
                Width = instanceSettings.Width ?? _services.Settings.Width,
                Height = instanceSettings.Height ?? _services.Settings.Height,
                ExtraJvmArguments = extraJvmArguments,
                ExtraGameArguments = extraGameArguments
            };

            Status = $"已启动（Java {java.MajorVersion}），等待游戏退出…";
            var exitCode = await _services.GameLauncher.LaunchAsync(
                request,
                new Progress<string>(AppendLog),
                _cts.Token);

            var report = CrashAnalyzer.AnalyzeGameDirectory(
                instanceDirectory, exitCode, Log);
            if (report.HasFindings)
            {
                HasCrashReport = true;
                CrashTitle = report.Findings[0].Title;
                CrashDetails = string.Join(Environment.NewLine + Environment.NewLine,
                    report.Findings.Select(finding =>
                        $"{finding.Title}：{finding.Detail}{Environment.NewLine}建议：{finding.Suggestion}"));
                Status = $"游戏异常退出（退出码 {exitCode}）：{report.Summary}";
            }
            else
            {
                Status = $"游戏已退出（退出码 {exitCode}）";
            }
        }
        catch (OperationCanceledException)
        {
            Status = "已取消启动";
        }
        catch (Exception ex)
        {
            Status = "启动失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            CanCancel = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        Status = "正在取消…";
    }

    [RelayCommand]
    private void ClearLog() => Log = "";

    private void AppendLog(string line)
    {
        var cleaned = LogSanitizer.Clean(line);
        if (cleaned is null) return;

        var text = Log + cleaned + Environment.NewLine;
        if (text.Length > 200_000)
            text = text[^200_000..];
        Log = text;
    }

    private static string FormatProgress(JobProgress progress)
    {
        var percent = progress.Percent is null ? "" : $"{progress.Percent * 100:0.0}%";
        var speed = progress.BytesPerSecond is > 0
            ? $" · {progress.BytesPerSecond / 1048576.0:0.0} MB/s"
            : "";
        var detail = string.IsNullOrWhiteSpace(progress.Detail) ? "" : $" · {progress.Detail}";
        return $"{progress.Stage} {percent}{speed}{detail}".Trim();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.0} {units[unit]}";
    }

    private static string FormatDate(string? value) =>
        DateTimeOffset.TryParse(value, out var time)
            ? time.LocalDateTime.ToString("yyyy-MM-dd")
            : value ?? "未知时间";
}
