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

public partial class LaunchViewModel
{
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

    private bool _applyingMicrosoftAccount;

    private void ApplyMicrosoftAccount(MicrosoftAccount account)
    {
        _applyingMicrosoftAccount = true;
        try
        {
            MicrosoftLoginExpired = false;
            _skinAccountGeneration++;
            SkinUploadStatus = "";
            CurrentSkinVariant = "classic";
            _microsoftAccount = account;
            // 就地更新列表并持久化：刷新令牌必须落盘，否则下次启动仍用旧令牌
            UpsertMicrosoftAccount(account);
            IsAuthlibAccount = false;
            IsMicrosoftAccount = true;
            PlayerName = account.Name;
            SaveAccountType("microsoft");
            OnPropertyChanged(nameof(AuthlibSkinClosetUrl));
            OnPropertyChanged(nameof(CanOpenAuthlibSkinCloset));
            Capes.Clear();
            SelectedCape = null;
            CapeStatus = "";
            OnPropertyChanged(nameof(HasCapes));
            OnPropertyChanged(nameof(AddAccountLabel));
            SetAccountSkin(null);
            AccountStatus = $"微软账户 · {account.Name}";
            ApplyCachedSkin($"ms:{account.Uuid}");
            _ = LoadMicrosoftSkinAsync(account);
        }
        finally
        {
            OnPropertyChanged(nameof(SelectedMicrosoftAccount));
            _applyingMicrosoftAccount = false;
        }
    }

    /// <summary>就地更新账号条目（不重建集合），并写入磁盘。</summary>
    private void UpsertMicrosoftAccount(MicrosoftAccount account)
    {
        var index = _microsoftAccounts.FindIndex(item => item.Uuid == account.Uuid);
        if (index >= 0) _microsoftAccounts[index] = account;
        else _microsoftAccounts.Add(account);

        var visible = -1;
        for (var i = 0; i < MicrosoftAccounts.Count; i++)
        {
            if (MicrosoftAccounts[i].Uuid != account.Uuid) continue;
            visible = i;
            break;
        }
        if (visible >= 0) MicrosoftAccounts[visible] = account;
        else MicrosoftAccounts.Add(account);

        MicrosoftAccountStore.SaveAll(_microsoftAccounts, MicrosoftAccountStore.ListPathFor(_microsoftAccountPath));
        OnPropertyChanged(nameof(ShowMicrosoftAccountPicker));
    }

    public void SetMicrosoftAccount(MicrosoftAccount account) => ApplyMicrosoftAccount(account);

    private void ApplyAuthlibAccount(AuthlibAccount account)
    {
        MicrosoftLoginExpired = false;
        _skinAccountGeneration++;
        SkinUploadStatus = "";
        CurrentSkinVariant = "classic";
        _authlibAccount = account;
        IsMicrosoftAccount = false;
        IsAuthlibAccount = true;
        PlayerName = account.Name;
        SaveAccountType("authlib");
        OnPropertyChanged(nameof(AuthlibSkinClosetUrl));
        OnPropertyChanged(nameof(CanOpenAuthlibSkinCloset));
        Capes.Clear();
        SelectedCape = null;
        CapeStatus = "";
        OnPropertyChanged(nameof(HasCapes));
        AccountStatus = $"第三方账户 · {account.Name}";
        ApplyCachedSkin($"authlib:{account.Uuid}");
        if (SelectedAuthlibAccount is null ||
            !SameServer(SelectedAuthlibAccount.Server, account.Server) ||
            !string.Equals(SelectedAuthlibAccount.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase))
            SelectedAuthlibAccount = account;
        _ = LoadSkinAsync(account);
    }

    private void CacheCurrentSkin(string cacheKey)
    {
        if (AccountSkin is null) return;
        _skinCache[cacheKey] = AccountSkin;
        _skinCacheKey = cacheKey;
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
            CacheCurrentSkin($"ms:{account.Uuid}");
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
            CacheCurrentSkin($"authlib:{account.Uuid}");
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
    /// <summary>离线模式新增一个角色名（玩家1、玩家2…），随后可在名字框里改。</summary>
    public void CreateOfflineRole()
    {
        var index = 1;
        string candidate;
        do { candidate = "玩家" + index++; } while (_services.Settings.OfflineRoles.Contains(candidate));
        PlayerName = candidate;
        _services.Settings.PlayerName = candidate;
        if (!_services.Settings.OfflineRoles.Contains(candidate)) _services.Settings.OfflineRoles.Add(candidate);
        _services.SaveSettings();
        RefreshOfflineRoles();
        RefreshOfflineSkin();
        AccountStatus = "已新建离线角色：" + candidate;
    }

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
        PlayerName = _services.Settings.PlayerName;
        RefreshOfflineSkin();
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
}
