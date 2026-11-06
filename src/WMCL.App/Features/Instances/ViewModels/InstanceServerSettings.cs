namespace WMCL.App.ViewModels.Instances;

/// <summary>实例的服务器、认证与启动前设置（空服务器时端口不落盘）。</summary>
public readonly record struct InstanceServerSettings(
    string Server,
    decimal Port,
    string AuthServer,
    string AuthServerName,
    string PluginDirectory,
    string PreLaunchCommand,
    bool PreLaunchWait)
{
    public const decimal DefaultPort = 25565;

    public static InstanceServerSettings From(
        string? server, int? port, string? authServer, string? authServerName,
        string? pluginDirectory, string? preLaunchCommand, bool preLaunchWait) =>
        new(
            server ?? "",
            port ?? DefaultPort,
            authServer ?? "",
            authServerName ?? "",
            pluginDirectory ?? "",
            preLaunchCommand ?? "",
            preLaunchWait);

    /// <summary>端口：只有填了服务器才写回，否则为 null（跟随默认）。</summary>
    public int? EffectivePort => string.IsNullOrWhiteSpace(Server) ? null : (int)Port;

    public string TrimmedServer => Server.Trim();
    public string TrimmedAuthServer => AuthServer.Trim();
    public string TrimmedAuthServerName => AuthServerName.Trim();
    public string TrimmedPluginDirectory => PluginDirectory.Trim();
    public string TrimmedPreLaunchCommand => PreLaunchCommand.Trim();
}
