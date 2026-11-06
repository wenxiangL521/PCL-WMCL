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

public partial class LaunchViewModel : ViewModelBase
{
    private readonly IInstallServices _services;
    private readonly McbbsModpackUpdater? _modpackUpdater;
    private readonly Func<string, Task<IReadOnlyList<VersionItem>>> _loadVersions;
    private readonly SemaphoreSlim _versionLoadGate = new(1, 1);
    private readonly List<VersionItem> _all = [];
    private CancellationTokenSource? _cts;
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
    private readonly Func<MicrosoftAccount, Task<MicrosoftSkinDownload>>? _downloadMicrosoftSkinWithInfo;
    private readonly Func<MicrosoftAccount, string, string, Task> _uploadMicrosoftSkin;
    private int _skinAccountGeneration;
    private readonly Func<AuthlibAccount, Task<string?>> _downloadAuthlibSkin;
    private readonly Func<MicrosoftAccount, Task<IReadOnlyList<MicrosoftCape>>> _getMicrosoftCapes;
    private readonly MinecraftNewsService? _newsService;
    private readonly Func<MicrosoftAccount, string?, Task> _setMicrosoftCape;
    private int _accountSkinRequest;
    private CrashReport? _lastCrashReport;
    private CrashDiagnosticSnapshot? _lastCrashSnapshot;
    private bool _isVersionLoading;

    public LaunchViewModel(IInstallServices services, McbbsModpackUpdater? modpackUpdater = null,
        Func<string, Task<IReadOnlyList<VersionItem>>>? loadVersions = null,
        MinecraftNewsService? newsService = null)
        : this(services, modpackUpdater, loadVersions, null, null, null,
            newsService: newsService)
    {
    }

