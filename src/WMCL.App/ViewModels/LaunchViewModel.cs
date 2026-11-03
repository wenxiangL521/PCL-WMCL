using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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
                bitmap.Save(stream);
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
    public bool HasAuthlibAccounts => AuthlibAccounts.Count > 0;

    /// <summary>第三方账号下拉只在“第三方”登录方式下出现，避免正版/离线时仍显示外置账号。</summary>
    public bool ShowAuthlibAccountPicker => IsAuthlibAccount && HasAuthlibAccounts;
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
            if (IsCredentialExpired(exception))
            {
                MicrosoftLoginExpired = true;
                AccountStatus = "微软登录已失效，请重新登录：" + exception.Message;
            }
            else
            {
                MicrosoftLoginExpired = false;
                AccountStatus = "暂时无法校验微软登录（网络问题），稍后会自动重试：" + exception.Message;
            }
        }
    }

    /// <summary>对照 PCL2：按响应文本判断是否需要重新登录。</summary>
    private static bool IsCredentialExpired(Exception exception)
    {
        var text = exception.ToString();
        return text.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
            || text.Contains("must sign in again", StringComparison.OrdinalIgnoreCase)
            || text.Contains("password expired", StringComparison.OrdinalIgnoreCase)
            || text.Contains("AADSTS70008", StringComparison.OrdinalIgnoreCase)
            || (text.Contains("refresh token", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("expired", StringComparison.OrdinalIgnoreCase));
    }

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

    partial void OnPlayerNameChanged(string value) =>
        OnPropertyChanged(nameof(PlayerInitial));

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
        MicrosoftLoginExpired = false;
        _skinAccountGeneration++;
        SkinUploadStatus = "";
        CurrentSkinVariant = "classic";
        _microsoftAccount = account;
        PlayerName = account.Name;
        IsAuthlibAccount = false;
        IsMicrosoftAccount = true;
        SaveAccountType("microsoft");
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
        MicrosoftLoginExpired = false;
        _skinAccountGeneration++;
        SkinUploadStatus = "";
        CurrentSkinVariant = "classic";
        _authlibAccount = account;
        PlayerName = account.Name;
        IsMicrosoftAccount = false;
        IsAuthlibAccount = true;
        SaveAccountType("authlib");
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

    private async Task<bool> LoadMicrosoftSkinAsync(MicrosoftAccount account)
    {
        var request = ++_accountSkinRequest;
        var generation = _skinAccountGeneration;
        try
        {
            var download = _downloadMicrosoftSkinWithInfo is null
                ? new MicrosoftSkinDownload(await _downloadMicrosoftSkin(account), null)
                : await _downloadMicrosoftSkinWithInfo(account);
            if (request != _accountSkinRequest || !IsCurrentSkinAccount(account, generation)) return false;
            SetAccountSkin(download.FilePath);
            if (download.Variant is not null)
                CurrentSkinVariant = string.Equals(download.Variant, "slim", StringComparison.OrdinalIgnoreCase) ? "slim" : "classic";
            return true;
        }
        catch (Exception ex)
        {
            if (request == _accountSkinRequest && IsCurrentSkinAccount(account, generation))
                AccountStatus = "刷新皮肤失败：" + ex.Message;
            return false;
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
        MicrosoftLoginExpired = false;
        _skinAccountGeneration++;
        SkinUploadStatus = "";
        CurrentSkinVariant = "classic";
        _accountSkinRequest++;
        IsMicrosoftAccount = false;
        IsAuthlibAccount = false;
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

    private void UpdateCommands()
    {
        CanLaunch = !IsBusy && !IsVersionLoading && (IsSelectedVersionUsable || !HasInstalledVersions);
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
            IsCheckingGameFiles = false;
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
