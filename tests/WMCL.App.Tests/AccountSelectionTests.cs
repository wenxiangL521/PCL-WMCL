using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Launch.Auth;

namespace WMCL.App.Tests;

public sealed class AccountSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wmcl-account-selection-" + Guid.NewGuid().ToString("N"));
    private string MicrosoftPath => Path.Combine(_root, "accounts", "microsoft.json");
    private string AuthlibPath => Path.Combine(_root, "accounts", "authlib.json");

    [AvaloniaFact]
    public void FirstAuthlibLoginRemainsVisibleWithoutStoredAccounts()
    {
        var model = Model();
        var view = new LaunchView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.HasAuthlibAccounts);
            Assert.True(view.FindControl<RadioButton>("AuthlibAccountRadio")!.IsEffectivelyVisible);
            Assert.False(model.TryUseSavedAuthlibAccount());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FirstAuthlibClickOpensTheLoginDialogWithTheInstanceServerAndCancelKeepsOffline()
    {
        var model = Model();
        var directory = Path.Combine(_root, "versions", "Required");
        Directory.CreateDirectory(directory);
        WMCL.Minecraft.InstanceSettingsStore.Save(directory,
            new WMCL.Minecraft.InstanceSettings { AuthServer = "https://required.invalid/yggdrasil" });
        model.SelectedVersion = new VersionItem { Id = "Required", IsInstalled = true };
        var view = new LaunchView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            var radio = view.FindControl<RadioButton>("AuthlibAccountRadio")!;
            radio.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await PumpAsync();
            var login = Assert.IsType<AuthlibLoginWindow>(Assert.Single(window.OwnedWindows));
            Assert.Equal("https://required.invalid/yggdrasil", Assert.IsType<AuthlibLoginViewModel>(login.DataContext).Server);
            login.Close();
            await PumpAsync();
            Assert.True(model.IsOfflineAccount);
            Assert.Empty(model.AuthlibAccounts);
            Assert.Equal(true, view.FindControl<RadioButton>("OfflineAccountRadio")!.IsChecked);
        }
        finally
        {
            foreach (var child in window.OwnedWindows.ToArray()) child.Close();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StandaloneSkinWindowShowsMicrosoftActions()
    {
        var model = Model();
        model.SetMicrosoftAccount(Microsoft());
        var window = new MsSkinWindow(model) { Width = 760, Height = 560 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<Button>("MsUploadButton")!.IsEffectivelyVisible);
            Assert.True(window.FindControl<ComboBox>("MsCapeBox")!.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData("https://littleskin.cn/api/yggdrasil", "https://littleskin.cn/user/closet")]
    [InlineData("https://example.com/auth/register", "https://example.com/user/closet")]
    [InlineData("https://example.com/", "https://example.com/user/closet")]
    [InlineData("", "")]
    public void AuthlibSkinClosetUsesRegisterAddress(string server, string expected)
    {
        Assert.Equal(expected, LaunchViewModel.BuildAuthlibSkinClosetUrl(server));
    }

    [AvaloniaFact]
    public void AuthlibAccountShowsSkinClosetButton()
    {
        var model = Model();
        var view = new LaunchView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 600 };
        window.Show();
        try
        {
            model.SetAuthlibAccount(Authlib("https://littleskin.cn/api/yggdrasil", "SkinUser"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<Button>("SkinClosetButton")!.IsEffectivelyVisible);
            Assert.Equal("https://littleskin.cn/user/closet", model.AuthlibSkinClosetUrl);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MicrosoftCapeListCanApplyNoneOrAnActiveCape()
    {
        var cape = new MicrosoftCape("cape-2", "Vanilla", "https://example.com/2.png", true);
        string? changed = null;
        var model = Model(
            getMicrosoftCapes: _ => Task.FromResult<IReadOnlyList<MicrosoftCape>>([cape]),
            setMicrosoftCape: (_, id) => { changed = id; return Task.CompletedTask; });
        var view = new LaunchView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            model.SetMicrosoftAccount(Microsoft());
            await model.LoadCapesAsync();
            model.ToggleSkinEditorCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<ComboBox>("CapeBox")!.IsEffectivelyVisible);
            Assert.True(view.FindControl<Button>("CdkeyButton")!.IsEffectivelyVisible);
        }
        finally { window.Close(); }
        Assert.True(model.HasCapes);
        Assert.Equal(2, model.Capes.Count);
        Assert.Equal("cape-2", model.SelectedCape?.Id);

        model.SelectedCape = model.Capes[0];
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)model.ChangeCapeCommand).ExecuteAsync(null);
        Assert.Null(changed);
        Assert.Equal("披风已更新", model.CapeStatus);

        model.SelectedCape = model.Capes.Single(item => item.Id == "cape-2");
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)model.ChangeCapeCommand).ExecuteAsync(null);
        Assert.Equal("cape-2", changed);
    }

    [AvaloniaFact]
    public async Task FirstMicrosoftClickCancelKeepsTheOriginalOfflineRadioState()
    {
        var services = Services();
        var model = Model(services);
        var view = new LaunchView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            Assert.True(model.IsOfflineAccount);
            Assert.True(string.IsNullOrWhiteSpace(services.Settings.MicrosoftClientId));
            var radio = view.FindControl<RadioButton>("OnlineAccountRadio")!;
            radio.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await PumpAsync();
            var login = Assert.IsType<MicrosoftLoginWindow>(Assert.Single(window.OwnedWindows));
            login.Close();
            await PumpAsync();
            Assert.True(model.IsOfflineAccount);
            Assert.False(model.IsMicrosoftAccount);
            Assert.True(services.Settings.AccountType is "" or "offline");
            Assert.Equal(true, view.FindControl<RadioButton>("OfflineAccountRadio")!.IsChecked);
            Assert.Null(MicrosoftAccountStore.Load(MicrosoftPath));
        }
        finally
        {
            foreach (var child in window.OwnedWindows.ToArray()) child.Close();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AuthlibRefreshSkinUsesTheSameLoaderAndKeepsTheFilePath()
    {
        Directory.CreateDirectory(_root);
        var png = Path.Combine(_root, "authlib-skin.png");
        await File.WriteAllBytesAsync(png, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        var calls = 0;
        var model = Model(downloadAuthlibSkin: _ =>
        {
            calls++;
            return Task.FromResult<string?>(png);
        });
        model.SetAuthlibAccount(Authlib("https://first.invalid", "First"));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!model.HasAccountSkin && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        var before = calls;
        await model.RefreshSkinCommand.ExecuteAsync(null);
        Assert.Equal(before + 1, calls);
        Assert.Equal("皮肤已刷新", model.AccountStatus);
        Assert.Equal(png, model.AccountSkinPath);
        Assert.True(model.HasAccountSkinFile);
    }

    [AvaloniaFact]
    public async Task LoadedSkinPathIsAvailableForSavingAndClearsWithOfflineSwitch()
    {
        Directory.CreateDirectory(_root);
        var png = Path.Combine(_root, "skin.png");
        await File.WriteAllBytesAsync(png, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        var model = Model(downloadMicrosoftSkin: _ => Task.FromResult<string?>(png));
        model.SetMicrosoftAccount(Microsoft());
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!model.HasAccountSkin && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(model.HasAccountSkin);
        Assert.Equal(png, model.AccountSkinPath);
        Assert.True(model.HasAccountSkinFile);
        model.SelectOfflineAccountCommand.Execute(null);
        Assert.Null(model.AccountSkinPath);
        Assert.False(model.HasAccountSkinFile);
    }

    [AvaloniaFact]
    public async Task RefreshSkinReloadsMicrosoftSkinAndReportsStatus()
    {
        var calls = 0;
        var model = Model(downloadMicrosoftSkin: _ =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });
        model.SetMicrosoftAccount(Microsoft());
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        var before = calls;
        await model.RefreshSkinCommand.ExecuteAsync(null);
        Assert.Equal(before + 1, calls);
        Assert.Equal("皮肤已刷新", model.AccountStatus);
    }

    [AvaloniaFact]
    public void SkinEditorStartsCollapsedAndResetsWhenLeavingMicrosoft()
    {
        var model = Model();
        model.SetMicrosoftAccount(Microsoft());
        var view = new LaunchView { DataContext = model };
        var window = new Window { Content = view, Width = 850, Height = 500 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var panel = view.FindControl<Border>("SkinEditorPanel")!;
            Assert.False(model.IsSkinEditorVisible);
            Assert.False(panel.IsVisible);
            Assert.Equal("修改皮肤", model.SkinEditorButtonText);

            model.ToggleSkinEditorCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsSkinEditorVisible);
            Assert.True(panel.IsVisible);
            Assert.Equal("收起皮肤", model.SkinEditorButtonText);

            model.SelectOfflineAccountCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.IsSkinEditorVisible);
            Assert.False(panel.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OfflineSelectionPreservesMicrosoftAndAllAuthlibAccountsAcrossRestarts()
    {
        var microsoft = Microsoft();
        MicrosoftAccountStore.Save(microsoft, MicrosoftPath);
        AuthlibAccountStore.SaveAccount(Authlib("https://first.invalid", "First"), AuthlibPath);
        AuthlibAccountStore.SaveAccount(Authlib("https://second.invalid", "Second"), AuthlibPath);
        var services = Services();
        var model = Model(services);
        Assert.True(model.IsMicrosoftAccount);
        model.SelectOfflineAccountCommand.Execute(null);
        Assert.True(model.IsOfflineAccount);
        Assert.False(model.HasAccountSkin);
        Assert.Equal("OfflinePlayer", model.PlayerName);
        Assert.Equal(microsoft, MicrosoftAccountStore.Load(MicrosoftPath));
        Assert.Equal(2, AuthlibAccountStore.LoadAll(AuthlibPath).Count);
        Assert.Equal("offline", services.Settings.AccountType);
        var restored = Model(services);
        Assert.True(restored.IsOfflineAccount);
        Assert.True(restored.TryUseSavedMicrosoftAccount());
        Assert.True(restored.IsMicrosoftAccount);
        Assert.Equal("MicrosoftPlayer", restored.PlayerName);
        Assert.True(restored.TryUseSavedAuthlibAccount());
        Assert.True(restored.IsAuthlibAccount);
        Assert.Equal("Second", restored.PlayerName);
        restored.SelectOfflineAccountCommand.Execute(null);
        Assert.True(restored.IsOfflineAccount);
        Assert.Equal(2, restored.AuthlibAccounts.Count);
        Assert.Equal(2, AuthlibAccountStore.LoadAll(AuthlibPath).Count);
        restored.SelectedAuthlibAccount = restored.AuthlibAccounts.First();
        Assert.True(restored.IsAuthlibAccount);
        Assert.Equal("First", restored.PlayerName);
    }

    [AvaloniaFact]
    public void ExplicitLogoutStillRemovesOnlyTheRequestedAccount()
    {
        MicrosoftAccountStore.Save(Microsoft(), MicrosoftPath);
        AuthlibAccountStore.SaveAccount(Authlib("https://first.invalid", "First"), AuthlibPath);
        AuthlibAccountStore.SaveAccount(Authlib("https://second.invalid", "Second"), AuthlibPath);
        var model = Model();
        model.LogoutMicrosoftCommand.Execute(null);
        Assert.Null(MicrosoftAccountStore.Load(MicrosoftPath));
        Assert.True(model.IsOfflineAccount);
        model.TryUseSavedAuthlibAccount();
        model.LogoutAuthlibCommand.Execute(null);
        Assert.Equal("First", Assert.Single(AuthlibAccountStore.LoadAll(AuthlibPath)).Name);
        Assert.True(model.IsAuthlibAccount);
        model.LogoutAuthlibCommand.Execute(null);
        Assert.Empty(AuthlibAccountStore.LoadAll(AuthlibPath));
        Assert.True(model.IsOfflineAccount);
    }

    [AvaloniaTheory]
    [InlineData("microsoft", false)]
    [InlineData("microsoft", true)]
    [InlineData("authlib", false)]
    [InlineData("authlib", true)]
    public async Task OldSkinSuccessOrFailureCannotClearOrReplaceTheNewAccountSkin(string type, bool fail)
    {
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string?> OldSkin()
        {
            try { return await pending.Task; }
            finally { completed.TrySetResult(); }
        }
        var model = Model(downloadMicrosoftSkin: _ => type == "microsoft" ? OldSkin() : Task.FromResult<string?>(null),
            downloadAuthlibSkin: account => type == "authlib" && account.Server == "https://first.invalid"
                ? OldSkin() : Task.FromResult<string?>(null));
        if (type == "microsoft") model.SetMicrosoftAccount(Microsoft());
        else model.SetAuthlibAccount(Authlib("https://first.invalid", "First"));
        model.SetAuthlibAccount(Authlib("https://second.invalid", "Second"));
        using var currentSkin = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        model.AccountSkin = currentSkin;
        if (fail) pending.SetException(new IOException("old skin failed"));
        else pending.SetResult(null);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await PumpAsync();
        Assert.True(model.IsAuthlibAccount);
        Assert.Equal("Second", model.PlayerName);
        Assert.Same(currentSkin, model.AccountSkin);
    }

    [AvaloniaFact]
    public async Task SwitchingOfflineInvalidatesSkinRequestsAndKeepsTheOfflineIdentity()
    {
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = Model(downloadMicrosoftSkin: _ => pending.Task);
        model.SetMicrosoftAccount(Microsoft());
        model.SelectOfflineAccountCommand.Execute(null);
        pending.SetResult(null);
        await PumpAsync();
        Assert.True(model.IsOfflineAccount);
        Assert.Equal("OfflinePlayer", model.PlayerName);
        Assert.Null(model.AccountSkin);
        Assert.True(model.TryUseSavedMicrosoftAccount());
        Assert.True(model.IsMicrosoftAccount);
    }

    private AppServices Services() => new(new AppSettings { GameDirectory = _root, PlayerName = "OfflinePlayer" }, saveSettings: _ => { });

    private LaunchViewModel Model(AppServices? services = null,
        Func<MicrosoftAccount, Task<string?>>? downloadMicrosoftSkin = null,
        Func<AuthlibAccount, Task<string?>>? downloadAuthlibSkin = null,
        Func<MicrosoftAccount, Task<IReadOnlyList<MicrosoftCape>>>? getMicrosoftCapes = null,
        Func<MicrosoftAccount, string?, Task>? setMicrosoftCape = null) =>
        new(services ?? Services(), null, null, Path.Combine(_root, "accounts"),
            downloadMicrosoftSkin ?? (_ => Task.FromResult<string?>(null)),
            downloadAuthlibSkin ?? (_ => Task.FromResult<string?>(null)),
            getMicrosoftCapes,
            setMicrosoftCape);

    private static MicrosoftAccount Microsoft() => new("MicrosoftPlayer", "same-uuid", "token", "refresh", DateTimeOffset.MaxValue);
    private static AuthlibAccount Authlib(string server, string name) => new(server, name, "same-uuid", "token", "client");

    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 5; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