    internal LaunchViewModel(IInstallServices services, McbbsModpackUpdater? modpackUpdater,
        Func<string, Task<IReadOnlyList<VersionItem>>>? loadVersions, string? accountDirectory,
        Func<MicrosoftAccount, Task<string?>>? downloadMicrosoftSkin,
        Func<AuthlibAccount, Task<string?>>? downloadAuthlibSkin,
        Func<MicrosoftAccount, Task<IReadOnlyList<MicrosoftCape>>>? getMicrosoftCapes = null,
        Func<MicrosoftAccount, string?, Task>? setMicrosoftCape = null,
        MinecraftNewsService? newsService = null,
        Func<MicrosoftAccount, string, string, Task>? uploadMicrosoftSkin = null,
        Func<MicrosoftAccount, Task<MicrosoftSkinDownload>>? downloadMicrosoftSkinWithInfo = null)
    {
        _services = services;
        _microsoftAccountPath = accountDirectory is null ? null : Path.Combine(accountDirectory, "microsoft.json");
        _authlibAccountPath = accountDirectory is null ? null : Path.Combine(accountDirectory, "authlib.json");
        _downloadMicrosoftSkin = downloadMicrosoftSkin ?? (account => _msSkin.DownloadSkinAsync(account));
        _downloadMicrosoftSkinWithInfo = downloadMicrosoftSkinWithInfo ?? (downloadMicrosoftSkin is null
            ? account => _msSkin.DownloadSkinWithInfoAsync(account, forceRefresh: true) : null);
        _uploadMicrosoftSkin = uploadMicrosoftSkin ?? ((account, path, variant) => _msSkin.UploadSkinAsync(account, path, variant));
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
        services.Features.Changed += (_, _) => OnPropertyChanged(nameof(IsHomeNewsVisible));
        RefreshAuthlibAccounts();
        LoadCustomHomePage();
        RefreshOfflineRoles();
        _microsoftAccount = MicrosoftAccountStore.Load(_microsoftAccountPath);
        _microsoftAccounts.AddRange(MicrosoftAccountStore.LoadAll(MicrosoftAccountStore.ListPathFor(_microsoftAccountPath)));
        if (_microsoftAccounts.Count == 0 && _microsoftAccount is not null)
            _microsoftAccounts.Add(_microsoftAccount);   // 旧版单账号迁移
        if (_microsoftAccount is not null && _microsoftAccounts.All(item => item.Uuid != _microsoftAccount.Uuid))
            _microsoftAccounts.Add(_microsoftAccount);
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

    public IInstallServices Services => _services;
    public ObservableCollection<string> SkinVariants { get; } = ["auto", "classic", "slim"];

    [ObservableProperty]
    private string _selectedSkinVariant = "auto";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUploadMicrosoftSkin))]
    private bool _isUploadingSkin;

    [ObservableProperty]
    private string _skinUploadStatus = "";

    [ObservableProperty]
    private string _currentSkinVariant = "classic";


    /// <summary>删除指定离线角色；删空后自动保留一个默认角色。</summary>
    public void DeleteOfflineRole(string? name)
    {
        var target = string.IsNullOrWhiteSpace(name) ? PlayerName : name!;
        var roles = _services.Settings.OfflineRoles;
        if (!roles.Remove(target)) return;
        if (roles.Count == 0) roles.Add("Steve");
        var next = roles[^1];
        _services.Settings.PlayerName = next;
        _services.SaveSettings();
        RefreshOfflineRoles();
        if (IsOfflineAccount) PlayerName = next;
        RefreshOfflineSkin();
        AccountStatus = "已删除角色：" + target;
    }

    /// <summary>删除指定微软账号；删除当前账号时切到剩下的账号或离线。</summary>
    public void DeleteMicrosoftAccount(MicrosoftAccount? account)
    {
        var target = account ?? _microsoftAccount;
        if (target is null) return;
        _microsoftAccounts.RemoveAll(item => item.Uuid == target.Uuid);
        MicrosoftAccountStore.SaveAll(_microsoftAccounts, MicrosoftAccountStore.ListPathFor(_microsoftAccountPath));
        var wasCurrent = _microsoftAccount?.Uuid == target.Uuid;
        RefreshMicrosoftAccounts();
        if (!wasCurrent) return;
        MicrosoftAccountStore.Clear(_microsoftAccountPath);
        _microsoftAccount = null;
        var next = _microsoftAccounts.LastOrDefault();
        if (next is not null) ApplyMicrosoftAccount(next);
        else SelectOfflineAccount();
        AccountStatus = "已移除微软账号：" + target.Name;
    }

    /// <summary>删除指定第三方账号。</summary>
    public void DeleteAuthlibAccount(AuthlibAccount? account)
    {
        var target = account ?? _authlibAccount;
        if (target is null) return;
        _authlibAccounts.RemoveAll(item =>
            SameServer(item.Server, target.Server) &&
            string.Equals(item.Uuid, target.Uuid, StringComparison.OrdinalIgnoreCase));
        AuthlibAccountStore.Remove(target.Server, target.Uuid, _authlibAccountPath);
        var wasCurrent = IsAuthlibAccount && _authlibAccount is not null &&
                         SameServer(_authlibAccount.Server, target.Server) &&
                         string.Equals(_authlibAccount.Uuid, target.Uuid, StringComparison.OrdinalIgnoreCase);
        RefreshAuthlibAccounts();
        if (!wasCurrent) return;
        _authlibAccount = null;
        var next = _authlibAccounts.LastOrDefault();
        if (next is not null) ApplyAuthlibAccount(next);
        else SelectOfflineAccount();
        AccountStatus = "已移除第三方账号：" + target.Name;
    }

    private void RefreshOfflineRoles()
    {
        var names = _services.Settings.OfflineRoles;
        if (names.Count == 0)
        {
            // 首次启动只在内存里补一个默认角色，避免构造期产生设置写入副作用
            names.Add(string.IsNullOrWhiteSpace(_services.Settings.PlayerName) ? "Steve" : _services.Settings.PlayerName);
        }

        // 只在内容真的变化时改动集合：切换/改名时原地替换，
        // 避免 Clear+Add 重建所有列表项导致悬停态错位到别的行。
        if (OfflineRoles.SequenceEqual(names))
        {
            OnPropertyChanged(nameof(SelectedOfflineRole));
            return;
        }
        if (OfflineRoles.Count == names.Count)
        {
            for (var i = 0; i < names.Count; i++)
            {
                if (!string.Equals(OfflineRoles[i], names[i], StringComparison.Ordinal))
                    OfflineRoles[i] = names[i];
            }
        }
        else
        {
            OfflineRoles.Clear();
            foreach (var name in names) OfflineRoles.Add(name);
        }
        OnPropertyChanged(nameof(SelectedOfflineRole));
    }

    /// <summary>已登录过的微软账号（保留多个，可切换）。</summary>
    public ObservableCollection<MicrosoftAccount> MicrosoftAccounts { get; } = [];

    public bool ShowMicrosoftAccountPicker => IsMicrosoftAccount && MicrosoftAccounts.Count > 1;

    /// <summary>浮层里切换微软账号。</summary>
    public MicrosoftAccount? SelectedMicrosoftAccount
    {
        get => _microsoftAccount;
        set
        {
            if (value is null || _applyingMicrosoftAccount) return;
            if (_microsoftAccount is not null && _microsoftAccount.Uuid == value.Uuid) return;
            ApplyMicrosoftAccount(value);
        }
    }

    private readonly List<MicrosoftAccount> _microsoftAccounts = [];
    public bool HasAuthlibAccounts => AuthlibAccounts.Count > 0;

    /// <summary>第三方账号下拉只在“第三方”登录方式下出现，避免正版/离线时仍显示外置账号。</summary>
    public bool ShowAuthlibAccountPicker => IsAuthlibAccount && HasAuthlibAccounts;

    /// <summary>角色浮层底部动作文案：按登录方式区分。</summary>
    public string AddAccountLabel => IsMicrosoftAccount
        ? "登录新账号"
        : IsAuthlibAccount ? "添加新账号" : "添加新角色";
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
    public string LaunchButtonText =>
        WMCL.App.ViewModels.Launching.LaunchButtonText.Title(IsVersionLoading, HasInstalledVersions);
    public string LaunchButtonSubtitle =>
        WMCL.App.ViewModels.Launching.LaunchButtonText.Subtitle(IsVersionLoading, HasInstalledVersions, SelectedVersionTitle);
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
            UpdateCommands();
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
        UpdateCommands();
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
    private bool _isCheckingGameFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameFileCheckError))]
    private string _gameFileCheckError = "";

    public bool HasGameFileCheckError => !string.IsNullOrEmpty(GameFileCheckError);
    public string GameFileCheckOutcome { get; private set; } = "";

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

    [ObservableProperty]
    private string _logExportStatus = "";

    /// <summary>首页直接导出诊断包，省得再去设置里找。</summary>
    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        var suggested = "WMCL-logs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip";
        string? path;
        try
        {
            path = await DialogService.PickLogBundlePathAsync(suggested);
        }
        catch (Exception exception)
        {
            LogExportStatus = "无法打开保存对话框：" + exception.Message;
            return;
        }
        if (string.IsNullOrWhiteSpace(path))
        {
            LogExportStatus = "已取消导出";
            return;
        }
        LogExportStatus = "正在导出诊断日志…";
        try
        {
            var result = await LogBundleExporter.ExportAsync(_services, path);
            LogExportStatus = $"已导出 {result.FileCount} 个文件：{result.Path}";
        }
        catch (Exception exception)
        {
            LogExportStatus = "导出失败：" + exception.Message;
        }
    }

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

    [ObservableProperty]
    private bool _microsoftLoginExpired;

    /// <summary>启动后校验已保存的微软账号：令牌失效时提示重新登录，而不是静默失败。</summary>
    internal async Task ValidateMicrosoftAccountAsync()
    {
        if (!IsMicrosoftAccount || _microsoftAccount is null) return;
        var clientId = MicrosoftLoginViewModel.ResolveClientId();
        if (string.IsNullOrWhiteSpace(clientId)) return;
        try
        {
            var refreshed = await _auth.RefreshAsync(
                clientId, _microsoftAccount.RefreshToken, saveAccount: false);
            if (IsMicrosoftAccount) ApplyMicrosoftAccount(refreshed);
            MicrosoftLoginExpired = false;
        }
        catch (Exception exception)
        {
            // 只有凭据本身失效才提示重新登录；网络问题不应常驻这个按钮。
            MicrosoftLoginExpired = WMCL.App.ViewModels.Accounts.AccountStatusText.IsCredentialExpired(exception);
            AccountStatus = WMCL.App.ViewModels.Accounts.AccountStatusText.ValidateFailure(exception);
        }
    }


    public bool HasAccountSkin => AccountSkin is not null;
    public string? AccountSkinPath => _accountSkinPath;
    public bool HasAccountSkinFile =>
        !string.IsNullOrWhiteSpace(_accountSkinPath) && File.Exists(_accountSkinPath);

    /// <summary>按账号缓存皮肤位图，切换账号时立即显示，避免默认头像闪一下。</summary>
    private readonly Dictionary<string, Bitmap> _skinCache = [];

    private string? _skinCacheKey;

    /// <summary>切换账号时先显示缓存头像，避免默认头像闪一下。</summary>
    private void ApplyCachedSkin(string cacheKey)
    {
        if (_skinCache.TryGetValue(cacheKey, out var cached))
        {
            _skinCacheKey = cacheKey;
            AccountSkin = cached;
            return;
        }
        _skinCacheKey = null;
        SetAccountSkin(null);
    }

    /// <summary>离线账号：从本地皮肤读取头像，换肤后调用即可立即更新。</summary>
    public void RefreshOfflineSkin()
    {
        _accountSkinRequest++;
        var path = OfflineSkinStore.PathFor(PlayerName);
        if (!File.Exists(path))
        {
            SetAccountSkin(null);
            return;
        }
        try
        {
            var key = "offline:" + PlayerName;
            var bitmap = new Bitmap(path);
            _skinCache[key] = bitmap;
            _skinCacheKey = key;
            AccountSkin = bitmap;
            CurrentSkinVariant = SkinModelDetector.Detect(bitmap);
        }
        catch (Exception ex)
        {
            AccountStatus = "读取离线皮肤失败：" + ex.Message;
            SetAccountSkin(null);
        }
    }

    private void SetAccountSkin(string? path, string? cacheKey = null)
    {
        if (!string.IsNullOrWhiteSpace(cacheKey) && path is not null)
        {
            _skinCacheKey = cacheKey;
        }
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

    private void RefreshMicrosoftAccounts()
    {
        MicrosoftAccounts.Clear();
        foreach (var account in _microsoftAccounts) MicrosoftAccounts.Add(account);
        OnPropertyChanged(nameof(ShowMicrosoftAccountPicker));
    }

    private void RefreshAuthlibAccounts()
    {
        AuthlibAccounts.Clear();
        foreach (var account in _authlibAccounts) AuthlibAccounts.Add(account);
        OnPropertyChanged(nameof(HasAuthlibAccounts));
        OnPropertyChanged(nameof(ShowAuthlibAccountPicker));
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

    private string? _renamingFrom;

    partial void OnPlayerNameChanged(string value)
    {
        OnPropertyChanged(nameof(PlayerInitial));
        if (!IsOfflineAccount || string.IsNullOrWhiteSpace(value)) return;
        _services.Settings.PlayerName = value;
        var roles = _services.Settings.OfflineRoles;
        if (!roles.Contains(value)) roles.Add(value);
        _services.SaveSettings();
        RefreshOfflineRoles();
        OnPropertyChanged(nameof(OfflineRoleName));
    }

    partial void OnIsMicrosoftAccountChanged(bool value)
    {
        OnPropertyChanged(nameof(IsOfflineAccount));
        OnPropertyChanged(nameof(CanUploadMicrosoftSkin));
        OnPropertyChanged(nameof(ShowAuthlibAccountPicker));
    }

    partial void OnIsAuthlibAccountChanged(bool value)
    {
        OnPropertyChanged(nameof(IsOfflineAccount));
        OnPropertyChanged(nameof(ShowAuthlibAccountPicker));
    }

    private static readonly MicrosoftCape NoCape = new("", "无披风", null, false);
    public ObservableCollection<MicrosoftCape> Capes { get; } = [];
    public ObservableCollection<MinecraftNewsItem> HomeNews { get; } = [];

    [ObservableProperty]
    private bool _isHomeNewsLoading;

    [ObservableProperty]
    private string _homeNewsStatus = "";

    /// <summary>按可用宽度决定展示条数（1 列 4 条、2 列 6 条，最多 6 条），避免出现空位。</summary>
    public ObservableCollection<MinecraftNewsItem> HomeNewsDisplay { get; } = [];

    private int _homeNewsCapacity = 4;

    public void SetHomeNewsCapacity(int capacity)
    {
        capacity = Math.Clamp(capacity, 2, 6);
        if (_homeNewsCapacity == capacity) return;
        _homeNewsCapacity = capacity;
        ApplyHomeNewsDisplay();
    }

    /// <summary>只做增量增删，保持已有条目实例不变；否则缩放窗口时列表重建会让新闻图片反复重新加载。</summary>
    internal void ApplyHomeNewsDisplay()
    {
        var desired = HomeNews.Take(_homeNewsCapacity).ToArray();
        if (HomeNewsDisplay.SequenceEqual(desired))
        {
            OnPropertyChanged(nameof(HasHomeNews));
            return;
        }
        while (HomeNewsDisplay.Count > desired.Length)
            HomeNewsDisplay.RemoveAt(HomeNewsDisplay.Count - 1);
        for (var index = 0; index < desired.Length; index++)
        {
            if (index >= HomeNewsDisplay.Count) HomeNewsDisplay.Add(desired[index]);
            else if (!ReferenceEquals(HomeNewsDisplay[index], desired[index]))
                HomeNewsDisplay[index] = desired[index];
        }
        OnPropertyChanged(nameof(HasHomeNews));
        if (HomeNews.Count > 0) HomeNewsStatus = $"已加载 {HomeNewsDisplay.Count} 条新闻";
    }

    public bool HasHomeNews => HomeNewsDisplay.Count > 0;

    /// <summary>启动页的 Minecraft 新闻卡片可以在“功能隐藏”里关掉。</summary>
    public bool IsHomeNewsVisible => _services.Features.IsVisible(FeatureCatalog.HomeNews);
    private bool _homeNewsLoaded;

    private bool _microsoftValidated;

    private void QueueMicrosoftValidation()
    {
        if (_microsoftValidated || !IsMicrosoftAccount || _microsoftAccount is null) return;
        _microsoftValidated = true;
        _ = ValidateMicrosoftAccountAsync();
    }

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
            foreach (var item in items.Take(6)) HomeNews.Add(item);
            ApplyHomeNewsDisplay();
            if (HomeNews.Count == 0) HomeNewsStatus = "没有可显示的新闻";
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
            OnPropertyChanged(nameof(AddAccountLabel));
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
            var generation = _skinAccountGeneration;
            if (await LoadMicrosoftSkinAsync(microsoft) && IsCurrentSkinAccount(microsoft, generation))
                AccountStatus = "皮肤已刷新";
        }
        else if (IsAuthlibAccount && _authlibAccount is { } authlib)
        {
            await LoadSkinAsync(authlib);
            AccountStatus = "皮肤已刷新";
        }
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

    private string? ResolveSavedVersionId()
    {
        var root = _services.Settings.GameDirectory;
        if (_services.Settings.LastVersionByFolder.TryGetValue(root, out var byFolder) &&
            !string.IsNullOrWhiteSpace(byFolder))
        {
            return byFolder;
        }
        return string.IsNullOrWhiteSpace(_services.Settings.LastVersionId)
            ? null
            : _services.Settings.LastVersionId;
    }

    private void SaveSelectedVersion(string id)
    {
        var settings = _services.Settings;
        if (settings.LastVersionId == id &&
            settings.LastVersionByFolder.TryGetValue(settings.GameDirectory, out var existing) &&
            existing == id)
        {
            return;
        }
        settings.LastVersionId = id;
        settings.LastVersionByFolder[settings.GameDirectory] = id;
        _services.SaveSettings();
    }

    private void UpdateCommands()
    {
        CanLaunch = !IsBusy && !IsVersionLoading && (IsSelectedVersionUsable || !HasInstalledVersions);
    }

    private void ApplyFilter(string? selectedId = null)
    {
        selectedId ??= SelectedVersion?.Id;
        // 首次加载时恢复上次选中的版本
        if (string.IsNullOrWhiteSpace(selectedId)) selectedId = ResolveSavedVersionId();
        Versions.Clear();
        foreach (var item in _all)
            Versions.Add(item);
        InstalledVersions.Clear();
        foreach (var item in WMCL.App.ViewModels.Launching.VersionSelection.Installed(_all))
            InstalledVersions.Add(item);
        OnPropertyChanged(nameof(HasInstalledVersions));
        OnPropertyChanged(nameof(LaunchButtonText));
        OnPropertyChanged(nameof(LaunchButtonSubtitle));

        SelectedVersion = WMCL.App.ViewModels.Launching.VersionSelection.Pick(InstalledVersions, selectedId);

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
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                Progress = (update.Percent ?? 0) * 100;
                ProgressText = FormatProgress(update);
            });

            using var operation = _services.GameOperations.Acquire(root, "安装游戏");
            var result = await _services.Installer.InstallAsync(
                target.Id,
                new InstallOptions
                {
                    Mirror = _services.Mirror,
                    MaxConcurrency = Math.Clamp(_services.Settings.DownloadThreads, 1, 64)
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


    internal async Task<bool> EnsurePurchaseNoticeAsync(Func<CancellationToken, Task<bool>> confirm,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_services.Settings.MinecraftPurchaseNoticeAcknowledged || StartupOptions.IsAutomation ||
            !(IsOfflineAccount || IsAuthlibAccount)) return true;
        if (!await confirm(ct)) return false;
        ct.ThrowIfCancellationRequested();
        _services.Settings.MinecraftPurchaseNoticeAcknowledged = true;
        try
        {
            _services.SaveSettings();
        }
        catch
        {
            _services.Settings.MinecraftPurchaseNoticeAcknowledged = false;
            throw;
        }
        return true;
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
