using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.App.Services;
using PCL.Launch.Auth;

namespace PCL.App.ViewModels;

public partial class MicrosoftLoginViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MicrosoftAuthService _auth = new();
    private CancellationTokenSource? _cts;

    public MicrosoftLoginViewModel(AppServices services)
    {
        _services = services;
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
    private bool _isBusy;

    [ObservableProperty]
    private MicrosoftAccount? _account;

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsBusy) return;

        var clientId = _services.Settings.MicrosoftClientId?.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            Status = "未配置微软 Client ID，请先在“设置”页填写 Azure 公共客户端应用 ID。";
            return;
        }

        IsBusy = true;
        Status = "正在请求设备码…";
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<MicrosoftAuthProgress>(update =>
            {
                Status = update.Stage;
                if (!string.IsNullOrEmpty(update.UserCode)) UserCode = update.UserCode;
                if (!string.IsNullOrEmpty(update.VerificationUri))
                    VerificationUri = update.VerificationUri;
                if (!string.IsNullOrEmpty(update.Message)) Message = update.Message;
            });

            Account = await _auth.LoginAsync(clientId, progress, _cts.Token);
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

    [RelayCommand]
    private void OpenVerification()
    {
        if (string.IsNullOrWhiteSpace(VerificationUri)) return;
        try
        {
            var startInfo = OperatingSystem.IsWindows()
                ? new ProcessStartInfo(VerificationUri) { UseShellExecute = true }
                : new ProcessStartInfo("xdg-open", VerificationUri) { UseShellExecute = false };
            Process.Start(startInfo);
        }
        catch
        {
            // ignore
        }
    }
}
