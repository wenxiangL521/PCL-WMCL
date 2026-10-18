using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.Launch.Auth;

namespace WMCL.App.ViewModels;

public partial class AuthlibLoginViewModel(AuthlibAuthService service) : ViewModelBase
{
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
        _cts = new CancellationTokenSource();
        Status = "正在登录…";
        try
        {
            Account = await service.AuthenticateAsync(Server, UserName, Password, _cts.Token);
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
}
