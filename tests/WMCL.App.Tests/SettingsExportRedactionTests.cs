using Avalonia.Headless.XUnit;
using WMCL.App.Services;

namespace WMCL.App.Tests;

/// <summary>配置导出脱敏的回归测试：代理密码与 API Key 不随导出文件泄漏。</summary>
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
            CurseForgeApiKey = "cf-key-123",
            MicrosoftClientId = "00000000-1111-2222-3333-444444444444",
            PlayerName = "Steve",
            MemoryMb = 4096,
            Theme = "dark",
            DownloadThreads = 32
        };

        var json = SettingsService.ExportToJson(settings);

        // 敏感凭据不得出现在导出 JSON 中。
        Assert.DoesNotContain("super-secret", json);
        Assert.DoesNotContain("cf-key-123", json);
        // ClientId 属于公共客户端 ID（非机密），保持导出以便迁移。
        Assert.Contains("00000000-1111-2222-3333-444444444444", json);

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
        Assert.Equal("", imported.CurseForgeApiKey);
    }

    [AvaloniaFact]
    public void ExportToFile_WritesRedactedJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wmcl-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "settings.json");
            var settings = new AppSettings { ProxyPassword = "pw", CurseForgeApiKey = "key" };
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
