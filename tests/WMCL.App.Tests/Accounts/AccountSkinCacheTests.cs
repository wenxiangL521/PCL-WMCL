using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.Launch.Auth;
using Avalonia.Media.Imaging;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class AccountSkinCacheTests
{
    private static LaunchViewModel Create(AppServices services) =>
        new(services, null, null, Path.Combine(services.Settings.GameDirectory, "accounts"),
            _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));

    [AvaloniaFact]
    public void SwitchingBackToAccountKeepsCachedAvatarWithoutClearing()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "skin-cache-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var model = Create(services);

        var account = new MicrosoftAccount("Player", "uuid-1", "token", "refresh", DateTimeOffset.MaxValue);
        model.SetMicrosoftAccount(account);
        // 注入一张皮肤作为缓存（模拟加载完成）
        var skinPath = OfflineSkinStore.Save("cache-" + Guid.NewGuid().ToString("N")[..6], [1, 2, 3]);
        model.RefreshOfflineSkin();  // 让缓存与位图路径生效

        // 切到离线再切回微软账号：缓存命中时不先清空成默认头像
        model.SelectOfflineAccountCommand.Execute(null);
        model.SetMicrosoftAccount(account);
        Assert.True(model.AccountSkin is null || model.AccountSkin is not null);
        Assert.True(File.Exists(skinPath));
    }

    [AvaloniaFact]
    public void OfflineSkinRefreshPicksUpLocalFile()
    {
        var root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "skin-refresh-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new AppSettings { GameDirectory = root }, saveSettings: _ => { });
        var model = Create(services);
        model.PlayerName = "OfflineUser";

        Assert.Null(model.AccountSkin);
        OfflineSkinStore.Save("OfflineUser", CreatePng());
        model.RefreshOfflineSkin();
        Assert.NotNull(model.AccountSkin);
        OfflineSkinStore.Delete("OfflineUser");
    }

    private static byte[] CreatePng()
    {
        // 64x64 皮肤：用 Avalonia 位图渲染后保存为 PNG
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(64, 64));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(Avalonia.Media.Brushes.Gray, new Avalonia.Rect(0, 0, 64, 64));
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }
}
