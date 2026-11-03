using System.Reflection;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class PurchaseNoticeTests
{
    [AvaloniaFact]
    public async Task AcknowledgementPersistsAcrossNewViewModelsAndLauncherRestarts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wmcl-purchase-notice-" + Guid.NewGuid().ToString("N"));
        var store = new FileAppSettingsStore(Path.Combine(directory, "settings.json"));
        var prompts = 0;
        Task<bool> Confirm(CancellationToken _) { prompts++; return Task.FromResult(true); }
        try
        {
            var services = new AppServices(new AppSettings { AccountType = "offline" }, saveSettings: store.Save);
            var first = new LaunchViewModel(services);
            Assert.True(await first.EnsurePurchaseNoticeAsync(Confirm));
            Assert.True(await first.EnsurePurchaseNoticeAsync(Confirm));
            Assert.True(await new LaunchViewModel(services).EnsurePurchaseNoticeAsync(Confirm));
            var restarted = new LaunchViewModel(new AppServices(store.Load(), saveSettings: store.Save));
            Assert.True(await restarted.EnsurePurchaseNoticeAsync(Confirm));
            Assert.True(store.Load().MinecraftPurchaseNoticeAcknowledged);
            Assert.Equal(1, prompts);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task CancelDoesNotRecordAcknowledgementAndNextAttemptStillPrompts()
    {
        var saves = 0;
        var services = new AppServices(new AppSettings { AccountType = "offline" }, saveSettings: _ => saves++);
        var launch = new LaunchViewModel(services);
        Assert.False(await launch.EnsurePurchaseNoticeAsync(_ => Task.FromResult(false)));
        Assert.False(services.Settings.MinecraftPurchaseNoticeAcknowledged);
        Assert.Equal(0, saves);
        Assert.True(await launch.EnsurePurchaseNoticeAsync(_ => Task.FromResult(true)));
        Assert.Equal(1, saves);
    }

    [AvaloniaFact]
    public async Task FailedSaveDoesNotLeaveAnUnpersistedAcknowledgement()
    {
        var services = new AppServices(new AppSettings { AccountType = "offline" },
            saveSettings: _ => throw new IOException("disk failure"));
        var launch = new LaunchViewModel(services);
        await Assert.ThrowsAsync<IOException>(() => launch.EnsurePurchaseNoticeAsync(_ => Task.FromResult(true)));
        Assert.False(services.Settings.MinecraftPurchaseNoticeAcknowledged);
    }

    [AvaloniaFact]
    public async Task CancelledConfirmationDoesNotSaveAcknowledgement()
    {
        var saves = 0;
        var services = new AppServices(new AppSettings { AccountType = "offline" }, saveSettings: _ => saves++);
        var launch = new LaunchViewModel(services);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => launch.EnsurePurchaseNoticeAsync(_ =>
        {
            cancellation.Cancel();
            return Task.FromResult(true);
        }, cancellation.Token));
        Assert.False(services.Settings.MinecraftPurchaseNoticeAcknowledged);
        Assert.Equal(0, saves);
    }

    [AvaloniaFact]
    public async Task MicrosoftAccountKeepsExistingNoNoticeBehaviour()
    {
        var services = new AppServices(new AppSettings { AccountType = "offline" }, saveSettings: _ => { });
        var launch = new LaunchViewModel(services) { IsMicrosoftAccount = true, IsAuthlibAccount = false };
        Assert.True(await launch.EnsurePurchaseNoticeAsync(_ => throw new InvalidOperationException("must not prompt")));
        Assert.False(services.Settings.MinecraftPurchaseNoticeAcknowledged);
    }

    [AvaloniaFact]
    public void ImportingSettingsCannotResetTheLocalAcknowledgement()
    {
        var local = new AppSettings { MinecraftPurchaseNoticeAcknowledged = true };
        using var settings = new SettingsViewModel(new AppServices(local, saveSettings: _ => { }));
        typeof(SettingsViewModel).GetMethod("LoadFromSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(settings, [SettingsService.ImportFromJson("{}")]);
        Assert.True(local.MinecraftPurchaseNoticeAcknowledged);
        Assert.False(SettingsService.ImportFromJson("""{"MinecraftPurchaseNoticeAcknowledged":true}""").MinecraftPurchaseNoticeAcknowledged);
    }

    [Fact]
    public void ExistingConfigurationWithoutTheFlagStillRequiresOneAcknowledgement() =>
        Assert.False(AppSettingsSerializer.Deserialize("{}").MinecraftPurchaseNoticeAcknowledged);
}
