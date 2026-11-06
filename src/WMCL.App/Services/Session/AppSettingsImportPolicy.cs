namespace WMCL.App.Services;

public static class AppSettingsImportPolicy
{
    public static bool IsProtectedProperty(string name) => name is
        nameof(AppSettings.PreLaunchCommand) or nameof(AppSettings.PreLaunchWait) or
        nameof(AppSettings.ExtraJvmArgs) or nameof(AppSettings.ExtraGameArgs) or
        nameof(AppSettings.JavaPath) or nameof(AppSettings.CustomHomePagePath) or
        nameof(AppSettings.TerracottaExecutablePath) or
        nameof(AppSettings.MinecraftPurchaseNoticeAcknowledged);

    public static void Validate(AppSettings settings)
    {
        if (!double.IsFinite(settings.DownloadSpeedLimitMbps) || settings.DownloadSpeedLimitMbps < 0 ||
            settings.DownloadSpeedLimitMbps > long.MaxValue / 2097152d ||
            !double.IsFinite(settings.FontScale))
            throw new InvalidDataException("导入配置含无效的下载速度或界面缩放数值，未应用任何设置。");
    }

    public static AppSettings Sanitize(AppSettings settings)
    {
        Validate(settings);
        var defaults = new AppSettings();
        foreach (var property in typeof(AppSettings).GetProperties())
            if (IsProtectedProperty(property.Name))
                property.SetValue(settings, property.GetValue(defaults));
        return settings;
    }
}
