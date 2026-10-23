using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Core.Logging;
using WMCL.Launch;
using WMCL.Launch.Auth;
using WMCL.Launch.Crash;
using WMCL.Launch.Diagnostics;
using WMCL.Launch.Java;
using WMCL.Minecraft;
using WMCL.Modding.Modpack;

namespace WMCL.App.ViewModels;

public partial class LaunchViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly McbbsModpackUpdater? _modpackUpdater;
    private readonly Func<string, Task<IReadOnlyList<VersionItem>>> _loadVersions;
    private readonly SemaphoreSlim _versionLoadGate = new(1, 1);
    private readonly List<VersionItem> _all = [];
    private CancellationTokenSource? _cts;
    private bool _offlineNoticeAccepted;
    private readonly MicrosoftAuthService _auth = new();
    private readonly AuthlibAuthService _authlib = new();
    private readonly AuthlibInjectorService _injector = new();
    private readonly AuthlibSkinService _skin = new();
    private readonly MicrosoftSkinService _msSkin = new();
    private MicrosoftAccount? _microsoftAccount;
    private AuthlibAccount? _authlibAccount;
    private readonly List<AuthlibAccount> _authlibAccounts;
    private readonly string? _microsoftAccountPath;
    private readonly string? _authlibAccountPath;
    private readonly Func<MicrosoftAccount, Task<string?>> _downloadMicrosoftSkin;
    private readonly Func<AuthlibAccount, Task<string?>> _downloadAuthlibSkin;
    private readonly Func<MicrosoftAccount, Task<IReadOnlyList<MicrosoftCape>>> _getMicrosoftCapes;
    private readonly MinecraftNewsService? _newsService;
    private readonly Func<MicrosoftAccount, string?, Task> _setMicrosoftCape;
    private int _accountSkinRequest;
    private CrashReport? _lastCrashReport;
    private CrashDiagnosticSnapshot? _lastCrashSnapshot;
    private bool _isVersionLoading;

    public LaunchViewModel(AppServices services, McbbsModpackUpdater? modpackUpdater = null,
        Func<string, Task<IReadOnlyList<VersionItem>>>? loadVersions = null,
        MinecraftNewsService? newsService = null)
        : this(services, modpackUpdater, loadVersions, null, null, null,
            newsService: newsService)
    {
    }

    internal LaunchViewModel(AppServices services, McbbsModpackUpdater? modpackUpdater,
        Func<string, Task<IReadOnlyList<VersionItem>>>? loadVersions, string? accountDirectory,
        Func<MicrosoftAccount, Task<string?>>? downloadMicrosoftSkin,
        Func<AuthlibAccount, Task<string?>>? downloadAuthlibSkin,
        Func<MicrosoftAccount, Task<IReadOnlyList<MicrosoftCape>>>? getMicrosoftCapes = null,
        Func<MicrosoftAccount, string?, Task>? setMicrosoftCape = null,
        MinecraftNewsService? newsService = null)
    {
        _services = services;
        _microsoftAccountPath = accountDirectory is null ? null : Path.Combine(accountDirectory, "microsoft.json");
        _authlibAccountPath = accountDirectory is null ? null : Path.Combine(accountDirectory, "authlib.json");
        _downloadMicrosoftSkin = downloadMicrosoftSkin ?? (account => _msSkin.DownloadSkinAsync(account));
        _downloadAuthlibSkin = downloadAuthlibSkin ?? (account => _skin.DownloadSkinAsync(account));
        _getMicrosoftCapes = getMicrosoftCapes ?? (account => _msSkin.GetCapesAsync(account));
        _setMicrosoftCape = setMicrosoftCape ?? ((account, capeId) => _msSkin.SetCapeAsync(account, capeId));
        _newsService = newsService;
        _modpackUpdater = modpackUpdater;
        _loadVersions = loadVersions ?? (root => new VersionCatalog(new VersionRepository(root)).LoadInstalledAsync());
        services.GameDirectoryChanged += (_, _) => ClearVersionSelection();
        _playerName = services.Settings.PlayerName;
        _memoryMb = services.Settings.MemoryMb;

        _authlibAccounts = AuthlibAccountStore.LoadAll(_authlibAccountPath).ToList();
        RefreshAuthlibAccounts();
        LoadCustomHomePage();
        _microsoftAccount = MicrosoftAccountStore.Load(_microsoftAccountPath);
        _authlibAccount = _authlibAccounts.LastOrDefault();
        if (services.Settings.AccountType == "offline") return;
        if (services.Settings.AccountType == "authlib" && TryUseSavedAuthlibAccount()) return;
        if (_microsoftAccount is not null)
        {
            ApplyMicrosoftAccount(_microsoftAccount);
        }
        else
        {
            var storedAuthlib = _authlibAccounts.LastOrDefault();
            if (storedAuthlib is not null) ApplyAuthlibAccount(storedAuthlib);
        }
    }

    public AppServices Services => _services;
    public ObservableCollection<string> SkinVariants { get; } = ["classic", "slim"];

    [ObservableProperty]
    private string _selectedSkinVariant = "classic";

    public async Task<string> UploadMicrosoftSkinAsync(string pngPath)
    {
        if (_microsoftAccount is null) return "请先登录微软账号";
        try
        {
            await _msSkin.UploadSkinAsync(_microsoftAccount, pngPath, SelectedSkinVariant);
            await LoadMicrosoftSkinAsync(_microsoftAccount);
            return "皮肤已上传";
        }
        catch (Exception ex)
        {
            return "上传皮肤失败：" + ex.Message;
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
    public bool HasAuthlibAccounts => AuthlibAccounts.Count > 0;
    public bool IsAuthlibOptionVisible => !HasInstalledVersions || HasAuthlibAccounts || !string.IsNullOrWhiteSpace(RequiredAuthServer);
    public string AuthlibSkinClosetUrl => BuildAuthlibSkinClosetUrl(
        _authlibAccount?.Server ?? RequiredAuthServer);
    public bool CanOpenAuthlibSkinCloset => IsAuthlibAccount &&
        !string.IsNullOrWhiteSpace(AuthlibSkinClosetUrl);

    internal static string BuildAuthlibSkinClosetUrl(string? server)
    {
        var value = server?.Trim().TrimEnd('/') ?? "";
        if (value.Length == 0) return "";
        if (!value.Contains("/auth/register", StringComparison.OrdinalIgnoreCase))
            value = value.Replace("api/yggdrasil", "auth/register", StringComparison.OrdinalIgnoreCase);
        if (!value.Contains("/auth/register", StringComparison.OrdinalIgnoreCase))
            value += "/auth/register";
        return value.Replace("/auth/register", "/user/closet", StringComparison.OrdinalIgnoreCase);
    }
    public string GameDirectory => _services.Settings.GameDirectory;
    public bool HasVersions => Versions.Count > 0;
    public string SelectedVersionTitle => SelectedVersion?.Id ?? "选择一个版本";
    public bool IsSelectedVersionUsable => SelectedVersion is { IsInstalled: true } version && version.Category != "error";
    public string LaunchButtonText => IsVersionLoading ? "正在加载" : HasInstalledVersions ? "启动游戏" : "下载游戏";
    public string LaunchButtonSubtitle => IsVersionLoading
        ? "正在加载版本列表，请稍候"
        : HasInstalledVersions ? SelectedVersionTitle : "未找到可用的游戏版本";
    public bool IsVersionLoading
    {
        get => _isVersionLoading;
        private set
        {
            if (_isVersionLoading == value) return;
            _isVersionLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LaunchButtonText));
            OnPropertyChanged(nameof(LaunchButtonSubtitle));
        }
    }

    private void ClearVersionSelection()
    {
        SelectedVersion = null;
        _all.Clear();
        Versions.Clear();
        InstalledVersions.Clear();
        OnPropertyChanged(nameof(HasVersions));
        OnPropertyChanged(nameof(HasInstalledVersions));
    }

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
    private bool _isGameRunning;

    [ObservableProperty]
    private bool _isCheckingModpackUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModpackLaunchError))]
    private string _modpackLaunchError = "";

    public bool HasModpackLaunchError => !string.IsNullOrEmpty(ModpackLaunchError);

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private string _log = "";

    public bool HasLaunchLog => !string.IsNullOrWhiteSpace(Log);

    partial void OnLogChanged(string value) =>
        OnPropertyChanged(nameof(HasLaunchLog));

    [ObservableProperty]
    private string _versionInfo = "选择一个版本";

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
    private string? _accountSkinPath;

    [ObservableProperty]
    private AuthlibAccount? _selectedAuthlibAccount;

    [ObservableProperty]
    private string _accountStatus = "离线账户";

    public bool HasAccountSkin => AccountSkin is not null;
    public string? AccountSkinPath => _accountSkinPath;
    public bool HasAccountSkinFile =>
        !string.IsNullOrWhiteSpace(_accountSkinPath) && File.Exists(_accountSkinPath);

    private void SetAccountSkin(string? path)
    {
        _accountSkinPath = path;
        AccountSkin = path is null ? null : new Bitmap(path);
        OnPropertyChanged(nameof(AccountSkinPath));
        OnPropertyChanged(nameof(HasAccountSkinFile));
    }

    [ObservableProperty]
    private Control? _customHomePage;

    [ObservableProperty]
    private string _customHomeError = "";

    public bool HasCustomHomePage => CustomHomePage is not null;

    partial void OnCustomHomePageChanged(Control? value) =>
        OnPropertyChanged(nameof(HasCustomHomePage));

    private void LoadCustomHomePage()
    {
        var path = _services.Settings.CustomHomePagePath?.Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            var xaml = File.ReadAllText(path);
            var control = AvaloniaRuntimeXamlLoader.Parse<Control>(
                xaml, typeof(LaunchViewModel).Assembly);
            control.DataContext = this;
            CustomHomePage = control;
            CustomHomeError = "";
        }
        catch (Exception ex)
        {
            CustomHomeError = "自定义主页加载失败：" + ex.Message;
        }
    }

    partial void OnSelectedAuthlibAccountChanged(AuthlibAccount? value)
    {
        if (value is null || IsAuthlibAccount && _authlibAccount is not null &&
            SameServer(value.Server, _authlibAccount.Server) &&
            string.Equals(value.Uuid, _authlibAccount.Uuid, StringComparison.OrdinalIgnoreCase))
            return;
        ApplyAuthlibAccount(value);
    }

    private void RefreshAuthlibAccounts()
    {
        AuthlibAccounts.Clear();
        foreach (var account in _authlibAccounts) AuthlibAccounts.Add(account);
        OnPropertyChanged(nameof(HasAuthlibAccounts));
        OnPropertyChanged(nameof(IsAuthlibOptionVisible));
    }

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

    [ObservableProperty]
    private bool _isSkinEditorVisible;

    public string SkinEditorButtonText => IsSkinEditorVisible ? "收起皮肤" : "修改皮肤";

    partial void OnIsSkinEditorVisibleChanged(bool value) =>
        OnPropertyChanged(nameof(SkinEditorButtonText));

    private static readonly MicrosoftCape NoCape = new("", "无披风", null, false);
    public ObservableCollection<MicrosoftCape> Capes { get; } = [];
    public ObservableCollection<MinecraftNewsItem> HomeNews { get; } = [];

    [ObservableProperty]
    private bool _isHomeNewsLoading;

    [ObservableProperty]
    private string _homeNewsStatus = "";

    public bool HasHomeNews => HomeNews.Count > 0;
    private bool _homeNewsLoaded;

    public async Task EnsureHomeNewsAsync()
    {
        if (_homeNewsLoaded || _newsService is null) return;
        _homeNewsLoaded = true;
        await LoadHomeNewsAsync();
    }

    [RelayCommand]
    internal async Task LoadHomeNewsAsync()
    {
        if (_newsService is null || IsHomeNewsLoading) return;
        IsHomeNewsLoading = true;
        HomeNewsStatus = "正在加载新闻…";
        try
        {
            var items = await _newsService.GetAsync(1);
            HomeNews.Clear();
            foreach (var item in items.Take(3)) HomeNews.Add(item);
            OnPropertyChanged(nameof(HasHomeNews));
            HomeNewsStatus = HomeNews.Count == 0 ? "没有可显示的新闻" : $"已加载 {HomeNews.Count} 条新闻";
        }
        catch (Exception ex)
        {
            HomeNewsStatus = "加载新闻失败：" + ex.Message;
        }
        finally
        {
            IsHomeNewsLoading = false;
        }
    }

    [ObservableProperty]
    private MicrosoftCape? _selectedCape;

    [ObservableProperty]
    private bool _isChangingCape;

    [ObservableProperty]
    private string _capeStatus = "";

    public bool HasCapes => Capes.Count > 0;

    [RelayCommand]
    private void ToggleSkinEditor()
    {
        if (!IsMicrosoftAccount) return;
        IsSkinEditorVisible = !IsSkinEditorVisible;
        if (IsSkinEditorVisible) _ = LoadCapesAsync();
    }

    internal async Task LoadCapesAsync()
    {
        if (!IsMicrosoftAccount || _microsoftAccount is not { } account) return;
        try
        {
            var capes = await _getMicrosoftCapes(account);
            Capes.Clear();
            Capes.Add(NoCape);
            foreach (var cape in capes.OrderByDescending(cape => cape.Active)) Capes.Add(cape);
            SelectedCape = Capes.FirstOrDefault(cape => cape.Active) ?? NoCape;
            CapeStatus = capes.Count == 0 ? "该账号没有可用披风" : $"共 {capes.Count} 个披风";
            OnPropertyChanged(nameof(HasCapes));
        }
        catch (Exception ex)
        {
            CapeStatus = "获取披风失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshCapesAsync()
    {
        if (!IsMicrosoftAccount) return;
        await LoadCapesAsync();
    }

    [RelayCommand]
    private async Task ChangeCapeAsync()
    {
        if (!IsMicrosoftAccount || _microsoftAccount is not { } account || SelectedCape is null) return;
        IsChangingCape = true;
        CapeStatus = "正在更改披风…";
        try
        {
            await _setMicrosoftCape(account, string.IsNullOrWhiteSpace(SelectedCape.Id) ? null : SelectedCape.Id);
            await LoadCapesAsync();
            await LoadMicrosoftSkinAsync(account);
            CapeStatus = "披风已更新";
        }
        catch (Exception ex)
        {
            CapeStatus = "更改披风失败：" + ex.Message;
        }
        finally
        {
            IsChangingCape = false;
        }
    }

    [RelayCommand]
    private async Task RefreshSkinAsync()
    {
        if (IsMicrosoftAccount && _microsoftAccount is { } microsoft)
        {
            await LoadMicrosoftSkinAsync(microsoft);
            AccountStatus = "皮肤已刷新";
        }
        else if (IsAuthlibAccount && _authlibAccount is { } authlib)
        {
            await LoadSkinAsync(authlib);
            AccountStatus = "皮肤已刷新";
        }
    }

    partial void OnIsAuthlibAccountChanged(bool value) =>
        OnPropertyChanged(nameof(IsOfflineAccount));

    public void SetMicrosoftAccount(MicrosoftAccount account) => ApplyMicrosoftAccount(account);

    public void SetAuthlibAccount(AuthlibAccount account)
    {
        _authlibAccounts.RemoveAll(item =>
            SameServer(item.Server, account.Server) &&
            string.Equals(item.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase));
        _authlibAccounts.Add(account);
        AuthlibAccountStore.SaveAccount(account, _authlibAccountPath);
        RefreshAuthlibAccounts();
        ApplyAuthlibAccount(account);
    }

    private void ApplyMicrosoftAccount(MicrosoftAccount account)
    {
        _microsoftAccount = account;
        PlayerName = account.Name;
        IsAuthlibAccount = false;
        IsMicrosoftAccount = true;
        SaveAccountType("microsoft");
        IsSkinEditorVisible = false;
        OnPropertyChanged(nameof(AuthlibSkinClosetUrl));
        OnPropertyChanged(nameof(CanOpenAuthlibSkinCloset));
        Capes.Clear();
        SelectedCape = null;
        CapeStatus = "";
        OnPropertyChanged(nameof(HasCapes));
        SetAccountSkin(null);
        AccountStatus = $"微软账户 · {account.Name}";
        _ = LoadMicrosoftSkinAsync(account);
    }

    private void ApplyAuthlibAccount(AuthlibAccount account)
    {
        _authlibAccount = account;
        PlayerName = account.Name;
        IsMicrosoftAccount = false;
        IsAuthlibAccount = true;
        SaveAccountType("authlib");
        IsSkinEditorVisible = false;
        OnPropertyChanged(nameof(AuthlibSkinClosetUrl));
        OnPropertyChanged(nameof(CanOpenAuthlibSkinCloset));
        Capes.Clear();
        SelectedCape = null;
        CapeStatus = "";
        OnPropertyChanged(nameof(HasCapes));
        AccountStatus = $"第三方账户 · {account.Name}";
        SetAccountSkin(null);
        if (SelectedAuthlibAccount is null ||
            !SameServer(SelectedAuthlibAccount.Server, account.Server) ||
            !string.Equals(SelectedAuthlibAccount.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase))
            SelectedAuthlibAccount = account;
        _ = LoadSkinAsync(account);
    }

    private async Task LoadMicrosoftSkinAsync(MicrosoftAccount account)
    {
        var request = ++_accountSkinRequest;
        try
        {
            var path = await _downloadMicrosoftSkin(account);
            if (request != _accountSkinRequest || _microsoftAccount is null || !IsMicrosoftAccount ||
                !string.Equals(_microsoftAccount.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase))
                return;
            SetAccountSkin(path);
        }
        catch
        {
            if (request == _accountSkinRequest && IsMicrosoftAccount) SetAccountSkin(null);
        }
    }

    private async Task LoadSkinAsync(AuthlibAccount account)
    {
        var request = ++_accountSkinRequest;
        try
        {
            var path = await _downloadAuthlibSkin(account);
            if (request != _accountSkinRequest || !IsAuthlibAccount || _authlibAccount is null ||
                !SameServer(_authlibAccount.Server, account.Server) ||
                !string.Equals(_authlibAccount.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase))
                return;
            SetAccountSkin(path);
        }
        catch
        {
            if (request == _accountSkinRequest && IsAuthlibAccount) SetAccountSkin(null);
        }
    }

    public bool TryUseSavedMicrosoftAccount()
    {
        if (_microsoftAccount is null) return false;
        ApplyMicrosoftAccount(_microsoftAccount);
        return true;
    }

    public bool TryUseSavedAuthlibAccount()
    {
        var requiredServer = RequiredAuthServer;
        var account = string.IsNullOrWhiteSpace(requiredServer)
            ? _authlibAccount ?? _authlibAccounts.LastOrDefault()
            : _authlibAccounts.LastOrDefault(item => SameServer(item.Server, requiredServer));
        if (account is null) return false;
        ApplyAuthlibAccount(account);
        return true;
    }

    public string? RequiredAuthServer => SelectedVersion is { IsInstalled: true } target
        ? InstanceSettingsStore.Load(Path.Combine(GameDirectory, "versions", target.Id)).AuthServer
        : null;

    [RelayCommand]
    private void SelectOfflineAccount()
    {
        _accountSkinRequest++;
        IsMicrosoftAccount = false;
        IsAuthlibAccount = false;
        IsSkinEditorVisible = false;
        SetAccountSkin(null);
        PlayerName = _services.Settings.PlayerName;
        AccountStatus = "离线账户";
        SaveAccountType("offline");
    }

    private void SaveAccountType(string type)
    {
        if (_services.Settings.AccountType == type) return;
        _services.Settings.AccountType = type;
        _services.SaveSettings();
    }

    [RelayCommand]
    private void LogoutMicrosoft()
    {
        _microsoftAccount = null;
        MicrosoftAccountStore.Clear(_microsoftAccountPath);
        _accountSkinRequest++;
        SetAccountSkin(null);
        IsMicrosoftAccount = false;
        if (!IsAuthlibAccount)
        {
            SelectOfflineAccount();
        }
    }

    [RelayCommand]
    private void LogoutAuthlib()
    {
        if (_authlibAccount is not null)
        {
            AuthlibAccountStore.Remove(_authlibAccount.Server, _authlibAccount.Uuid, _authlibAccountPath);
            _authlibAccounts.RemoveAll(item =>
                SameServer(item.Server, _authlibAccount.Server) &&
                string.Equals(item.Uuid, _authlibAccount.Uuid, StringComparison.OrdinalIgnoreCase));
        }
        _authlibAccount = _authlibAccounts.LastOrDefault();
        _accountSkinRequest++;
        RefreshAuthlibAccounts();
        SelectedAuthlibAccount = _authlibAccount;
        SetAccountSkin(null);
        if (_authlibAccount is not null)
        {
            ApplyAuthlibAccount(_authlibAccount);
            return;
        }
        IsAuthlibAccount = false;
        if (!IsMicrosoftAccount)
        {
            SelectOfflineAccount();
        }
    }

    public async Task LoadAsync(string? preferredVersion = null)
    {
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
        ModpackLaunchError = "";
        OnPropertyChanged(nameof(IsSelectedVersionUsable));
        OnPropertyChanged(nameof(SelectedVersionTitle));
        OnPropertyChanged(nameof(LaunchButtonSubtitle));
        OnPropertyChanged(nameof(CanOpenVersionSettings));
        OnPropertyChanged(nameof(IsAuthlibOptionVisible));
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
    private void OpenVersionSettings() => RequestVersionSettings();

    public void RequestVersionSettings()
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

    private async Task RunPreLaunchAsync(
        string rawCommand,
        string gameRoot,
        string instanceDirectory,
        string versionId,
        JavaRuntime java,
        bool wait,
        CancellationToken ct)
    {
        var command = rawCommand
            .Replace("{java}", java.Path, StringComparison.OrdinalIgnoreCase)
            .Replace("{minecraft}", gameRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{verpath}", instanceDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("{name}", versionId, StringComparison.OrdinalIgnoreCase);

        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe")
            : new ProcessStartInfo("/bin/sh");
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(command);
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(command);
        }
        startInfo.WorkingDirectory = gameRoot;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = wait;
        startInfo.RedirectStandardError = wait;

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("启动前命令无法启动。");
        AppendLog("[启动前命令] " + command);
        if (!wait) return;

        string stdout, stderr;
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(ct))
                .ConfigureAwait(false);
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }
            throw;
        }

        if (!string.IsNullOrWhiteSpace(stdout)) AppendLog(stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(stderr)) AppendLog(stderr.TrimEnd());
        AppendLog($"[启动前命令] 退出码 {process.ExitCode}");
    }

    private void UpdateCommands()
    {
        CanLaunch = !IsBusy && IsSelectedVersionUsable;
    }

    private void ApplyFilter(string? selectedId = null)
    {
        selectedId ??= SelectedVersion?.Id;
        Versions.Clear();
        foreach (var item in _all)
            Versions.Add(item);
        InstalledVersions.Clear();
        foreach (var item in _all.Where(item => item.IsInstalled))
            InstalledVersions.Add(item);
        OnPropertyChanged(nameof(HasInstalledVersions));
        OnPropertyChanged(nameof(IsAuthlibOptionVisible));
        OnPropertyChanged(nameof(LaunchButtonText));
        OnPropertyChanged(nameof(LaunchButtonSubtitle));

        SelectedVersion = InstalledVersions.FirstOrDefault(version => version.Id == selectedId && version.Category != "error")
                          ?? InstalledVersions.FirstOrDefault(version => !version.IsHidden && version.Category != "error");

        OnPropertyChanged(nameof(HasVersions));
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (SelectedVersion is null || IsBusy) return;

        var target = SelectedVersion;
        var root = _services.Settings.GameDirectory;
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

            using var operation = _services.GameOperations.Acquire(root, "安装游戏");
            var result = await new VersionInstaller(new VersionRepository(root)).InstallAsync(
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
    private void SaveDiagnosticPackage()
    {
        if (!HasCrashReport || _lastCrashSnapshot is null) return;
        try
        {
            var path = WriteDiagnosticPackage(WmclPaths.LogsDirectory);
            Status = "诊断包已保存：" + path;
            OpenPath(path);
        }
        catch (Exception ex)
        {
            Status = "保存诊断包失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenCrashReport()
    {
        var path = _lastCrashReport?.CrashReportPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Status = "没有找到 Minecraft crash-report 文件";
            return;
        }
        OpenPath(path);
    }

    [RelayCommand]
    private void OpenCrashLog()
    {
        var path = _lastCrashReport?.LogPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Status = "没有找到崩溃日志文件";
            return;
        }
        OpenPath(path);
    }

    [RelayCommand]
    private void OpenCrashLogFolder()
    {
        var path = _lastCrashReport?.LogPath is { } log && File.Exists(log)
            ? Path.GetDirectoryName(log)!
            : Path.Combine(_lastCrashSnapshot?.Session.GameDirectory ?? _services.Settings.GameDirectory, "logs");
        if (Directory.Exists(path)) OpenPath(path);
        else Status = "日志目录不存在：" + path;
    }

    [RelayCommand]
    private void SaveCrashReport()
    {
        if (!HasCrashReport || _lastCrashSnapshot is null) return;
        try
        {
            var path = WriteCrashReport(WmclPaths.LogsDirectory);
            Status = "崩溃报告已保存：" + path;
            OpenPath(path);
        }
        catch (Exception ex)
        {
            Status = "保存崩溃报告失败：" + ex.Message;
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            ExternalLauncher.Open(path);
        }
        catch
        {
            // 忽略
        }
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
        HasCrashReport = false;
        CrashTitle = "";
        CrashDetails = "";
        _lastCrashReport = null;
        _lastCrashSnapshot = null;
        Status = $"正在准备启动 {target.Id}…";
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

            if (!_offlineNoticeAccepted && !StartupOptions.IsAutomation &&
                (IsOfflineAccount || IsAuthlibAccount))
            {
                var accepted = await DialogService.ConfirmAsync(
                    "支持正版 Minecraft",
                    "Minecraft 是付费游戏。如果你还没有购买，请先通过官方渠道购买正版。\n\n"
                    + "继续启动表示你已知晓；你可以在“关于”页打开正版购买页面。", _cts.Token);
                if (!accepted)
                {
                    Status = "已取消启动（请支持正版 Minecraft）";
                    return;
                }
                _offlineNoticeAccepted = true;
            }

            var version = await new VersionRepository(gameRoot).GetVersionAsync(target.Id, _cts.Token);
            var installManifest = InstallManifestStore.Load(gameRoot, target.Id);
            var javaRequirement = JavaRequirement.ForVersion(version, installManifest);
            var supportedJava = installManifest?.McbbsSupportJava ?? [];

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
                       ?? throw new InvalidOperationException($"无法识别指定的 Java：{effectiveJavaPath}，请重新选择 Java 后再启动。");
            }
            else
            {
                java = await _services.JavaProvisioner.EnsureAsync(
                    javaRequirement, javaProgress, _cts.Token);
            }
            if (javaRequirement.WarningFor(java) is { } javaWarning)
            {
                WMCL.Core.Logging.Log.Warn(javaWarning);
                Status = javaWarning;
            }
            if (supportedJava.Count > 0 && !supportedJava.Contains(java.MajorVersion))
                throw new InvalidOperationException(
                    $"该整合包不支持 Java {java.MajorVersion}，仅支持 Java {string.Join("、", supportedJava.OrderBy(major => major))}。");

            var instanceDirectory = IsolationPolicy.GetGameDirectory(
                gameRoot, target.Id, version, _services.Settings.IsolationMode);
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
                MemoryMb = Math.Max(instanceSettings.MemoryMb ?? (int)MemoryMb,
                    installManifest?.McbbsMinMemoryMb ?? 0),
                Width = instanceSettings.Width ?? _services.Settings.Width,
                Height = instanceSettings.Height ?? _services.Settings.Height,
                ExtraJvmArguments = extraJvmArguments,
                ExtraGameArguments = extraGameArguments,
                ProcessPriority = _services.Settings.ProcessPriority,
                HighPerformanceGpu = _services.Settings.HighPerformanceGpu
            };

            if (!string.IsNullOrWhiteSpace(_services.Settings.PreLaunchCommand))
            {
                Status = "正在执行全局启动前命令…";
                await RunPreLaunchAsync(
                    _services.Settings.PreLaunchCommand,
                    gameRoot, instanceDirectory, target.Id, java,
                    _services.Settings.PreLaunchWait, _cts.Token);
            }
            if (!string.IsNullOrWhiteSpace(instanceSettings.PreLaunchCommand))
            {
                Status = "正在执行实例启动前命令…";
                await RunPreLaunchAsync(
                    instanceSettings.PreLaunchCommand,
                    gameRoot, instanceDirectory, target.Id, java,
                    instanceSettings.PreLaunchWait, _cts.Token);
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
            Status = "启动失败：" + ex.Message;
        }
        finally
        {
            IsCheckingModpackUpdate = false;
            Progress = 0;
            ProgressText = "";
            IsBusy = false;
            CanCancel = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_cts is not { } cancellation || cancellation.IsCancellationRequested) return;
        Status = "正在取消…";
        cancellation.Cancel();
    }

    [RelayCommand]
    private void ClearLog() => Log = "";

    internal void ApplyCrashResult(CrashDiagnosticSnapshot snapshot)
    {
        _lastCrashSnapshot = snapshot;
        _lastCrashReport = snapshot.Report;
        HasCrashReport = snapshot.Report.HasFindings;
        CrashTitle = snapshot.Report.Findings.FirstOrDefault()?.Title ?? "";
        CrashDetails = string.Join(Environment.NewLine + Environment.NewLine, snapshot.Report.Findings.Select(finding =>
            $"{finding.Title}：{finding.Detail}{Environment.NewLine}建议：{finding.Suggestion}"));
        Status = HasCrashReport
            ? $"游戏异常退出（退出码 {snapshot.ExitCode}）：{snapshot.Report.Summary}"
            : $"游戏已退出（退出码 {snapshot.ExitCode}）";
    }

    internal string WriteDiagnosticPackage(string directory) => DiagnosticPackage.Export(directory,
        _lastCrashSnapshot ?? throw new InvalidOperationException("没有可导出的故障快照。"));

    internal string WriteCrashReport(string directory)
    {
        var snapshot = _lastCrashSnapshot ?? throw new InvalidOperationException("没有可导出的故障快照。");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, snapshot.ReportText);
        return path;
    }

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
