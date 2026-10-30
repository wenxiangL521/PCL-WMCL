using WMCL.Launch.Auth;

namespace WMCL.Launch.Tests;

public sealed class DataProtectionServiceTests
{
    [Fact]
    public void Encrypt_Decrypt_RoundTrips()
    {
        var plaintext = "Hello, WMCL!";
        var encrypted = DataProtectionService.Encrypt(plaintext);
        Assert.NotEqual(plaintext, encrypted);
        Assert.Equal(plaintext, DataProtectionService.Decrypt(encrypted));
    }

    [Fact]
    public void Encrypt_Decrypt_Unicode()
    {
        var plaintext = "你好，世界！🎮 Minecraft";
        var encrypted = DataProtectionService.Encrypt(plaintext);
        Assert.Equal(plaintext, DataProtectionService.Decrypt(encrypted));
    }

    [Fact]
    public void Encrypt_Decrypt_LongText()
    {
        var plaintext = new string('A', 10000);
        var encrypted = DataProtectionService.Encrypt(plaintext);
        Assert.Equal(plaintext, DataProtectionService.Decrypt(encrypted));
    }

    [Fact]
    public void Encrypt_EachCall_DifferentCiphertext()
    {
        var plaintext = "same input";
        var enc1 = DataProtectionService.Encrypt(plaintext);
        var enc2 = DataProtectionService.Encrypt(plaintext);
        Assert.NotEqual(enc1, enc2);
        Assert.Equal(plaintext, DataProtectionService.Decrypt(enc1));
        Assert.Equal(plaintext, DataProtectionService.Decrypt(enc2));
    }

    [Fact]
    public void Decrypt_InvalidInput_ReturnsNull()
    {
        Assert.Null(DataProtectionService.Decrypt(""));
        Assert.Null(DataProtectionService.Decrypt("not-base64"));
        Assert.Null(DataProtectionService.Decrypt("!!!invalid!!!"));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ReturnsNull()
    {
        var encrypted = DataProtectionService.Encrypt("secret");
        var data = Convert.FromBase64String(encrypted);
        // Tamper with the last byte (tag)
        data[^1] ^= 0xFF;
        var tampered = Convert.ToBase64String(data);
        Assert.Null(DataProtectionService.Decrypt(tampered));
    }

    [Fact]
    public void Decrypt_EmptyString_ReturnsNull()
    {
        Assert.Null(DataProtectionService.Decrypt(""));
    }

    [Fact]
    public void MicrosoftAccountStore_SaveLoad_RoundTrips()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "wmcl-encrypt-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var path = Path.Combine(tmpDir, "microsoft.json");
        try
        {
            var account = new MicrosoftAccount(
                "TestUser", "12345678-1234-1234-1234-123456789012",
                "access-token-abc123", "refresh-token-xyz789", DateTimeOffset.UtcNow.AddHours(1));
            MicrosoftAccountStore.Save(account, path);

            // 文件内容应该是密文，不包含明文 token
            var raw = File.ReadAllText(path);
            Assert.DoesNotContain("access-token-abc123", raw);
            Assert.DoesNotContain("refresh-token-xyz789", raw);
            Assert.DoesNotContain("TestUser", raw);

            // 加载应成功
            var loaded = MicrosoftAccountStore.Load(path);
            Assert.NotNull(loaded);
            Assert.Equal("TestUser", loaded.Name);
            Assert.Equal("access-token-abc123", loaded.AccessToken);
            Assert.Equal("refresh-token-xyz789", loaded.RefreshToken);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void MicrosoftAccountStore_Load_PlaintextJson_BackwardCompatible()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "wmcl-encrypt-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var path = Path.Combine(tmpDir, "microsoft.json");
        try
        {
            // 写入明文 JSON（旧格式）
            File.WriteAllText(path,
                """{"name":"Legacy","uuid":"aaa","accessToken":"plain-token","refreshToken":"legacy-refresh"}""");

            // 应能向后兼容加载
            var loaded = MicrosoftAccountStore.Load(path);
            Assert.NotNull(loaded);
            Assert.Equal("Legacy", loaded.Name);
            Assert.Equal("plain-token", loaded.AccessToken);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void MicrosoftAccountStore_Clear_RemovesFile()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "wmcl-encrypt-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var path = Path.Combine(tmpDir, "microsoft.json");
        try
        {
            var account = new MicrosoftAccount("User", "uuid", "token", "refresh", DateTimeOffset.UtcNow);
            MicrosoftAccountStore.Save(account, path);
            Assert.True(File.Exists(path));

            MicrosoftAccountStore.Clear(path);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }
}
