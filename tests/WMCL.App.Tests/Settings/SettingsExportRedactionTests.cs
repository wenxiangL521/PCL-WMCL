using Avalonia.Headless.XUnit;
using WMCL.App.Services;

namespace WMCL.App.Tests;

/// <summary>配置导出脱敏的回归测试：代理密码与 API Key 不随导出文件泄漏。</summary>
[Trait("Kind", "Headless")]
public sealed class SettingsExportRedactionTests
{
    [AvaloniaFact]
    public void ExportJson_StripsSecretsButKeepsOtherSettings()
    {
        var settings = new AppSettings
        {
            ProxyMode = "custom",
            ProxyUrl = "http://127.0.0.1:8080",
            ProxyUsername = "user",
            ProxyPassword = "super-secret",
            PlayerName = "Steve",
            MemoryMb = 4096,
            Theme = "dark",
            DownloadThreads = 32
        };

        var json = SettingsService.ExportToJson(settings);

        // 敏感凭据不得出现在导出 JSON 中。
        Assert.DoesNotContain("super-secret", json);
        // Client ID / API Key 已不再进入设置，导出里也不会出现。
        Assert.DoesNotContain("MicrosoftClientId", json);
        Assert.DoesNotContain("CurseForgeApiKey", json);

        // 非敏感设置完整保留。
        var imported = SettingsService.ImportFromJson(json);
        Assert.Equal("http://127.0.0.1:8080", imported.ProxyUrl);
        Assert.Equal("user", imported.ProxyUsername);
        Assert.Equal("Steve", imported.PlayerName);
        Assert.Equal(4096, imported.MemoryMb);
        Assert.Equal("dark", imported.Theme);
        Assert.Equal(32, imported.DownloadThreads);
        // 敏感字段为空，导入方需重新填写。
        Assert.Equal("", imported.ProxyPassword);
    }

    [AvaloniaFact]
    public void ExportToFile_WritesRedactedJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wmcl-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "settings.json");
            var settings = new AppSettings { ProxyPassword = "pw" };
            SettingsService.ExportToFile(settings, file);

            var raw = File.ReadAllText(file);
            Assert.DoesNotContain("\"pw\"", raw);
            Assert.DoesNotContain("\"key\"", raw);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [AvaloniaFact]
    public void ExportRedaction_DoesNotMutateOriginalSettings()
    {
        var settings = new AppSettings { ProxyPassword = "pw", PlayerName = "Alex" };
        SettingsService.ExportToJson(settings);
        // 导出脱敏必须作用于副本，不能改掉运行中的设置。
        Assert.Equal("pw", settings.ProxyPassword);
        Assert.Equal("Alex", settings.PlayerName);
    }
}
