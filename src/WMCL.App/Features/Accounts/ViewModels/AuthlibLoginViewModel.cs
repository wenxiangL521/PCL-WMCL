using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.Launch.Auth;

namespace WMCL.App.Features.Accounts;

public partial class AuthlibLoginViewModel : ViewModelBase, IDisposable
{
    private readonly Func<string, string, string, CancellationToken, Task<AuthlibAccount>> _authenticate;

    public AuthlibLoginViewModel(AuthlibAuthService service)
        : this(service.AuthenticateAsync)
    {
    }

    internal AuthlibLoginViewModel(Func<string, string, string, CancellationToken, Task<AuthlibAccount>> authenticate)
    {
        _authenticate = authenticate;
    }

    public AuthlibAccount? Account { get; private set; }
    public event EventHandler? LoginSucceeded;

    [ObservableProperty]
    private string _server = "https://littleskin.cn/api/yggdrasil";

    [ObservableProperty]
    private string _nideServerId = "";

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _status = "支持 Yggdrasil / Authlib-Injector 认证服务器";

    [ObservableProperty]
    private bool _isBusy;

    private CancellationTokenSource? _cts;

    [RelayCommand]
    private void UseNide()
    {
        try
        {
            Server = AuthlibAuthService.NideServerUrl(NideServerId);
            Status = "已填入 Nide 认证地址，请使用统一通行证账号登录";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        // 宿主会在 LoginSucceeded 里立即 Dispose 本 VM，因此这里必须持有本地引用，
        // 不能回到字段上取值，否则 finally 会拿到 null 并让整个进程崩溃。
        var cancellation = new CancellationTokenSource();
        var previous = _cts;
        _cts = cancellation;
        previous?.Dispose();
        Status = "正在登录…";
        try
        {
            Account = await _authenticate(Server, UserName, Password, cancellation.Token);
            AuthlibAccountStore.Save(Account);
            Status = $"登录成功：{Account.Name}";
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            Status = "已取消登录";
        }
        catch (Exception ex)
        {
            Status = "登录失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            if (ReferenceEquals(_cts, cancellation)) _cts = null;
            cancellation.Dispose();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        Status = "正在取消…";
    }

    public void Dispose()
    {
        var cancellation = _cts;
        _cts = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
