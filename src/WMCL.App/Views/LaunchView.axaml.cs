using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WMCL.App.Animation;
using WMCL.App.ViewModels;
using WMCL.Launch.Auth;

namespace WMCL.App.Views;

public partial class LaunchView : UserControl
{
    private MicrosoftLoginViewModel? _microsoftLogin;
    private AuthlibLoginViewModel? _authlibLogin;

    public LaunchView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => { LoadHomeNews(); ObserveAccount(); ObserveNewsCapacity(); };
        DataContextChanged += (_, _) => { LoadHomeNews(); ObserveAccount(); };

        LogBox.TextChanged += (_, _) =>
        {
            if (LogBox.Text is { Length: > 0 })
                LogBox.CaretIndex = LogBox.Text.Length;
        };
    }

    private void LoadHomeNews()
    {
        if (DataContext is ViewModels.LaunchViewModel viewModel)
            _ = viewModel.EnsureHomeNewsAsync();
    }

    private void OnHomeNewsClick(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (sender is not Control { DataContext: Services.MinecraftNewsItem item } ||
            !Services.MinecraftNewsService.IsSafeNewsLink(item.Url))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            if (DataContext is ViewModels.LaunchViewModel viewModel)
                viewModel.HomeNewsStatus = "打开新闻失败：" + ex.Message;
        }
    }

    private void OnOpenNewsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        _ = new MinecraftNewsWindow().ShowDialog(owner);
    }

    private void OnDeleteMicrosoftAccountClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.LaunchViewModel viewModel)
            viewModel.DeleteMicrosoftAccount(viewModel.SelectedMicrosoftAccount);
    }

    private void OnDeleteAuthlibAccountClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.LaunchViewModel viewModel)
            viewModel.DeleteAuthlibAccount(viewModel.SelectedAuthlibAccount);
    }

    private void OnDeleteOfflineRoleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.LaunchViewModel viewModel)
            viewModel.DeleteOfflineRole(viewModel.SelectedOfflineRole);
    }

    private bool _accountPopupClosing;

    private async void OnPlayerNameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AccountPopup.IsOpen && !_accountPopupClosing)
        {
            await CloseAccountPopupAsync();
            return;
        }
        OpenAccountPopup();
    }

    private void OpenAccountPopup()
    {
        _accountPopupClosing = false;
        AccountPopup.IsOpen = true;
        // 先以收起态呈现，等一帧后再切到展开态，触发过渡
        AccountPopupPanel.Classes.Set("open", false);
        Dispatcher.UIThread.Post(() => AccountPopupPanel.Classes.Set("open", true),
            DispatcherPriority.Render);
    }

    private async Task CloseAccountPopupAsync()
    {
        _accountPopupClosing = true;
        AccountPopupPanel.Classes.Set("open", false);
        await Task.Delay(150);
        AccountPopup.IsOpen = false;
        _accountPopupClosing = false;
    }

    private void OnAddAccountClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.LaunchViewModel viewModel) return;
        if (viewModel.IsMicrosoftAccount)
        {
            OpenMicrosoftLogin(forceRelogin: true);
            return;
        }
        if (viewModel.IsAuthlibAccount)
        {
            OnAuthlibAccountClick(this, e);
            return;
        }
        viewModel.CreateOfflineRole();
    }

    private void OnAvatarSkinMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.LaunchViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not Window owner) return;
        OpenSkinManager(viewModel, owner);
    }

    private void OnAvatarRefreshMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.LaunchViewModel viewModel) RefreshSkin(viewModel);
    }

    private void RefreshSkin(ViewModels.LaunchViewModel viewModel)
    {
        if (viewModel.IsMicrosoftAccount)
        {
            _ = viewModel.LoadCapesAsync();
            viewModel.RefreshSkinCommand.Execute(null);
            return;
        }
        if (viewModel.IsAuthlibAccount)
        {
            viewModel.RefreshSkinCommand.Execute(null);
            return;
        }
        viewModel.RefreshOfflineSkin();
        viewModel.AccountStatus = "已刷新离线皮肤";
    }

    private void OpenSkinManager(ViewModels.LaunchViewModel viewModel, Window owner)
    {
        if (viewModel.IsMicrosoftAccount)
        {
            var window = new MsSkinWindow(viewModel);
            _ = window.ShowDialog(owner);
            _ = viewModel.LoadCapesAsync();
            return;
        }
        if (viewModel.IsAuthlibAccount)
        {
            OnSkinClosetClick(this, new Avalonia.Interactivity.RoutedEventArgs());
            return;
        }
        var offline = new OfflineSkinWindow(viewModel.PlayerName);
        offline.Closed += (_, _) => viewModel.RefreshOfflineSkin();
        _ = offline.ShowDialog(owner);
    }

    private void OnSkinClosetClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.LaunchViewModel viewModel) return;
        var url = viewModel.AuthlibSkinClosetUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            viewModel.AccountStatus = "当前认证服务器没有可用的皮肤衣柜地址";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            viewModel.AccountStatus = "打开皮肤衣柜失败：" + ex.Message;
        }
    }

    private CancellationTokenSource? _accountReveal;
    private LaunchViewModel? _accountViewModel;

    private void ObserveNewsCapacity()
    {
        if (HomeNewsCard is null) return;
        HomeNewsCard.SizeChanged -= OnHomeNewsCardSizeChanged;
        HomeNewsCard.SizeChanged += OnHomeNewsCardSizeChanged;
        ApplyNewsCapacity();
    }

    private void OnHomeNewsCardSizeChanged(object? sender, SizeChangedEventArgs e) => ApplyNewsCapacity();

    /// <summary>一列显示 4 条、两列显示 6 条，避免多列时最后一行留空。</summary>
    private void ApplyNewsCapacity()
    {
        if (DataContext is not LaunchViewModel viewModel) return;
        var width = HomeNewsCard.Bounds.Width;
        if (width <= 0) return;
        viewModel.SetHomeNewsCapacity(width >= 700 ? 6 : 4);
    }

    private void ObserveAccount()
    {
        if (_accountViewModel is not null) _accountViewModel.PropertyChanged -= OnAccountPropertyChanged;
        _accountViewModel = DataContext as LaunchViewModel;
        if (_accountViewModel is not null) _accountViewModel.PropertyChanged += OnAccountPropertyChanged;
    }

    private void OnAccountPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(LaunchViewModel.IsMicrosoftAccount)
            or nameof(LaunchViewModel.IsAuthlibAccount))
            PlayAccountReveal();
    }

    private void PlayAccountReveal()
    {
        _accountReveal?.Cancel();
        _accountReveal?.Dispose();
        var cancellation = new CancellationTokenSource();
        _accountReveal = cancellation;
        _ = RevealAccountAsync(cancellation);
    }

    private async Task RevealAccountAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Animation.PclPageAnimation.RunAsync([AccountDetail], entering: true, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Animation.PclPageAnimation.Reset([AccountDetail]);
        }
        finally
        {
            if (ReferenceEquals(_accountReveal, cancellation))
            {
                _accountReveal = null;
                cancellation.Dispose();
            }
        }
    }

    private void OnOnlineAccountClick(object? sender, RoutedEventArgs e) =>
        OpenMicrosoftLogin(forceRelogin: false);

    /// <summary>令牌失效后的重新登录入口：即使当前已是微软账号也要重新走设备码流程。</summary>
    private void OnMicrosoftReloginClick(object? sender, RoutedEventArgs e) =>
        OpenMicrosoftLogin(forceRelogin: true);

    private void OpenMicrosoftLogin(bool forceRelogin)
    {
        if (DataContext is not LaunchViewModel viewModel) return;
        if (!forceRelogin)
        {
            if (viewModel.IsMicrosoftAccount) return;
            if (viewModel.TryUseSavedMicrosoftAccount()) return;
        }
        if (LoginHost.Content is not null) return;

        var loginViewModel = new MicrosoftLoginViewModel(viewModel.Services);
        var loginView = new MicrosoftLoginView { DataContext = loginViewModel };
        _microsoftLogin = loginViewModel;
        loginViewModel.LoginSucceeded += (_, _) => FinishMicrosoftLogin(viewModel, loginViewModel.Account);
        loginView.Cancelled += (_, _) => FinishMicrosoftLogin(viewModel, null);
        loginView.SettingsRequested += (_, _) =>
        {
            FinishMicrosoftLogin(viewModel, null);
            if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel main)
                main.NavigateTo("settings");
        };
        ShowLogin(loginView);
    }

    private void OnAuthlibAccountClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel viewModel) return;
        if (!viewModel.IsAuthlibAccount && viewModel.TryUseSavedAuthlibAccount()) return;
        if (LoginHost.Content is not null) return;

        var loginViewModel = new AuthlibLoginViewModel(viewModel.AuthlibService);
        if (!string.IsNullOrWhiteSpace(viewModel.RequiredAuthServer))
            loginViewModel.Server = viewModel.RequiredAuthServer;
        var loginView = new AuthlibLoginView { DataContext = loginViewModel };
        _authlibLogin = loginViewModel;
        loginViewModel.LoginSucceeded += (_, _) => FinishAuthlibLogin(viewModel, loginViewModel.Account);
        loginView.Cancelled += (_, _) => FinishAuthlibLogin(viewModel, null);
        ShowLogin(loginView);
    }

    private void FinishMicrosoftLogin(LaunchViewModel viewModel, MicrosoftAccount? account)
    {
        if (_microsoftLogin is null) return;
        if (account is not null) viewModel.SetMicrosoftAccount(account);
        else ResetAccountRadios(viewModel);

        _microsoftLogin.Dispose();
        _microsoftLogin = null;
        HideLogin();
    }

    private void FinishAuthlibLogin(LaunchViewModel viewModel, AuthlibAccount? account)
    {
        if (_authlibLogin is null) return;
        if (account is not null) viewModel.SetAuthlibAccount(account);
        else ResetAccountRadios(viewModel);

        _authlibLogin.Dispose();
        _authlibLogin = null;
        HideLogin();
    }

    private void ShowLogin(Control view) => PclLoginMotion.Show(LoginHost, AccountPanel, view);

    private void HideLogin() => PclLoginMotion.Hide(LoginHost, AccountPanel);

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _microsoftLogin?.Dispose();
        _authlibLogin?.Dispose();
        _microsoftLogin = null;
        _authlibLogin = null;
        if (LoginHost is not null)
        {
            LoginHost.Content = null;
            LoginHost.IsVisible = false;
            AccountPanel.IsVisible = true;
            AccountPanel.Opacity = 1;
        }
    }

    private void OnOfflineAccountClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LaunchViewModel viewModel) return;
        viewModel.SelectOfflineAccountCommand.Execute(null);
    }

    private void OnOpenVersionSelectClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel main)
            main.OpenVersionSelect();
    }

    private void ResetAccountRadios(LaunchViewModel viewModel)
    {
        OnlineAccountRadio.IsChecked = viewModel.IsMicrosoftAccount;
        OfflineAccountRadio.IsChecked = viewModel.IsOfflineAccount;
        AuthlibAccountRadio.IsChecked = viewModel.IsAuthlibAccount;
    }
}
