using System.Collections.ObjectModel;
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
    private MicrosoftAccount? _microsoftAccount;

    public LaunchViewModel(AppServices services)
    {
        _services = services;
        _catalog = new VersionCatalog(services.Repository);
        _playerName = services.Settings.PlayerName;
        _memoryMb = services.Settings.MemoryMb;

        var stored = MicrosoftAccountStore.Load();
        if (stored is not null) ApplyMicrosoftAccount(stored);
    }

    public AppServices Services => _services;
    public bool IsOfflineAccount => !IsMicrosoftAccount;

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
    private string _accountStatus = "离线账户";

    [ObservableProperty]
    private bool _hasCrashReport;

    [ObservableProperty]
    private string _crashTitle = "";

    [ObservableProperty]
    private string _crashDetails = "";

    partial void OnIsMicrosoftAccountChanged(bool value) =>
        OnPropertyChanged(nameof(IsOfflineAccount));

    public void SetMicrosoftAccount(MicrosoftAccount account) => ApplyMicrosoftAccount(account);

    private void ApplyMicrosoftAccount(MicrosoftAccount account)
    {
        _microsoftAccount = account;
        PlayerName = account.Name;
        IsMicrosoftAccount = true;
        AccountStatus = $"微软账户 · {account.Name}";
    }

    [RelayCommand]
    private void LogoutMicrosoft()
    {
        _microsoftAccount = null;
        MicrosoftAccountStore.Clear();
        IsMicrosoftAccount = false;
        AccountStatus = "离线账户";
        PlayerName = "Steve";
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
        VersionInfo = value is null
            ? "选择一个版本"
            : $"{value.Id} · {value.TypeText}{(value.IsLatest ? " · 最新" : "")} · {FormatDate(value.ReleaseTime)}";
        UpdateCommands();
    }

    partial void OnIsBusyChanged(bool value) => UpdateCommands();

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

            AccountInfo account;
            if (_microsoftAccount is not null)
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

            var extraGameArguments = CommandLineSplitter
                .Split(instanceSettings.ExtraGameArgs).ToList();
            if (!string.IsNullOrWhiteSpace(instanceSettings.Server))
            {
                extraGameArguments.Add("--server");
                extraGameArguments.Add(instanceSettings.Server.Trim());
                extraGameArguments.Add("--port");
                extraGameArguments.Add((instanceSettings.Port ?? 25565).ToString());
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
                ExtraJvmArguments = CommandLineSplitter.Split(instanceSettings.ExtraJvmArgs),
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
