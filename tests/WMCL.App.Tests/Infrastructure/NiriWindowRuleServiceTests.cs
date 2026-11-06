using WMCL.App.Services.Platform;
using WMCL.Link;
using System.Text;
using System.Text.RegularExpressions;
using WMCL.App.Services;

namespace WMCL.App.Tests;

[Trait("Kind", "Unit")]
public sealed class NiriWindowRuleServiceTests
{
    private readonly string _root = Path.Combine(WMCL.Tests.TestEnvironment.Root, "niri-window-effects-" + Guid.NewGuid().ToString("N"));
    private string Config => Path.Combine(_root, "config.kdl");

    public NiriWindowRuleServiceTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RuleOnlyMatchesTheLauncherAndContainsTheExplicitBlurBoolean(bool enabled)
    {
        var rule = NiriWindowRuleService.BuildRule(enabled);
        Assert.StartsWith("// WMCL window effects BEGIN\n", rule);
        Assert.EndsWith("// WMCL window effects END\n", rule);
        Assert.Contains("match app-id=r#\"^WMCL\\.App$\"#", rule);
        Assert.Contains("background-effect {\n        blur " + (enabled ? "true" : "false") + "\n", rule);
        Assert.Contains("draw-border-with-background false", rule);
        Assert.Contains("open-floating true", rule);
        Assert.Single(Regex.Matches(rule, "window-rule"));
        Assert.Single(Regex.Matches(rule, "match app-id"));
        Assert.DoesNotContain("opacity", rule, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passes", rule);
        Assert.DoesNotContain("radius", rule);
        Assert.Matches(new Regex(@"^WMCL\.App$"), "WMCL.App");
        Assert.DoesNotMatch(new Regex(@"^WMCL\.App$"), "WMCLXApp");
        Assert.DoesNotMatch(new Regex(@"^WMCL\.App$"), "Other.WMCL.App");
    }

    [Fact]
    public void ConfigPathUsesNiriOverrideThenXdgThenTheUserConfigDirectory()
    {
        var previousNiri = Environment.GetEnvironmentVariable("NIRI_CONFIG");
        var previousXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("NIRI_CONFIG", Config);
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(_root, "xdg"));
            Assert.Equal(Path.GetFullPath(Config), NiriWindowRuleService.GetConfigPath());
            Environment.SetEnvironmentVariable("NIRI_CONFIG", "relative-niri.kdl");
            Assert.Equal(Path.GetFullPath("relative-niri.kdl"), NiriWindowRuleService.GetConfigPath());
            Environment.SetEnvironmentVariable("NIRI_CONFIG", null);
            Assert.Equal(Path.Combine(_root, "xdg", "niri", "config.kdl"), NiriWindowRuleService.GetConfigPath());
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);
            Assert.Equal(Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "niri", "config.kdl")),
                NiriWindowRuleService.GetConfigPath());
        }
        finally
        {
            Environment.SetEnvironmentVariable("NIRI_CONFIG", previousNiri);
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousXdg);
        }
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", false)]
    [InlineData("\r\n", true)]
    public async Task AppendPreservesEveryOriginalByteBomAndLineEndingAndValidatesBesideIncludes(string newline, bool bom)
    {
        var text = "// 原配置 café" + newline + "include \"shared.kdl\"" + newline +
            "window-rule { match app-id=\"OtherApp\"; opacity 0.8; }";
        var original = Encode(text, bom);
        await File.WriteAllBytesAsync(Config, original);
        await File.WriteAllTextAsync(Path.Combine(_root, "shared.kdl"), "// relative include");
        var validated = 0;
        var service = new NiriWindowRuleService(async (candidate, ct) =>
        {
            validated++;
            Assert.True(Path.IsPathFullyQualified(candidate));
            Assert.Equal(_root, Path.GetDirectoryName(candidate));
            Assert.NotEqual(Config, candidate);
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(candidate)!, "shared.kdl")));
            Assert.Equal(original, await File.ReadAllBytesAsync(Config, ct));
            var bytes = await File.ReadAllBytesAsync(candidate, ct);
            Assert.Equal(original, bytes.Take(original.Length).ToArray());
            Assert.Contains(NiriWindowRuleService.BuildRule(true).Replace("\n", newline), Encoding.UTF8.GetString(bytes));
        });
        var backup = await service.ApplyAsync(Config, true);
        Assert.Equal(1, validated);
        Assert.True(Path.IsPathFullyQualified(backup));
        Assert.Equal(_root, Path.GetDirectoryName(backup));
        Assert.Equal(original, await File.ReadAllBytesAsync(backup));
        var expected = Encode(text + newline + NiriWindowRuleService.BuildRule(true).Replace("\n", newline), bom);
        Assert.Equal(expected, await File.ReadAllBytesAsync(Config));
        Assert.Empty(Directory.GetFiles(_root, ".wmcl-niri-*"));
    }

    [Fact]
    public async Task UpdatingOnlyTheOwnedBlockIsIdempotentAndKeepsUniqueCompleteBackups()
    {
        const string prefix = "// 不改其他配置\r\nwindow-rule { match app-id=\"Other\"; }\r\n";
        const string suffix = "\r\n// mixed line ending remains untouched\nlayout { gaps 8; }\n";
        var original = prefix + NiriWindowRuleService.BuildRule(true).TrimEnd('\n').Replace("\n", "\r\n") + suffix;
        await File.WriteAllTextAsync(Config, original, new UTF8Encoding(false));
        var service = new NiriWindowRuleService((_, _) => Task.CompletedTask);
        var firstBackup = await service.ApplyAsync(Config, false);
        var updated = await File.ReadAllTextAsync(Config);
        Assert.StartsWith(prefix, updated);
        Assert.EndsWith(suffix, updated);
        Assert.Contains("blur false", updated);
        Assert.DoesNotContain("blur true", updated);
        Assert.Single(Regex.Matches(updated, "WMCL window effects BEGIN"));
        Assert.Equal(original, await File.ReadAllTextAsync(firstBackup));
        var secondBackup = await service.ApplyAsync(Config, false);
        Assert.NotEqual(firstBackup, secondBackup);
        Assert.Equal(updated, await File.ReadAllTextAsync(secondBackup));
        Assert.Equal(updated, await File.ReadAllTextAsync(Config));
        Assert.Equal(original, await File.ReadAllTextAsync(firstBackup));
    }

    [Theory]
    [InlineData("")]
    [InlineData("// no newline")]
    [InlineData("// already terminated\n")]
    public async Task AppendingHandlesEmptyAndUnterminatedConfigurationWithoutDuplicateRules(string original)
    {
        await File.WriteAllTextAsync(Config, original);
        var service = new NiriWindowRuleService((_, _) => Task.CompletedTask);
        await service.ApplyAsync(Config, true);
        var first = await File.ReadAllTextAsync(Config);
        await service.ApplyAsync(Config, true);
        Assert.Equal(first, await File.ReadAllTextAsync(Config));
        Assert.StartsWith(original, first);
        Assert.Single(Regex.Matches(first, "WMCL window effects BEGIN"));
    }

    [Theory]
    [InlineData("// WMCL window effects BEGIN\nwindow-rule {}\n")]
    [InlineData("// WMCL window effects END\n")]
    [InlineData("// WMCL window effects END\n// WMCL window effects BEGIN\n")]
    [InlineData("// WMCL window effects BEGIN\n// WMCL window effects BEGIN\n// WMCL window effects END\n")]
    [InlineData("// WMCL window effects BEGIN extra\n// WMCL window effects END\n")]
    [InlineData("// WMCL window effects BEGIN\n// WMCL window effects END\n// WMCL window effects BEGIN\n// WMCL window effects END\n")]
    [InlineData("// WMCL window effects BROKEN\n")]
    public async Task IncompleteOrAmbiguousMarkersAreRejectedBeforeValidation(string original)
    {
        var bytes = Encoding.UTF8.GetBytes(original);
        await File.WriteAllBytesAsync(Config, bytes);
        var validations = 0;
        var service = new NiriWindowRuleService((_, _) => { validations++; return Task.CompletedTask; });
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ApplyAsync(Config, true));
        Assert.Equal(0, validations);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Config));
        Assert.Equal([Config], Directory.GetFiles(_root));
    }

    [Fact]
    public async Task InvalidUtf8IsRejectedWithoutLossyReplacementOrFileChanges()
    {
        byte[] original = [0x2f, 0x2f, 0x20, 0xc3, 0x28];
        await File.WriteAllBytesAsync(Config, original);
        var service = new NiriWindowRuleService((_, _) => throw new InvalidOperationException("must not validate"));
        await Assert.ThrowsAsync<DecoderFallbackException>(() => service.ApplyAsync(Config, false));
        Assert.Equal(original, await File.ReadAllBytesAsync(Config));
        Assert.Equal([Config], Directory.GetFiles(_root));
    }

    [Fact]
    public async Task ValidationFailureDoesNotCreateABackupOrChangeTheOriginalAndRemovesTheCandidate()
    {
        var original = Encoding.UTF8.GetBytes("// original\n");
        await File.WriteAllBytesAsync(Config, original);
        string? candidatePath = null;
        var service = new NiriWindowRuleService((candidate, _) =>
        {
            candidatePath = candidate;
            Assert.True(File.Exists(candidate));
            return Task.FromException(new InvalidDataException("synthetic validation failure"));
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ApplyAsync(Config, false));
        Assert.Equal(original, await File.ReadAllBytesAsync(Config));
        Assert.NotNull(candidatePath);
        Assert.False(File.Exists(candidatePath));
        Assert.Equal([Config], Directory.GetFiles(_root));
    }

    [Fact]
    public async Task CancellationWhileValidatingRemovesOnlyTheOwnedCandidate()
    {
        await File.WriteAllTextAsync(Config, "// original\n");
        await File.WriteAllTextAsync(Path.Combine(_root, "unrelated.tmp"), "preserve");
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new NiriWindowRuleService(async (candidate, ct) =>
        {
            entered.SetResult(candidate);
            await Task.Delay(Timeout.Infinite, ct);
        });
        var applying = service.ApplyAsync(Config, true, cancellation.Token);
        var path = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => applying);
        Assert.Equal("// original\n", await File.ReadAllTextAsync(Config));
        Assert.Equal("preserve", await File.ReadAllTextAsync(Path.Combine(_root, "unrelated.tmp")));
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_root, "*.wmcl-backup-*"));
    }

    [Fact]
    public async Task CancellationJustAfterValidationStillCannotCommit()
    {
        await File.WriteAllTextAsync(Config, "// original");
        using var cancellation = new CancellationTokenSource();
        var service = new NiriWindowRuleService((_, _) => { cancellation.Cancel(); return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(Config, true, cancellation.Token));
        Assert.Equal("// original", await File.ReadAllTextAsync(Config));
        Assert.Equal([Config], Directory.GetFiles(_root));
    }

    [Fact]
    public async Task AnAlreadyCancelledRequestDoesNotValidateOrCreateFiles()
    {
        await File.WriteAllTextAsync(Config, "// original");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new NiriWindowRuleService((_, _) => throw new InvalidOperationException("must not validate"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(Config, true, cancellation.Token));
        Assert.Equal([Config], Directory.GetFiles(_root));
    }

    [Fact]
    public async Task ConcurrentExternalSaveIsPreservedInsteadOfBeingOverwritten()
    {
        await File.WriteAllTextAsync(Config, "// original");
        var external = Encoding.UTF8.GetBytes("// externally saved while validating\n");
        var service = new NiriWindowRuleService(async (_, ct) => await File.WriteAllBytesAsync(Config, external, ct));
        var exception = await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(Config, true));
        Assert.Contains("其他程序修改", exception.Message);
        Assert.Equal(external, await File.ReadAllBytesAsync(Config));
        Assert.Equal([Config], Directory.GetFiles(_root));
    }

    [Fact]
    public async Task TamperingWithTheValidatedCandidateCannotCommitOrDestroyTheOriginal()
    {
        await File.WriteAllTextAsync(Config, "// original");
        var service = new NiriWindowRuleService(async (candidate, ct) => await File.WriteAllTextAsync(candidate, "// changed candidate", ct));
        await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(Config, true));
        Assert.Equal("// original", await File.ReadAllTextAsync(Config));
        Assert.Equal([Config], Directory.GetFiles(_root));
    }

    [Fact]
    public async Task SymlinkTargetIsRejectedWithManualCopyFallback()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        var actual = Path.Combine(_root, "actual.kdl");
        await File.WriteAllTextAsync(actual, "// preserve linked file");
        File.CreateSymbolicLink(Config, actual);
        var service = new NiriWindowRuleService((_, _) => throw new InvalidOperationException("must not validate"));
        var exception = await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(Config, true));
        Assert.Contains("符号链接", exception.Message);
        Assert.Contains("复制 Niri 规则", exception.Message);
        Assert.NotNull(new FileInfo(Config).LinkTarget);
        Assert.Equal("// preserve linked file", await File.ReadAllTextAsync(actual));
        Assert.Empty(Directory.GetFiles(_root, ".wmcl-niri-*"));
    }

    [Fact]
    public async Task SymlinkReplacingTheOriginalDuringValidationIsNotFollowedOrOverwritten()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        await File.WriteAllTextAsync(Config, "// original");
        var actual = Path.Combine(_root, "external.kdl");
        await File.WriteAllTextAsync(actual, "// external");
        var service = new NiriWindowRuleService((_, _) =>
        {
            File.Delete(Config);
            File.CreateSymbolicLink(Config, actual);
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(Config, true));
        Assert.NotNull(new FileInfo(Config).LinkTarget);
        Assert.Equal("// external", await File.ReadAllTextAsync(actual));
        Assert.Empty(Directory.GetFiles(_root, "*.wmcl-backup-*"));
        Assert.Empty(Directory.GetFiles(_root, ".wmcl-niri-*"));
    }

    [Fact]
    public async Task AtomicReplacementAndBackupKeepOriginalUnixPermissions()
    {
        if (OperatingSystem.IsWindows()) return;
        await File.WriteAllTextAsync(Config, "// permissions");
        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(Config, mode);
        var service = new NiriWindowRuleService((_, _) => Task.CompletedTask);
        var backup = await service.ApplyAsync(Config, true);
        Assert.Equal(mode, File.GetUnixFileMode(Config));
        Assert.Equal(mode, File.GetUnixFileMode(backup));
    }

    private static byte[] Encode(string text, bool bom)
    {
        var bytes = new UTF8Encoding(false, true).GetBytes(text);
        return bom ? new byte[] { 0xef, 0xbb, 0xbf }.Concat(bytes).ToArray() : bytes;
    }
}
