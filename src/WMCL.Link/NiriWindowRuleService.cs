using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace WMCL.Link;

/// <summary>
/// Opt-in editing of one launcher-owned Niri rule. Callers must obtain user confirmation;
/// this service never changes compositor configuration merely because a setting changed.
/// </summary>
public sealed class NiriWindowRuleService
{
    internal const string BeginMarker = "// WMCL window effects BEGIN";
    internal const string EndMarker = "// WMCL window effects END";
    private const string MarkerName = "WMCL window effects";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly SemaphoreSlim ApplyGate = new(1, 1);
    private readonly Func<string, CancellationToken, Task> _validate;

    public NiriWindowRuleService() : this(ValidateWithNiriAsync) { }

    internal NiriWindowRuleService(Func<string, CancellationToken, Task> validate)
    {
        ArgumentNullException.ThrowIfNull(validate);
        _validate = validate;
    }

    public static string GetConfigPath()
    {
        var configured = Environment.GetEnvironmentVariable("NIRI_CONFIG");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var directory = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(directory))
            directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.GetFullPath(Path.Combine(directory, "niri", "config.kdl"));
    }

    /// <summary>Client opacity remains an application concern, never a compositor-wide rule.</summary>
    public static string BuildRule(bool blurEnabled) =>
        BeginMarker + "\n" +
        "window-rule {\n" +
        "    match app-id=r#\"^WMCL\\.App$\"#\n" +
        // 窗口可自由缩放后 niri 会把它当普通窗口平铺；显式要求浮动，
        // 保持与之前“固定尺寸窗口自动浮动”一致的观感。
        "    open-floating true\n" +
        "    draw-border-with-background false\n" +
        "    background-effect {\n" +
        "        blur " + (blurEnabled ? "true" : "false") + "\n" +
        "    }\n" +
        "}\n" +
        EndMarker + "\n";

    /// <summary>
    /// Validate a same-directory candidate, back up the unchanged original, then atomically
    /// replace it. No shell, compositor restart, global effects, or other applications are modified.
    /// </summary>
    public async Task<string> ApplyAsync(string configPath, bool blurEnabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        var target = Path.GetFullPath(configPath);
        var directory = Path.GetDirectoryName(target)!;
        var candidateName = ".wmcl-niri-" + Guid.NewGuid().ToString("N") + ".kdl";
        var candidate = Path.GetFullPath(Path.Combine(directory, candidateName));
        var backupName = Path.GetFileName(target) + ".wmcl-backup-" +
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff", System.Globalization.CultureInfo.InvariantCulture) +
            "-" + Guid.NewGuid().ToString("N") + ".kdl";
        var backup = Path.GetFullPath(Path.Combine(directory, backupName));
        var candidateCreated = false;
        var backupCreated = false;
        var committed = false;
        await ApplyGate.WaitAsync(cancellationToken);
        try
        {
            RejectLinkedFile(target);
            var original = await File.ReadAllBytesAsync(target, cancellationToken);
            var originalHash = SHA256.HashData(original);
            var bom = original.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf });
            var content = Utf8.GetString(original, bom ? 3 : 0, original.Length - (bom ? 3 : 0));
            var updated = UpdateOwnedBlock(content, blurEnabled);
            var encoded = Utf8.GetBytes(updated);
            var candidateBytes = bom ? new byte[] { 0xef, 0xbb, 0xbf }.Concat(encoded).ToArray() : encoded;
            UnixFileMode? mode = OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(target);

            VerifyOwnedPath(candidate, directory, candidateName);
            await WriteNewFileAsync(candidate, candidateBytes, mode, cancellationToken, () => candidateCreated = true);
            await _validate(candidate, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinkedFile(candidate);
            if (!(await File.ReadAllBytesAsync(candidate, cancellationToken)).AsSpan().SequenceEqual(candidateBytes))
                throw new IOException("Niri 候选配置在验证期间发生变化，未写入原配置。");
            await EnsureOriginalUnchangedAsync(target, originalHash, cancellationToken);

            VerifyOwnedPath(backup, directory, backupName);
            await WriteNewFileAsync(backup, original, mode, cancellationToken, () => backupCreated = true);
            // Recheck after writing the backup as well: editors may save while validation runs.
            await EnsureOriginalUnchangedAsync(target, originalHash, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            VerifyOwnedPath(candidate, directory, candidateName);
            if (!string.Equals(Path.GetFullPath(configPath), target, StringComparison.Ordinal))
                throw new IOException("Niri 配置路径发生变化，未写入原配置。");
            File.Move(candidate, target, overwrite: true);
            candidateCreated = false;
            committed = true;
            return backup;
        }
        finally
        {
            if (candidateCreated) DeleteOwnedFile(candidate, directory, candidateName);
            if (backupCreated && !committed) DeleteOwnedFile(backup, directory, backupName);
            ApplyGate.Release();
        }
    }

    private static string UpdateOwnedBlock(string content, bool blurEnabled)
    {
        var begins = new List<(int Start, int End)>();
        var ends = new List<(int Start, int End)>();
        var position = 0;
        while (position < content.Length)
        {
            var start = position;
            while (position < content.Length && content[position] is not ('\r' or '\n')) position++;
            var end = position;
            var line = content[start..end].Trim(' ', '\t');
            if (line == BeginMarker) begins.Add((start, end));
            if (line == EndMarker) ends.Add((start, end));
            if (position < content.Length && content[position++] == '\r' && position < content.Length && content[position] == '\n') position++;
        }
        var occurrences = CountOccurrences(content, MarkerName);
        if (begins.Count == 0 && ends.Count == 0 && occurrences == 0)
        {
            var newline = DetectNewline(content);
            var separator = content.Length == 0 || content[^1] is '\r' or '\n' ? "" : newline;
            return content + separator + BuildRule(blurEnabled).Replace("\n", newline, StringComparison.Ordinal);
        }
        if (begins.Count != 1 || ends.Count != 1 || occurrences != 2 || ends[0].Start <= begins[0].Start)
            throw new InvalidDataException("WMCL 的 Niri 配置标记不完整或出现多次，未修改；请手动复制规则并检查已有标记。");
        var block = BuildRule(blurEnabled).TrimEnd('\n').Replace("\n", DetectNewline(content), StringComparison.Ordinal);
        return content[..begins[0].Start] + block + content[ends[0].End..];
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
        return count;
    }

    private static string DetectNewline(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n') return "\n";
            if (text[index] == '\r') return index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";
        }
        return "\n";
    }

    private static void RejectLinkedFile(string path)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Niri 配置是符号链接，未自动写入；请使用“复制 Niri 规则”手动加入实际配置。");
        if (!info.Exists) throw new FileNotFoundException("未找到 Niri 配置文件，请先创建配置或手动复制规则。", path);
    }

    private static async Task EnsureOriginalUnchangedAsync(string target, byte[] originalHash, CancellationToken cancellationToken)
    {
        RejectLinkedFile(target);
        var current = await File.ReadAllBytesAsync(target, cancellationToken);
        if (!SHA256.HashData(current).AsSpan().SequenceEqual(originalHash))
            throw new IOException("Niri 配置已被其他程序修改，未覆盖；请重新查看配置后再应用。");
    }

    private static async Task WriteNewFileAsync(string path, byte[] bytes, UnixFileMode? mode, CancellationToken cancellationToken, Action onCreated)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 4096, options: FileOptions.Asynchronous);
        onCreated();
        if (!OperatingSystem.IsWindows() && mode is { } permissions) File.SetUnixFileMode(path, permissions);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static void VerifyOwnedPath(string path, string directory, string fileName)
    {
        if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(Path.Combine(directory, fileName)), StringComparison.Ordinal) ||
            !string.Equals(Path.GetDirectoryName(path), directory, StringComparison.Ordinal))
            throw new IOException("拒绝操作不属于本次 Niri 更新的临时文件。");
    }

    private static void DeleteOwnedFile(string path, string directory, string fileName)
    {
        VerifyOwnedPath(path, directory, fileName);
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task ValidateWithNiriAsync(string candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "niri",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(candidate)!
            }
        };
        process.StartInfo.ArgumentList.Add("validate");
        process.StartInfo.ArgumentList.Add("--config");
        process.StartInfo.ArgumentList.Add(candidate);
        try
        {
            if (!process.Start()) throw new InvalidOperationException("无法启动 Niri 配置验证。");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException("无法运行 niri validate；请确认已安装 Niri，或手动复制规则。", exception);
        }
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var messages = (await error + "\n" + await output).Trim();
            if (process.ExitCode != 0)
                throw new InvalidDataException("Niri 配置验证失败，原配置未修改：" + messages[..Math.Min(messages.Length, 4096)]);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException) { }
            try { await Task.WhenAll(output, error); }
            catch (OperationCanceledException) { }
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw new TimeoutException("Niri 配置验证超时，原配置未修改。");
        }
    }
}
