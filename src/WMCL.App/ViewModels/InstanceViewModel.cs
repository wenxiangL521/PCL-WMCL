using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.Modpack;
using WMCL.Download;
using WMCL.Launch;
using WMCL.Launch.Java;
using Avalonia.Threading;

namespace WMCL.App.ViewModels;

public partial class InstanceViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _services;
    private readonly ServerLauncher _serverLauncher = new();

    public InstanceViewModel(AppServices services)
    {
        _services = services;
        _serverLauncher.OutputReceived += line =>
            Dispatcher.UIThread.Post(() => AppendServerOutput(line));
        _serverLauncher.Exited += code =>
            Dispatcher.UIThread.Post(() =>
            {
                IsServerRunning = false;
                AppendServerOutput($"[退出码 {code}]");
                ServerFilesStatus = $"服务端已退出（{code}）";
            });
    }

    public ObservableCollection<VersionItem> Instances { get; } = [];
    private readonly List<VersionItem> _allInstances = [];
    public ObservableCollection<string> CategoryOptions { get; } =
        ["全部", "未分类", "原版", "整合包", "生存", "创造", "测试"];
    public ObservableCollection<string> InstanceCategoryOptions { get; } =
        ["未分类", "原版", "整合包", "生存", "创造", "测试"];

    [ObservableProperty]
    private string _selectedCategoryFilter = "全部";
    public ObservableCollection<ServerEntry> Servers { get; } = [];
    public event EventHandler? InstancesChanged;
    public string GameDirectory => _services.Settings.GameDirectory;
    public bool HasInstances => Instances.Count > 0;
    public string SelectedInstanceTitle => SelectedInstance?.Id ?? "未选择实例";
    public string SelectedInstanceInfo => SelectedInstance is null
        ? "从上方选择一个实例"
        : SelectedInstance.Subtitle;
    public string SelectedInstancePath => SelectedInstance is null
        ? GameDirectory
        : Path.Combine(GameDirectory, "versions", SelectedInstance.Id);

    [ObservableProperty]
    private VersionItem? _selectedInstance;

    [ObservableProperty]
    private ServerEntry? _selectedServer;

    [ObservableProperty]
    private string _newServerName = "";

    [ObservableProperty]
    private string _newServerAddress = "";

    [ObservableProperty]
    private string _serverStatus = "选择实例后管理服务器列表";

    [ObservableProperty]
    private string _serverFilesStatus = "";

    [ObservableProperty]
    private bool _isServerRunning;

    [ObservableProperty]
    private string _serverConsole = "";

    [ObservableProperty]
    private string _serverCommand = "";

    [ObservableProperty]
    private string _serverPropertiesText = "";

    [ObservableProperty]
    private string _serverPropertiesPath = "";

    [ObservableProperty]
    private string _serverPingText = "";

    [ObservableProperty]
    private bool _isServerPinging;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _serverFavicon;

    [ObservableProperty]
    private IReadOnlyList<WMCL.Minecraft.MotdRun>? _serverMotdRuns;

    public bool HasServerMotdRuns => ServerMotdRuns is { Count: > 0 };

    partial void OnServerMotdRunsChanged(IReadOnlyList<WMCL.Minecraft.MotdRun>? value) =>
        OnPropertyChanged(nameof(HasServerMotdRuns));

    public bool HasServerFavicon => ServerFavicon is not null;

    partial void OnServerFaviconChanged(Avalonia.Media.Imaging.Bitmap? value) =>
        OnPropertyChanged(nameof(HasServerFavicon));

    [ObservableProperty]
    private string _status = "就绪";

    [ObservableProperty]
    private bool _useGlobalMemory = true;

    [ObservableProperty]
    private decimal _instanceMemoryMb = 4096;

    [ObservableProperty]
    private bool _useGlobalJava = true;

    [ObservableProperty]
    private string _instanceJavaPath = "";

    [ObservableProperty]
    private string _instanceJvmArgs = "";

    [ObservableProperty]
    private string _instanceGameArgs = "";

    [ObservableProperty]
    private bool _useGlobalWindow = true;

    [ObservableProperty]
    private decimal _instanceWidth = 854;

    [ObservableProperty]
    private decimal _instanceHeight = 480;

    [ObservableProperty]
    private string _instanceLogoPath = "";

    [ObservableProperty]
    private string _instanceDescription = "";

    [ObservableProperty]
    private string _instanceCategory = "未分类";

    [ObservableProperty]
    private bool _instanceFavorite;

    [ObservableProperty]
    private string _instanceIsolation = "跟随全局";

    public ObservableCollection<string> IsolationOptions { get; } = ["跟随全局", "隔离", "不隔离"];

    [ObservableProperty]
    private string _instanceServer = "";

    [ObservableProperty]
    private decimal _instanceServerPort = 25565;

    [ObservableProperty]
    private string _instanceAuthServer = "";

    [ObservableProperty]
    private string _instanceAuthServerName = "";

    [ObservableProperty]
    private string _instancePluginDirectory = "";

    [ObservableProperty]
    private string _instancePreLaunchCommand = "";

    [ObservableProperty]
    private bool _instancePreLaunchWait = true;

    public bool CanEditWindow => !UseGlobalWindow;

    partial void OnUseGlobalWindowChanged(bool value) =>
        OnPropertyChanged(nameof(CanEditWindow));

    [ObservableProperty]
    private string _exportPath = "";

    [ObservableProperty]
    private string _manifestStatus = "";

    [ObservableProperty]
    private bool _isRepairingManifest;

    [ObservableProperty]
    private bool _exportMods = true;

    [ObservableProperty]
    private bool _exportConfigs = true;

    [ObservableProperty]
    private bool _exportResourcePacks = true;

    [ObservableProperty]
    private bool _exportShaderPacks = true;

    [ObservableProperty]
    private bool _exportSaves = true;

    [ObservableProperty]
    private bool _exportWmcl = true;

    [ObservableProperty]
    private bool _exportOptions = true;

    [ObservableProperty]
    private string _importPath = "";

    [ObservableProperty]
    private bool _isImporting;

    public bool CanEditMemory => !UseGlobalMemory;
    public bool CanEditJava => !UseGlobalJava;

    partial void OnUseGlobalMemoryChanged(bool value) =>
        OnPropertyChanged(nameof(CanEditMemory));

    partial void OnUseGlobalJavaChanged(bool value) =>
        OnPropertyChanged(nameof(CanEditJava));

    partial void OnSelectedInstanceChanged(VersionItem? value)
    {
        OnPropertyChanged(nameof(SelectedInstanceTitle));
        OnPropertyChanged(nameof(SelectedInstanceInfo));
        OnPropertyChanged(nameof(SelectedInstancePath));
        LoadInstanceSettings(value);
        _ = LoadServersAsync(value);
    }

    public async Task LoadAsync()
    {
        Status = "正在扫描实例…";
        try
        {
            var installed = await Task.Run(() => _services.Repository.ListInstalled());
            _allInstances.Clear();
            foreach (var version in installed)
            {
                var item = new VersionItem
                {
                    Id = version.Id,
                    Type = version.Type ?? "release",
                    ReleaseTime = version.ReleaseTime,
                    IsInstalled = true
                };
                var settings = InstanceSettingsStore.Load(
                    Path.Combine(GameDirectory, "versions", version.Id));
                item.IsFavorite = settings.IsFavorite;
                item.Description = settings.Description;
                if (!string.IsNullOrWhiteSpace(settings.LogoPath) && File.Exists(settings.LogoPath))
                {
                    try { item.Logo = new Avalonia.Media.Imaging.Bitmap(settings.LogoPath); }
                    catch { item.Logo = null; }
                }
                item.Category = settings.Category;
                EnsureCategoryOptions(settings.Category);
                _allInstances.Add(item);
            }
            SortInstances();

            SelectedInstance = Instances.FirstOrDefault();
            if (SelectedInstance is not null) LoadInstanceSettings(SelectedInstance);
            Status = Instances.Count == 0 ? "还没有安装任何版本" : $"共 {Instances.Count} 个实例";
        }
        catch (Exception ex)
        {
            Status = "扫描失败：" + ex.Message;
        }
        finally
        {
            OnPropertyChanged(nameof(HasInstances));
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    private void LoadInstanceSettings(VersionItem? instance)
    {
        if (instance is null) return;

        var settings = InstanceSettingsStore.Load(
            Path.Combine(GameDirectory, "versions", instance.Id));
        UseGlobalMemory = settings.MemoryMb is null;
        InstanceMemoryMb = settings.MemoryMb ?? _services.Settings.MemoryMb;
        UseGlobalJava = string.IsNullOrWhiteSpace(settings.JavaPath);
        InstanceJavaPath = settings.JavaPath ?? "";
        InstanceJvmArgs = settings.ExtraJvmArgs;
        InstanceGameArgs = settings.ExtraGameArgs;
        UseGlobalWindow = settings.Width is null && settings.Height is null;
        InstanceWidth = settings.Width ?? _services.Settings.Width;
        InstanceHeight = settings.Height ?? _services.Settings.Height;
        InstanceDescription = settings.Description;
        InstanceCategory = string.IsNullOrWhiteSpace(settings.Category) ? "未分类" : settings.Category;
        InstanceFavorite = settings.IsFavorite;
        InstanceLogoPath = settings.LogoPath;
        InstanceIsolation = settings.IsolationMode switch
        {
            IsolationPolicy.Isolated => "隔离",
            IsolationPolicy.Global => "不隔离",
            _ => "跟随全局"
        };
        InstanceServer = settings.Server;
        InstanceServerPort = settings.Port ?? 25565;
        InstanceAuthServer = settings.AuthServer;
        InstanceAuthServerName = settings.AuthServerName;
        InstancePluginDirectory = settings.PluginDirectory;
        InstancePreLaunchCommand = settings.PreLaunchCommand;
        InstancePreLaunchWait = settings.PreLaunchWait;
    }

    private async Task LoadServersAsync(VersionItem? item)
    {
        Servers.Clear();
        SelectedServer = null;
        ServerFavicon = null;
        ServerMotdRuns = null;
        if (item is null) return;
        try
        {
            var version = await _services.Repository.GetVersionAsync(item.Id);
            var directory = IsolationPolicy.GetGameDirectory(
                GameDirectory, item.Id, version, _services.Settings.IsolationMode);
            foreach (var server in ServerListService.Load(directory))
            {
                Servers.Add(new ServerEntry
                {
                    Name = server.Name,
                    Address = server.Address,
                    Icon = server.Icon,
                    IconImage = DecodeFavicon(server.Icon)
                });
            }
            SelectedServer = Servers.FirstOrDefault();
            ServerStatus = Servers.Count == 0
                ? "服务器列表为空"
                : $"{Servers.Count} 个服务器";
        }
        catch (Exception ex)
        {
            ServerStatus = "读取服务器列表失败：" + ex.Message;
        }
    }

    private async Task SaveServersAsync()
    {
        if (SelectedInstance is null) return;
        var version = await _services.Repository.GetVersionAsync(SelectedInstance.Id);
        var directory = IsolationPolicy.GetGameDirectory(
            GameDirectory, SelectedInstance.Id, version, _services.Settings.IsolationMode);
        ServerListService.Save(directory, Servers.Select(entry => entry.ToServer()));
    }

    [RelayCommand]
    private async Task AddServerAsync()
    {
        var name = NewServerName.Trim();
        var address = NewServerAddress.Trim();
        if (address.Length == 0)
        {
            ServerStatus = "服务器地址不能为空";
            return;
        }
        if (name.Length == 0) name = address;
        Servers.Add(new ServerEntry { Name = name, Address = address });
        SelectedServer = Servers[^1];
        NewServerName = "";
        NewServerAddress = "";
        try
        {
            await SaveServersAsync();
            ServerStatus = $"已添加：{name}";
        }
        catch (Exception ex)
        {
            ServerStatus = "保存失败：" + ex.Message;
        }
    }

    private static Avalonia.Media.Imaging.Bitmap? DecodeFavicon(string? favicon)
    {
        if (string.IsNullOrWhiteSpace(favicon)) return null;
        try
        {
            var comma = favicon.IndexOf(',');
            var base64 = comma >= 0 ? favicon[(comma + 1)..] : favicon;
            var bytes = Convert.FromBase64String(base64);
            using var stream = new MemoryStream(bytes);
            return new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    private async Task RemoveServerAsync()
    {
        if (SelectedServer is null) return;
        var name = SelectedServer.Name;
        Servers.Remove(SelectedServer);
        SelectedServer = Servers.FirstOrDefault();
        try
        {
            await SaveServersAsync();
            ServerStatus = $"已删除：{name}";
        }
        catch (Exception ex)
        {
            ServerStatus = "保存失败：" + ex.Message;
        }
    }

    public void ImportServers(string file)
    {
        try
        {
            var imported = ServerListService.LoadFile(file);
            var existing = Servers.Select(server => server.Address)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var server in imported)
            {
                if (!existing.Add(server.Address)) continue;
                Servers.Add(new ServerEntry
                {
                    Name = server.Name,
                    Address = server.Address,
                    Icon = server.Icon,
                    IconImage = DecodeFavicon(server.Icon)
                });
                added++;
            }
            _ = SaveServersAsync();
            ServerStatus = $"已导入 {added} 个服务器（跳过重复 {imported.Count - added} 个）";
        }
        catch (Exception ex)
        {
            ServerStatus = "导入服务器失败：" + ex.Message;
        }
    }

    public void ExportServers(string file)
    {
        try
        {
            ServerListService.SaveFile(file, Servers.Select(entry => entry.ToServer()));
            ServerStatus = "服务器列表已导出：" + file;
        }
        catch (Exception ex)
        {
            ServerStatus = "导出服务器失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task PrepareServerFilesAsync()
    {
        if (SelectedInstance is null) return;
        ServerFilesStatus = "正在准备服务端文件…";
        try
        {
            var result = await ServerInstaller.EnsureAsync(
                GameDirectory, SelectedInstance.Id);
            await LoadServerPropertiesAsync(result.PropertiesFile);
            ServerFilesStatus = (result.Downloaded ? "已下载服务端：" : "服务端已存在：")
                                 + Path.GetFileName(result.ServerJar)
                                 + "；请编辑 eula.txt 为 true 后再手动启动。";
        }
        catch (Exception ex)
        {
            ServerFilesStatus = "准备服务端失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task StartServerAsync()
    {
        if (SelectedInstance is null || IsServerRunning) return;
        ServerFilesStatus = "正在准备服务端文件与 Java…";
        try
        {
            var server = await ServerInstaller.EnsureAsync(
                GameDirectory, SelectedInstance.Id);
            await LoadServerPropertiesAsync(server.PropertiesFile);
            var eula = await File.ReadAllTextAsync(server.EulaFile);
            if (!System.Text.RegularExpressions.Regex.IsMatch(
                    eula, @"(?im)^\s*eula\s*=\s*true\s*$"))
            {
                ServerFilesStatus =
                    "请先把 eula.txt 中的 eula 改为 true，表示你同意 Minecraft EULA。";
                return;
            }

            var java = await _services.JavaProvisioner.EnsureAsync(
                JavaLocator.InferMajorVersion(SelectedInstance.Id));
            _serverLauncher.Start(
                java.Path,
                Path.GetDirectoryName(server.ServerJar)!,
                Path.GetFileName(server.ServerJar));
            ServerConsole = "";
            IsServerRunning = true;
            ServerFilesStatus = "服务端已启动，输出如下：";
        }
        catch (Exception ex)
        {
            IsServerRunning = false;
            ServerFilesStatus = "启动服务端失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task StopServerAsync()
    {
        if (!IsServerRunning) return;
        ServerFilesStatus = "正在停止服务端…";
        var stopped = await _serverLauncher.StopAsync();
        IsServerRunning = false;
        ServerFilesStatus = stopped ? "服务端已停止" : "服务端未在超时内退出，已强制结束";
    }

    private async Task LoadServerPropertiesAsync(string path)
    {
        ServerPropertiesPath = path;
        ServerPropertiesText = File.Exists(path)
            ? await File.ReadAllTextAsync(path)
            : "";
    }

    [RelayCommand]
    private async Task SendServerCommandAsync()
    {
        var command = ServerCommand.Trim();
        if (command.Length == 0 || !IsServerRunning) return;
        try
        {
            await _serverLauncher.SendCommandAsync(command);
            ServerCommand = "";
        }
        catch (Exception ex)
        {
            ServerFilesStatus = "发送命令失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveServerPropertiesAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerPropertiesPath)) return;
        try
        {
            await File.WriteAllTextAsync(ServerPropertiesPath, ServerPropertiesText);
            ServerFilesStatus = "已保存 server.properties（运行中的服务端可能覆盖）";
        }
        catch (Exception ex)
        {
            ServerFilesStatus = "保存 server.properties 失败：" + ex.Message;
        }
    }

    private void AppendServerOutput(string line)
    {
        ServerConsole += line + Environment.NewLine;
        if (ServerConsole.Length > 100_000)
            ServerConsole = ServerConsole[^50_000..];
    }

    public void Dispose() => _serverLauncher.Dispose();

    [RelayCommand]
    private async Task PingAllServersAsync()
    {
        if (Servers.Count == 0 || IsServerPinging) return;
        IsServerPinging = true;
        ServerStatus = $"正在 Ping {Servers.Count} 个服务器…";
        try
        {
            using var gate = new SemaphoreSlim(8);
            var tasks = Servers.Select(async entry =>
            {
                await gate.WaitAsync();
                try
                {
                    var result = await ServerPingService.PingAsync(
                        entry.Address, timeout: TimeSpan.FromSeconds(4));
                    var players = result.Online is not null && result.Max is not null
                        ? $"{result.Online}/{result.Max}"
                        : "?";
                    entry.PingText = $"{players} 人 · {result.LatencyMs} ms";
                }
                catch (Exception ex)
                {
                    entry.PingText = "离线：" + ex.Message;
                }
                finally
                {
                    gate.Release();
                }
            });
            await Task.WhenAll(tasks);
            ServerStatus = "全部 Ping 完成";
        }
        finally
        {
            IsServerPinging = false;
        }
    }

    [RelayCommand]
    private async Task PingServerAsync()
    {
        if (SelectedServer is null || IsServerPinging) return;
        IsServerPinging = true;
        ServerFavicon = null;
        ServerMotdRuns = null;
        ServerPingText = "正在 Ping…";
        try
        {
            var result = await ServerPingService.PingAsync(
                SelectedServer.Address, timeout: TimeSpan.FromSeconds(5));
            var players = result.Online is not null && result.Max is not null
                ? $"{result.Online}/{result.Max} 人"
                : "人数未知";
            var version = string.IsNullOrWhiteSpace(result.Version) ? "版本未知" : result.Version;
            ServerMotdRuns = result.MotdRuns;
            var sample = result.PlayerSample is { Count: > 0 }
                ? " · 玩家：" + string.Join(", ", result.PlayerSample.Take(6))
                : "";
            ServerPingText = $"{players} · {version} · {result.LatencyMs} ms{sample}";
            if (SelectedServer is not null)
                SelectedServer.PingText = $"{players} · {result.LatencyMs} ms";
            ServerFavicon = DecodeFavicon(result.Favicon);
            if (!string.IsNullOrWhiteSpace(result.Favicon) && SelectedServer is not null)
            {
                SelectedServer.Icon = result.Favicon;
                SelectedServer.IconImage = DecodeFavicon(result.Favicon);
                await SaveServersAsync();
                ServerPingText += " · 图标已保存";
            }
        }
        catch (Exception ex)
        {
            ServerPingText = "Ping 失败：" + ex.Message;
        }
        finally
        {
            IsServerPinging = false;
        }
    }

    [RelayCommand]
    private async Task SaveServersNowAsync()
    {
        try
        {
            await SaveServersAsync();
            ServerStatus = "服务器列表已保存";
        }
        catch (Exception ex)
        {
            ServerStatus = "保存失败：" + ex.Message;
        }
    }

    private async Task RepairManagedDownloadsAsync(string instanceId)
    {
        var manifest = InstallManifestStore.Load(GameDirectory, instanceId);
        if (manifest is null) return;
        var items = new List<DownloadItem>();
        foreach (var file in manifest.ManagedFiles)
        {
            if (string.IsNullOrWhiteSpace(file.Url)) continue;
            var fullPath = Path.Combine(
                GameDirectory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                await using var stream = File.OpenRead(fullPath);
                var hash = Convert.ToHexStringLower(
                    await System.Security.Cryptography.SHA1.HashDataAsync(stream));
                if (string.Equals(hash, file.Sha1, StringComparison.OrdinalIgnoreCase)) continue;
            }
            items.Add(new DownloadItem
            {
                Destination = fullPath,
                Urls = [file.Url],
                Sha1 = file.Sha1
            });
        }
        if (items.Count == 0) return;
        var progress = new Progress<DownloadProgress>(update =>
            ManifestStatus = $"修复受管文件 {(update.Percent ?? 0) * 100:0}% {update.CurrentFile}");
        await new DownloadManager().DownloadAsync(
            items,
            new DownloadOptions { MaxConcurrency = 4 },
            progress);
    }

    [RelayCommand]
    private async Task RepairManifestAsync()
    {
        if (SelectedInstance is null || IsRepairingManifest) return;
        IsRepairingManifest = true;
        ManifestStatus = "正在修复基础文件…";
        try
        {
            var progress = new Progress<JobProgress>(update =>
                ManifestStatus = $"{update.Stage} {(update.Percent ?? 0) * 100:0}%");
            await _services.Installer.InstallAsync(
                SelectedInstance.Id,
                new InstallOptions
                {
                    Mirror = _services.Mirror,
                    MaxConcurrency = 16,
                    VerifyExisting = true
                },
                progress);
            ManifestStatus = "基础文件修复完成，正在修复受管 Mod…";
            await RepairManagedDownloadsAsync(SelectedInstance.Id);
            try
            {
                var overrideCount = await OverridesRepair.RepairAsync(
                    GameDirectory, SelectedInstance.Id);
                if (overrideCount > 0)
                    ManifestStatus = $"已重新解压 {overrideCount} 个 overrides 文件";
            }
            catch (FileNotFoundException)
            {
                ManifestStatus = "原始整合包已不存在，跳过 overrides 修复";
            }
            catch (InvalidOperationException)
            {
                // 非整合包/旧清单没有 overrides 信息
            }
            var result = await InstallManifestStore.VerifyAsync(GameDirectory, SelectedInstance.Id);
            ManifestStatus = result.IsHealthy
                ? $"修复完成，清单正常：{result.Checked} 个受管文件"
                : $"修复完成，但仍有异常：缺失 {result.Missing}，损坏 {result.Corrupted}";
        }
        catch (Exception ex)
        {
            ManifestStatus = "修复失败：" + ex.Message;
        }
        finally
        {
            IsRepairingManifest = false;
        }
    }

    [RelayCommand]
    private async Task VerifyManifestAsync()
    {
        if (SelectedInstance is null) return;
        ManifestStatus = "正在校验安装清单…";
        try
        {
            var result = await InstallManifestStore.VerifyAsync(GameDirectory, SelectedInstance.Id);
            ManifestStatus = result.Checked == 0 && result.Problems.Count > 0
                ? result.Problems[0]
                : result.IsHealthy
                    ? $"清单正常：{result.Checked} 个受管文件"
                    : $"清单异常：缺失 {result.Missing}，损坏 {result.Corrupted}\n"
                      + string.Join(Environment.NewLine, result.Problems.Take(5));
        }
        catch (Exception ex)
        {
            ManifestStatus = "校验失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void SaveInstanceSettings()
    {
        if (SelectedInstance is null) return;

        InstanceSettingsStore.Save(
            Path.Combine(GameDirectory, "versions", SelectedInstance.Id),
            new InstanceSettings
            {
                MemoryMb = UseGlobalMemory ? null : (int)InstanceMemoryMb,
                JavaPath = UseGlobalJava || string.IsNullOrWhiteSpace(InstanceJavaPath)
                    ? null
                    : InstanceJavaPath.Trim(),
                ExtraJvmArgs = InstanceJvmArgs.Trim(),
                ExtraGameArgs = InstanceGameArgs.Trim(),
                Width = UseGlobalWindow ? null : (int)InstanceWidth,
                Height = UseGlobalWindow ? null : (int)InstanceHeight,
                Description = InstanceDescription.Trim(),
                Category = InstanceCategory == "未分类" ? "" : InstanceCategory,
                LogoPath = InstanceLogoPath.Trim(),
                IsFavorite = InstanceFavorite,
                IsolationMode = InstanceIsolation switch
                {
                    "隔离" => IsolationPolicy.Isolated,
                    "不隔离" => IsolationPolicy.Global,
                    _ => ""
                },
                Server = InstanceServer.Trim(),
                Port = string.IsNullOrWhiteSpace(InstanceServer)
                    ? null
                    : (int)InstanceServerPort,
                AuthServer = InstanceAuthServer.Trim(),
                AuthServerName = InstanceAuthServerName.Trim(),
                PluginDirectory = InstancePluginDirectory.Trim(),
                PreLaunchCommand = InstancePreLaunchCommand.Trim(),
                PreLaunchWait = InstancePreLaunchWait
            });
        SelectedInstance.Description = InstanceDescription.Trim();
        SelectedInstance.Category = InstanceCategory == "未分类" ? "" : InstanceCategory;
        EnsureCategoryOptions(SelectedInstance.Category);
        SelectedInstance.IsFavorite = InstanceFavorite;
        if (File.Exists(InstanceLogoPath))
        {
            try { SelectedInstance.Logo = new Avalonia.Media.Imaging.Bitmap(InstanceLogoPath); }
            catch { SelectedInstance.Logo = null; }
        }
        else
        {
            SelectedInstance.Logo = null;
        }
        SortInstances();
        Status = $"已保存 {SelectedInstance.Id} 的实例设置";
    }

    [RelayCommand]
    private void ResetInstanceSettings()
    {
        if (SelectedInstance is null) return;
        InstanceSettingsStore.Reset(
            Path.Combine(GameDirectory, "versions", SelectedInstance.Id));
        LoadInstanceSettings(SelectedInstance);
        Status = $"已重置 {SelectedInstance.Id} 的实例设置";
    }

    [RelayCommand]
    private async Task CloneInstanceAsync()
    {
        if (SelectedInstance is null) return;
        var name = await DialogService.PromptAsync(
            "克隆实例", "输入新实例名：", SelectedInstance.Id + "-copy");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var id = InstanceManager.Clone(GameDirectory, SelectedInstance.Id, name);
            await LoadAsync();
            SelectedInstance = Instances.FirstOrDefault(version =>
                string.Equals(version.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? SelectedInstance;
            Status = $"已克隆为：{id}";
            InstancesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = "克隆失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task RenameInstanceAsync()
    {
        if (SelectedInstance is null) return;
        var name = await DialogService.PromptAsync(
            "重命名实例", "输入新的实例名：", SelectedInstance.Id);
        if (string.IsNullOrWhiteSpace(name) ||
            string.Equals(name, SelectedInstance.Id, StringComparison.Ordinal))
            return;

        try
        {
            var oldId = SelectedInstance.Id;
            InstanceManager.Rename(GameDirectory, oldId, name);
            await LoadAsync();
            SelectedInstance = Instances.FirstOrDefault(version =>
                string.Equals(version.Id, name, StringComparison.OrdinalIgnoreCase))
                ?? SelectedInstance;
            Status = $"已重命名：{oldId} → {name}";
            InstancesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = "重命名失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteInstanceAsync()
    {
        if (SelectedInstance is null) return;
        var id = SelectedInstance.Id;
        if (!await DialogService.ConfirmAsync(
                "删除实例",
                $"确定删除实例 {id}？" + Environment.NewLine +
                "版本目录会被删除，此操作不可恢复。"))
            return;

        try
        {
            InstanceManager.Delete(GameDirectory, id);
            await LoadAsync();
            Status = $"已删除实例：{id}";
            InstancesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = "删除失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ExportModpackAsync()
    {
        if (SelectedInstance is null) return;
        Status = $"正在导出整合包 {SelectedInstance.Id}…";
        try
        {
            var result = await ModpackExporter.ExportAsync(
                GameDirectory, SelectedInstance.Id,
                outputPath: ExportPath.Trim().Length > 0 ? ExportPath.Trim() : null,
                isolationMode: _services.Settings.IsolationMode);
            Status = $"已导出 {result.FileCount} 个文件：{result.ArchivePath}";
        }
        catch (Exception ex)
        {
            Status = "导出整合包失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ImportInstanceAsync()
    {
        if (IsImporting || string.IsNullOrWhiteSpace(ImportPath)) return;

        IsImporting = true;
        Status = "正在导入实例…";
        try
        {
            var result = await InstanceImporter.ImportAsync(
                GameDirectory, ImportPath.Trim());
            await LoadAsync();
            SelectedInstance = Instances.FirstOrDefault(
                version => string.Equals(version.Id, result.Id, StringComparison.OrdinalIgnoreCase))
                ?? SelectedInstance;
            Status = $"已导入 {result.FileCount} 个文件：{result.VersionDirectory}";
        }
        catch (Exception ex)
        {
            Status = "导入失败：" + ex.Message;
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    private async Task ExportInstanceAsync()
    {
        if (SelectedInstance is null) return;
        Status = $"正在导出 {SelectedInstance.Id}…";
        try
        {
            var result = await InstanceExporter.ExportAsync(
                GameDirectory, SelectedInstance.Id,
                new InstanceExportOptions(
                    ExportMods, ExportConfigs, ExportResourcePacks,
                    ExportShaderPacks, ExportSaves, ExportWmcl, ExportOptions),
                ExportPath.Trim().Length > 0 ? ExportPath.Trim() : null,
                isolationMode: _services.Settings.IsolationMode);
            Status = $"已导出 {result.FileCount} 个文件：{result.ArchivePath}";
        }
        catch (Exception ex)
        {
            Status = "导出失败：" + ex.Message;
        }
    }

    partial void OnSelectedCategoryFilterChanged(string value) => ApplyInstanceFilter();

    private void EnsureCategoryOptions(string? category)
    {
        if (string.IsNullOrWhiteSpace(category) || category == "未分类") return;
        if (!InstanceCategoryOptions.Contains(category)) InstanceCategoryOptions.Add(category);
        if (!CategoryOptions.Contains(category)) CategoryOptions.Add(category);
    }

    private void SortInstances()
    {
        var ordered = _allInstances
            .OrderByDescending(item => item.IsFavorite)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _allInstances.Clear();
        _allInstances.AddRange(ordered);
        ApplyInstanceFilter();
    }

    private void ApplyInstanceFilter()
    {
        var selected = SelectedInstance?.Id;
        var filter = SelectedCategoryFilter;
        Instances.Clear();
        foreach (var item in _allInstances)
        {
            var category = string.IsNullOrWhiteSpace(item.Category) ? "未分类" : item.Category;
            if (filter != "全部" && category != filter) continue;
            Instances.Add(item);
        }
        OnPropertyChanged(nameof(HasInstances));
        if (selected is not null)
            SelectedInstance = Instances.FirstOrDefault(item => item.Id == selected)
                              ?? Instances.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var path = SelectedInstance is null
            ? GameDirectory
            : Path.Combine(GameDirectory, "versions", SelectedInstance.Id);
        OpenPath(Directory.Exists(path) ? path : GameDirectory);
    }

    [RelayCommand]
    private void OpenGameFolder() => OpenPath(GameDirectory);

    private static void OpenPath(string path)
    {
        try
        {
            ProcessStartInfo startInfo;
            if (OperatingSystem.IsWindows())
                startInfo = new ProcessStartInfo("explorer.exe", path);
            else if (OperatingSystem.IsMacOS())
                startInfo = new ProcessStartInfo("open", path);
            else
                startInfo = new ProcessStartInfo("xdg-open", path);

            startInfo.UseShellExecute = false;
            Process.Start(startInfo);
        }
        catch
        {
            // 打开目录失败不阻塞 UI
        }
    }
}
