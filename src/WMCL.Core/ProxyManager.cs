namespace WMCL.Core;

/// <summary>代理模式。</summary>
public enum ProxyMode
{
    /// <summary>不使用代理。</summary>
    NoProxy,
    /// <summary>使用系统代理。</summary>
    SystemProxy,
    /// <summary>使用自定义代理地址。</summary>
    CustomProxy
}

/// <summary>代理配置。</summary>
public sealed record ProxyConfig(
    ProxyMode Mode,
    string CustomAddress = "",
    string? Username = null,
    string? Password = null,
    string BypassList = "localhost,127.0.0.1,::1");

/// <summary>
/// 代理管理器。负责解析代理模式、构建 IWebProxy 实例、检测系统代理。
/// 在 HttpClient 创建前调用 Apply()。
/// </summary>
public static class ProxyManager
{
    /// <summary>当前代理配置（由 App 启动时注入）。</summary>
    public static ProxyConfig Current { get; set; } = new(ProxyMode.NoProxy);

    /// <summary>系统代理地址（从系统设置读取，仅在 SystemProxy 模式下使用）。</summary>
    public static string? SystemProxyAddress { get; set; }

    /// <summary>将代理配置注入 .NET HTTP 环境变量，并重置全局 HttpClient 使其立即生效。</summary>
    public static void Apply(ProxyConfig config)
    {
        Current = config;
        var value = config.Mode switch
        {
            ProxyMode.NoProxy => null,
            ProxyMode.SystemProxy => SystemProxyAddress,
            ProxyMode.CustomProxy => NormalizeAddress(config.CustomAddress),
            _ => null
        };

        if (value is null)
        {
            // 清除代理环境变量
            Environment.SetEnvironmentVariable("HTTP_PROXY", null);
            Environment.SetEnvironmentVariable("HTTPS_PROXY", null);
            Environment.SetEnvironmentVariable("ALL_PROXY", null);
        }
        else
        {
            Environment.SetEnvironmentVariable("HTTP_PROXY", value);
            Environment.SetEnvironmentVariable("HTTPS_PROXY", value);
            Environment.SetEnvironmentVariable("ALL_PROXY", value);

            if (!string.IsNullOrWhiteSpace(config.BypassList))
                Environment.SetEnvironmentVariable("NO_PROXY", config.BypassList);
            else
                Environment.SetEnvironmentVariable("NO_PROXY", "localhost,127.0.0.1,::1");
        }

        // SocketsHttpHandler 只在构造时读取一次代理环境变量；
        // 不重置的话，运行时改代理对已创建的全局客户端无效（需重启才生效）。
        Http.Http.ResetClient();
    }

    /// <summary>归一化代理地址，自动补全 scheme。</summary>
    public static string? NormalizeAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var value = address.Trim();
        if (!value.Contains("://"))
            value = "http://" + value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == "http" || uri.Scheme == "https")
            ? uri.ToString().TrimEnd('/')
            : null;
    }

    /// <summary>
    /// 把设置中存储的代理模式字符串（none/system/custom）解析为枚举。
    /// 不能用 Enum.TryParse：枚举成员名是 NoProxy/SystemProxy/CustomProxy，
    /// 与 UI 下拉框存储值不一致，直接 TryParse 永远失败。
    /// </summary>
    public static ProxyMode ParseMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "system" => ProxyMode.SystemProxy,
        "custom" => ProxyMode.CustomProxy,
        _ => ProxyMode.NoProxy
    };

    /// <summary>检测系统代理。</summary>
    public static async Task<string?> DetectSystemProxyAsync()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return DetectWindowsProxy();
            if (OperatingSystem.IsLinux())
                return await DetectLinuxProxyAsync();
            if (OperatingSystem.IsMacOS())
                return ParseScutilProxy(await RunCommandAsync("scutil", "--proxy"));
        }
        catch
        {
            // 检测失败时返回 null
        }
        return null;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? DetectWindowsProxy()
    {
        // Windows: 从注册表读取系统代理
        using var reg = Microsoft.Win32.Registry.CurrentUser
            .OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings");
        if (reg is null) return null;
        var proxy = reg.GetValue("ProxyEnable") is int enable && enable == 1
            ? reg.GetValue("ProxyServer") as string
            : null;
        return string.IsNullOrWhiteSpace(proxy) ? null : proxy;
    }

    private static async Task<string?> DetectLinuxProxyAsync()
    {
        // Linux: 读取 gsettings（GNOME 系代理设置）
        var mode = await RunCommandAsync("gsettings", "get", "org.gnome.system.proxy mode");
        if (mode.Trim().Trim('\'') != "manual") return null;

        var host = (await RunCommandAsync(
            "gsettings", "get", "org.gnome.system.proxy.http host")).Trim().Trim('\'');
        if (host.Length == 0) return null;

        var portText = (await RunCommandAsync(
            "gsettings", "get", "org.gnome.system.proxy.http port")).Trim();
        // GNOME 端口未设置时为 0；0 不是有效端口，视为未配置代理。
        return int.TryParse(portText, out var port) && port > 0 ? $"{host}:{port}" : null;
    }

    /// <summary>
    /// 解析 <c>scutil --proxy</c> 输出，返回最佳代理地址（优先 HTTPS）。
    /// 输出为每个字段独立一行、主机与端口分开的字典，例如：
    /// <code>
    /// &lt;dictionary&gt; {
    ///   HTTPEnable : 1
    ///   HTTPProxy : 127.0.0.1
    ///   HTTPPort : 8080
    ///   HTTPSEnable : 0
    /// }
    /// </code>
    /// </summary>
    internal static string? ParseScutilProxy(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length > 0 && value.Length > 0)
                values[key] = value;
        }
        return ScutilAddress(values, "HTTPS") ?? ScutilAddress(values, "HTTP");

        static string? ScutilAddress(Dictionary<string, string> values, string protocol)
        {
            // 仅当对应协议启用（Enable : 1）且主机、端口均有效时返回。
            if (!values.TryGetValue(protocol + "Enable", out var enabled) || enabled != "1")
                return null;
            if (!values.TryGetValue(protocol + "Proxy", out var host) || IsMissing(host))
                return null;
            if (!values.TryGetValue(protocol + "Port", out var port) ||
                !int.TryParse(port, out var portNumber) || portNumber <= 0)
                return null;
            return $"{host}:{port}";
        }

        static bool IsMissing(string value) =>
            value.Length == 0 || value == "<null>";
    }

    /// <summary>运行命令并返回标准输出（失败或超时返回空字符串）。</summary>
    private static async Task<string> RunCommandAsync(string fileName, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(psi);
        if (process is null) return "";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 进程可能已退出 */ }
        }
        return output;
    }

    /// <summary>获取当前代理地址（用于 UI 显示）。</summary>
    public static string ResolvedAddress => Current.Mode switch
    {
        ProxyMode.NoProxy => "不使用代理",
        ProxyMode.SystemProxy => SystemProxyAddress ?? "未检测到系统代理",
        ProxyMode.CustomProxy => NormalizeAddress(Current.CustomAddress) ?? "无效的代理地址",
        _ => "未设置"
    };

    /// <summary>获取代理配置摘要（不含凭据）。</summary>
    public static string Describe() => Current.Mode switch
    {
        ProxyMode.NoProxy => "不使用代理",
        ProxyMode.SystemProxy => $"系统代理（{ResolvedAddress}）",
        ProxyMode.CustomProxy => $"自定义代理（{ResolvedAddress}）",
        _ => "未设置"
    };
}
