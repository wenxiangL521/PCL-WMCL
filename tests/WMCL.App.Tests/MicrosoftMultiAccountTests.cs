using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Launch.Auth;

namespace WMCL.App.Tests;

public sealed class OfflineRoleTests
{
    private static LaunchViewModel Create(out AppServices services)
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "offline-role-" + Guid.NewGuid().ToString("N"));
        services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        return new LaunchViewModel(services, null, null, Path.Combine(root, "accounts"),
            _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));
    }

    [AvaloniaFact]
    public void AddsUniqueOfflineRoles()
    {
        var model = Create(out var services);
        model.SelectOfflineAccountCommand.Execute(null);
        var before = model.OfflineRoles.Count;
        model.CreateOfflineRole();
        model.CreateOfflineRole();
        model.CreateOfflineRole();
        Assert.True(model.OfflineRoles.Count >= before + 3);
        Assert.Equal(model.OfflineRoles.Count, model.OfflineRoles.Distinct().Count());
        Assert.Contains(model.PlayerName, model.OfflineRoles);
        Assert.Equal(model.PlayerName, services.Settings.PlayerName);
    }

    [AvaloniaFact]
    public void RenamingUpdatesRoleList()
    {
        var model = Create(out var services);
        model.SelectOfflineAccountCommand.Execute(null);
        model.CreateOfflineRole();
        var created = model.PlayerName;

        model.OfflineRoleName = "自定义名字";
        Assert.Contains("自定义名字", model.OfflineRoles);
        Assert.DoesNotContain(created, model.OfflineRoles);
        Assert.Equal("自定义名字", services.Settings.PlayerName);
    }
}

public sealed class AccountDeletionTests
{
    private static LaunchViewModel Create(out AppServices services)
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "del-" + Guid.NewGuid().ToString("N"));
        services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        return new LaunchViewModel(services, null, null, Path.Combine(root, "accounts"),
            _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));
    }

    [AvaloniaFact]
    public void DeletingOfflineRoleKeepsAtLeastOne()
    {
        var model = Create(out var services);
        model.SelectOfflineAccountCommand.Execute(null);
        model.CreateOfflineRole();
        model.CreateOfflineRole();
        var before = model.OfflineRoles.Count;

        var target = model.OfflineRoles.Last();
        model.DeleteOfflineRole(target);
        Assert.Equal(before - 1, model.OfflineRoles.Count);
        Assert.DoesNotContain(target, model.OfflineRoles);

        // 一直删到只剩一个：删除逻辑会保留最后一个默认角色，不会无限循环
        var guard = 0;
        while (model.OfflineRoles.Count > 1 && guard++ < 20)
            model.DeleteOfflineRole(model.OfflineRoles.Last());
        Assert.Single(model.OfflineRoles);
        Assert.False(string.IsNullOrWhiteSpace(services.Settings.PlayerName));
    }

    [AvaloniaFact]
    public void DeletingMicrosoftAccountSwitchesToRemaining()
    {
        var model = Create(out _);
        model.SetMicrosoftAccount(new MicrosoftAccount("First", "uuid-1", "t", "r", DateTimeOffset.MaxValue));
        model.SetMicrosoftAccount(new MicrosoftAccount("Second", "uuid-2", "t", "r", DateTimeOffset.MaxValue));

        model.DeleteMicrosoftAccount(model.MicrosoftAccounts.First(item => item.Uuid == "uuid-2"));
        Assert.Single(model.MicrosoftAccounts);
        Assert.Equal("First", model.PlayerName);
        Assert.True(model.IsMicrosoftAccount);
    }

    [AvaloniaFact]
    public void DeletingLastMicrosoftAccountFallsBackToOffline()
    {
        var model = Create(out _);
        model.SetMicrosoftAccount(new MicrosoftAccount("Only", "uuid-1", "t", "r", DateTimeOffset.MaxValue));
        model.DeleteMicrosoftAccount(model.MicrosoftAccounts.Single());
        Assert.Empty(model.MicrosoftAccounts);
        Assert.True(model.IsOfflineAccount);
    }

    [AvaloniaFact]
    public void DeletingAuthlibAccountRemovesIt()
    {
        var model = Create(out _);
        var account = new WMCL.Launch.Auth.AuthlibAccount(
            "https://example.invalid/api/yggdrasil", "Third", "uuid-a", "token", "client");
        model.SetAuthlibAccount(account);
        Assert.Single(model.AuthlibAccounts);
        model.DeleteAuthlibAccount(model.AuthlibAccounts.Single());
        Assert.Empty(model.AuthlibAccounts);
        Assert.True(model.IsOfflineAccount);
    }
}

public sealed class MicrosoftTokenPersistenceTests
{
    [AvaloniaFact]
    public void RefreshedTokensArePersisted()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "ms-token-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var accountsDir = Path.Combine(root, "accounts");
        var model = new LaunchViewModel(services, null, null, accountsDir,
            _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));

        model.SetMicrosoftAccount(new MicrosoftAccount("Player", "uuid-1", "old-token", "old-refresh", DateTimeOffset.MaxValue));
        // 模拟令牌刷新：同一 UUID、新的 refresh token
        model.SetMicrosoftAccount(new MicrosoftAccount("Player", "uuid-1", "new-token", "new-refresh", DateTimeOffset.MaxValue));

        var listPath = WMCL.Launch.Auth.MicrosoftAccountStore.ListPathFor(Path.Combine(accountsDir, "microsoft.json"));
        var saved = WMCL.Launch.Auth.MicrosoftAccountStore.LoadAll(listPath);
        Assert.Single(saved);
        Assert.Equal("new-refresh", saved[0].RefreshToken);
        Assert.Single(model.MicrosoftAccounts);
    }
}

public sealed class OfflineRoleChurnTests
{
    [AvaloniaFact]
    public void SwitchingRoleDoesNotRebuildCollection()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "churn-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var model = new LaunchViewModel(services, null, null, Path.Combine(root, "accounts"),
            _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));
        model.SelectOfflineAccountCommand.Execute(null);
        model.CreateOfflineRole();
        model.CreateOfflineRole();

        var changes = 0;
        model.OfflineRoles.CollectionChanged += (_, _) => changes++;
        var target = model.OfflineRoles.First(item => item != model.PlayerName);
        model.SelectedOfflineRole = target;

        Assert.Equal(0, changes);
        Assert.Equal(target, model.PlayerName);
    }

    [AvaloniaFact]
    public void RenamingReplacesInPlace()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "churn2-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var model = new LaunchViewModel(services, null, null, Path.Combine(root, "accounts"),
            _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));
        model.SelectOfflineAccountCommand.Execute(null);

        var adds = 0;
        var removes = 0;
        model.OfflineRoles.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add) adds++;
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove) removes++;
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) removes += 99;
        };
        model.OfflineRoleName = "改名后";
        Assert.Equal(0, adds);
        Assert.Equal(0, removes);
        Assert.Contains("改名后", model.OfflineRoles);
    }
}
