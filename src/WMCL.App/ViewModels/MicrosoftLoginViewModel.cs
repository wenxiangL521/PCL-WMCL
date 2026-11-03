using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Launch.Auth;

namespace WMCL.App.ViewModels;

public partial class MicrosoftLoginViewModel : ObservableObject, IDisposable
{
    private sealed class LoginSession
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public bool Active { get; set; } = true;
        public bool AutomaticActionsStarted { get; set; }
        public int PendingActions { get; set; }
        public bool LoginFinished { get; set; }

        public void DisposeWhenFinished()
        {
            if (LoginFinished && PendingActions == 0) Cancellation.Dispose();
        }
    }

    private readonly AppServices _services;
    private readonly Func<string, IProgress<MicrosoftAuthProgress>, CancellationToken, Task<MicrosoftAccount>> _login;
    private readonly Func<string, CancellationToken, Task> _openBrowser;
    private readonly bool _clipboardInjected;
    private Func<string, CancellationToken, Task> _copyCode;
    private LoginSession? _session;
    private bool _started;
    private bool _disposed;

    public MicrosoftLoginViewModel(AppServices services)
        : this(services, (clientId, progress, ct) => new MicrosoftAuthService().LoginAsync(clientId, progress, ct))
    {
    }

    internal MicrosoftLoginViewModel(AppServices services,
        Func<string, IProgress<MicrosoftAuthProgress>, CancellationToken, Task<MicrosoftAccount>> login,
        Func<string, CancellationToken, Task>? copyCode = null,
        Func<string, CancellationToken, Task>? openBrowser = null)
    {
        _services = services;
        _login = login;
        _clipboardInjected = copyCode is not null;
        _copyCode = copyCode ?? ((_, _) => Task.FromException(new InvalidOperationException("剪贴板不可用")));
        _openBrowser = openBrowser ?? ((url, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            ExternalLauncher.Open(url);
            return Task.CompletedTask;
        });
    }

    public event EventHandler? LoginSucceeded;

    [ObservableProperty]
    private string _status = "准备登录";

    [ObservableProperty]
    private string _userCode = "";

    [ObservableProperty]
    private string _verificationUri = "";

    [ObservableProperty]
    private string _message = "";

    [ObservableProperty]
    private string _clipboardStatus = "";

    [ObservableProperty]
    private string _browserStatus = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canRetry;

    [ObservableProperty]
    private MicrosoftAccount? _account;

    public bool HasVerificationUri => !string.IsNullOrWhiteSpace(VerificationUri);
    public bool HasUserCode => !string.IsNullOrWhiteSpace(UserCode);
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool ShowConfigurationHint =>
        !IsBusy && !HasVerificationUri && string.IsNullOrWhiteSpace(Message);
    internal Task AutomaticActionsTask { get; private set; } = Task.CompletedTask;

    partial void OnVerificationUriChanged(string value)
    {
        OnPropertyChanged(nameof(HasVerificationUri));
        OnPropertyChanged(nameof(ShowConfigurationHint));
    }

    partial void OnUserCodeChanged(string value) => OnPropertyChanged(nameof(HasUserCode));

    partial void OnMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(ShowConfigurationHint));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(ShowConfigurationHint));

    internal void AttachClipboardAction(Func<string, CancellationToken, Task> copyCode)
    {
        if (!_disposed && !_clipboardInjected) _copyCode = copyCode;
    }

    internal Task EnsureStartedAsync()
    {
        if (_started || _disposed) return Task.CompletedTask;
        return StartCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (_disposed || IsBusy || Account is not null) return;
        _started = true;
        UserCode = "";
        VerificationUri = "";
        Message = "";
        ClipboardStatus = "";
        BrowserStatus = "";
        CanRetry = false;
        AutomaticActionsTask = Task.CompletedTask;
        var clientId = ResolveClientId();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            Status = "未配置微软 Client ID：请在“设置”页填写，或用 secrets.local.props 构建内置值。";
            return;
        }

        var session = new LoginSession();
        _session = session;
        IsBusy = true;
        Status = "正在请求设备码…";
        try
        {
            var progress = new Progress<MicrosoftAuthProgress>(update =>
            {
                if (!IsCurrent(session)) return;
                Status = update.Stage;
                if (!string.IsNullOrEmpty(update.UserCode)) UserCode = update.UserCode;
                if (!string.IsNullOrEmpty(update.VerificationUri))
                {
                    if (TryGetOfficialVerificationUri(update.VerificationUri, out var official))
                        VerificationUri = official;
                    else
                    {
                        VerificationUri = "";
                        BrowserStatus = "微软返回的验证地址不在官方允许列表中，已阻止打开。";
                    }
                }
                if (!string.IsNullOrEmpty(update.Message)) Message = update.Message;
                if (!session.AutomaticActionsStarted && HasUserCode && HasVerificationUri)
                {
                    session.AutomaticActionsStarted = true;
                    AutomaticActionsTask = RunAutomaticActionsAsync(session, UserCode, VerificationUri);
                }
            });
            var account = await _login(clientId, progress, session.Cancellation.Token);
            if (!IsCurrent(session)) return;
            session.Active = false;
            session.Cancellation.Cancel();
            Account = account;
            Status = $"登录成功：{account.Name}";
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_session, session) && !_disposed)
            {
                Status = "已取消登录";
                CanRetry = true;
            }
        }
        catch (Exception ex)
        {
            if (IsCurrent(session))
            {
                Status = "登录失败：" + ex.Message;
                CanRetry = true;
            }
        }
        finally
        {
            session.Active = false;
            session.Cancellation.Cancel();
            if (ReferenceEquals(_session, session))
            {
                _session = null;
                if (!_disposed) IsBusy = false;
            }
            session.LoginFinished = true;
            session.DisposeWhenFinished();
        }
    }

    /// <summary>对照 PCL2：Client ID 由构建期内置，用户不可自行设置。</summary>
    internal static string ResolveClientId() => WMCL.Core.LauncherSecrets.MicrosoftClientId;

    private bool IsCurrent(LoginSession session) => !_disposed && session.Active &&
        ReferenceEquals(_session, session) && !session.Cancellation.IsCancellationRequested;

    private async Task RunAutomaticActionsAsync(LoginSession session, string code, string url)
    {
        await CopyAsync(session, code);
        if (IsCurrent(session)) await OpenAsync(session, url);
    }

    private async Task CopyAsync(LoginSession session, string code)
    {
        if (!IsCurrent(session)) return;
        session.PendingActions++;
        try
        {
            await _copyCode(code, session.Cancellation.Token);
            if (IsCurrent(session)) ClipboardStatus = "设备码已复制，请在官方网页粘贴。";
        }
        catch (Exception)
        {
            if (IsCurrent(session)) ClipboardStatus = "自动复制失败，请点击“复制设备码”重试或手动选择设备码。";
        }
        finally
        {
            session.PendingActions--;
            session.DisposeWhenFinished();
        }
    }

    private async Task OpenAsync(LoginSession session, string url)
    {
        if (!IsCurrent(session)) return;
        if (!TryGetOfficialVerificationUri(url, out var official))
        {
            BrowserStatus = "验证地址不在官方允许列表中，已阻止打开。";
            return;
        }
        session.PendingActions++;
        try
        {
            await _openBrowser(official, session.Cancellation.Token);
            if (IsCurrent(session)) BrowserStatus = "已打开微软官方登录页。";
        }
        catch (Exception)
        {
            if (IsCurrent(session)) BrowserStatus = "打开浏览器失败，请点击“打开登录页”重试或手动访问上方地址。";
        }
        finally
        {
            session.PendingActions--;
            session.DisposeWhenFinished();
        }
    }

    internal static bool TryGetOfficialVerificationUri(string value, out string official)
    {
        official = "";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            (uri.IdnHost != "microsoft.com" && uri.IdnHost != "www.microsoft.com") ||
            (uri.AbsolutePath != "/link" && uri.AbsolutePath != "/devicelogin")) return false;
        official = "https://" + uri.IdnHost + uri.AbsolutePath;
        return true;
    }

    [RelayCommand]
    private async Task CopyUserCodeAsync()
    {
        if (_session is { } session && HasUserCode) await CopyAsync(session, UserCode);
    }

    [RelayCommand]
    private async Task OpenVerificationAsync()
    {
        if (_session is { } session && HasVerificationUri) await OpenAsync(session, VerificationUri);
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_disposed || _session is not { } session || !session.Active) return;
        session.Active = false;
        session.Cancellation.Cancel();
        Status = "已取消登录";
        CanRetry = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_session is { } session)
        {
            session.Active = false;
            session.Cancellation.Cancel();
        }
    }
}
