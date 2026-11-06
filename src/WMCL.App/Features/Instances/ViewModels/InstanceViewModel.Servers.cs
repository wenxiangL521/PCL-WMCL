using WMCL.Core;
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.Modpack;
using WMCL.Download;
using WMCL.Launch;
using WMCL.Launch.Java;
using Avalonia.Threading;

namespace WMCL.App.Features.Instances;

public partial class InstanceViewModel
{
    private async Task LoadServersAsync(VersionItem? item)
    {
        var request = ++_serverLoadRequest;
        var root = GameDirectory;
        Servers.Clear();
        SelectedServer = null;
        ServerFavicon = null;
        ServerMotdRuns = null;
        if (item is null) return;
        try
        {
            var version = await new VersionRepository(root).GetLocalVersionAsync(item.Id);
            if (request != _serverLoadRequest || GameDirectory != root || SelectedInstance?.Id != item.Id) return;
            var directory = IsolationPolicy.GetGameDirectory(
                root, item.Id, version, _services.Settings.IsolationMode);
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
            if (request == _serverLoadRequest && GameDirectory == root && SelectedInstance?.Id == item.Id)
                ServerStatus = "读取服务器列表失败：" + ex.Message;
        }
    }

    private async Task SaveServersAsync()
    {
        if (SelectedInstance is null) return;
        var root = GameDirectory;
        var id = SelectedInstance.Id;
        var servers = Servers.Select(entry => entry.ToServer()).ToArray();
        using var operation = _services.GameOperations.Acquire(root, "保存服务器列表");
        var version = await new VersionRepository(root).GetVersionAsync(id);
        var directory = IsolationPolicy.GetGameDirectory(
            root, id, version, _services.Settings.IsolationMode);
        ServerListService.Save(directory, servers);
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

    private static Avalonia.Media.Imaging.Bitmap? DecodeFavicon(string? favicon) =>
        ImageDecoder.FromFavicon(favicon);

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
            _ = SaveImportedServersAsync(added, imported.Count - added);
        }
        catch (Exception ex)
        {
            ServerStatus = "导入服务器失败：" + ex.Message;
        }
    }

    private async Task SaveImportedServersAsync(int added, int skipped)
    {
        try
        {
            await SaveServersAsync();
            ServerStatus = $"已导入 {added} 个服务器（跳过重复 {skipped} 个）";
        }
        catch (Exception ex) { ServerStatus = "导入服务器失败：" + ex.Message; }
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
        var root = GameDirectory;
        var id = SelectedInstance.Id;
        ServerFilesStatus = "正在准备服务端文件…";
        try
        {
            using var operation = _services.GameOperations.Acquire(root, "准备服务端文件");
            var result = await ServerInstaller.EnsureAsync(
                root, id);
            await LoadServerPropertiesAsync(root, result.PropertiesFile, id);
            ServerFilesStatus = (result.Downloaded ? "已下载服务端：" : "服务端已存在：")
                                 + Path.GetFileName(result.ServerJar)
                                 + "；请编辑 eula.txt 为 true 后再手动启动。";
        }
        catch (Exception ex)
        {
            ServerFilesStatus = "准备服务端失败：" + ex.Message;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task StartServerAsync(CancellationToken ct)
    {
        if (SelectedInstance is null || IsServerRunning || _serverOperation is not null) return;
        var root = GameDirectory;
        var id = SelectedInstance.Id;
        ServerFilesStatus = "正在准备服务端文件与 Java…";
        Interlocked.Increment(ref _serverSession);
        try
        {
            _serverOperation = _services.GameOperations.Acquire(root, "运行服务端 " + id);
            var server = await ServerInstaller.EnsureAsync(
                root, id, ct: ct);
            await LoadServerPropertiesAsync(root, server.PropertiesFile, id);
            var eula = await File.ReadAllTextAsync(server.EulaFile);
            if (!System.Text.RegularExpressions.Regex.IsMatch(
                    eula, @"(?im)^\s*eula\s*=\s*true\s*$"))
            {
                ServerFilesStatus =
                    "请先把 eula.txt 中的 eula 改为 true，表示你同意 Minecraft EULA。";
                return;
            }

            var versionJson = await _services.Repository.GetVersionAsync(id);
            var requirement = JavaRequirement.ForVersion(versionJson);
            var java = await _services.JavaProvisioner.EnsureAsync(requirement, ct: ct);
            ct.ThrowIfCancellationRequested();
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
        finally
        {
            if (!_serverLauncher.IsRunning)
            {
                Interlocked.Exchange(ref _serverOperation, null)?.Dispose();
                IsServerRunning = false;
            }
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

    private async Task LoadServerPropertiesAsync(string root, string path, string instanceId)
    {
        var text = File.Exists(path) ? await File.ReadAllTextAsync(path) : "";
        _serverPropertiesRoot = root;
        _serverPropertiesInstanceId = instanceId;
        ServerPropertiesPath = path;
        ServerPropertiesText = text;
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
        var root = _serverPropertiesRoot;
        var path = ServerPropertiesPath;
        var text = ServerPropertiesText;
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (SelectedInstance is not { } current || current.Id != _serverPropertiesInstanceId ||
                root != GameDirectory)
                throw new InvalidOperationException("游戏目录已切换，请重新准备服务端文件。");
            using var operation = _services.GameOperations.Acquire(root, "保存服务端设置");
            await File.WriteAllTextAsync(path, text);
            ServerFilesStatus = "已保存 server.properties";
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
                    var modCount = result.ModList?.Count ?? 0;
                    entry.PingText = $"{players} 人 · {result.LatencyMs} ms"
                        + (modCount > 0 ? $" · {modCount} mods" : "");
                    if (ReferenceEquals(SelectedServer, entry))
                    {
                        ServerModList = result.ModList;
                        ServerModInfoType = result.ModInfoType ?? "";
                        ServerMotdRuns = result.MotdRuns;
                    }
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
        ServerModList = null;
        ServerModInfoType = "";
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
            ServerModList = result.ModList;
            ServerModInfoType = result.ModInfoType ?? "";
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
}
