using System.Text;
using System.Text.Json;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Core.Update;
using WMCL.Download;
using WMCL.Launch;
using WMCL.Launch.Auth;
using WMCL.Launch.Java;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Modrinth;
using WMCL.Modding.Modpack;

namespace WMCL.Cli.Commands;

/// <summary>启动、账号、服务器、更新与版本列表命令。</summary>
public static class GameCommands
{
    public static async Task<int> LaunchAsync(
        IReadOnlyList<string> positional,
        IReadOnlyDictionary<string, string> options,
        CancellationToken ct)
    {
        if (positional.Count == 0)
        {
            Console.Error.WriteLine("缺少版本号，例如：wmcl launch 1.21.1 --offline Steve");
            return 1;
        }

        var versionId = positional[0];
        var gameDirectory = CliHelpers.GetGameDirectory(options);
        var repository = new VersionRepository(gameDirectory);
        if (!options.ContainsKey("dry-run"))
        {
            var updater = new McbbsModpackUpdater(repository,
                new CurseForgeClient(options.GetValueOrDefault("api-key") ?? WmclEnvironment.Get("CURSEFORGE_API_KEY")));
            await updater.PrepareLaunchAsync(versionId, new SyncProgress<JobProgress>(CliHelpers.PrintProgress), ct);
            ct.ThrowIfCancellationRequested();
        }
        var preferences = new LaunchPreferences
        {
            IsolationMode = CliHelpers.GetIsolationMode(options),
            IncludeInstanceJvmTuning = false
        };
        var overrides = new LaunchOverrides
        {
            JavaPath = options.GetValueOrDefault("java"),
            MemoryMb = options.ContainsKey("memory") ? CliHelpers.GetInt(options, "memory", 4096) : null,
            Width = options.ContainsKey("width") ? CliHelpers.GetInt(options, "width", 854) : null,
            Height = options.ContainsKey("height") ? CliHelpers.GetInt(options, "height", 480) : null,
            Server = options.GetValueOrDefault("server"),
            Port = options.ContainsKey("port") ? CliHelpers.GetInt(options, "port", 25565) : null
        };
        var javaLocator = new JavaLocator();
        var preparation = new LaunchPreparationService(javaLocator, new JavaProvisioner(javaLocator));
        var prepared = await preparation.PrepareAsync(gameDirectory, versionId, preferences, overrides,
            new SyncProgress<JobProgress>(CliHelpers.PrintProgress), ct);
        var instanceDirectory = prepared.GameDirectory;
        var java = prepared.Java;
        if (prepared.JavaWarning is { } javaWarning)
            Console.Error.WriteLine("警告：" + javaWarning);
        if (!string.IsNullOrWhiteSpace(prepared.Manifest?.McbbsAuthlibInjectorServer))
            throw new InvalidOperationException(
                "该整合包要求第三方认证服务器，CLI 启动暂不支持 authlib-injector，请使用桌面启动页登录后启动。");

        var playerName = options.GetValueOrDefault("offline", "Player");

        AccountInfo account;
        if (options.ContainsKey("microsoft"))
        {
            var stored = MicrosoftAccountStore.Load()
                         ?? throw new InvalidOperationException(
                             "尚未登录微软账号，请先运行：wmcl login microsoft --client-id <Client ID>");
            if (stored.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("微软账号的 AccessToken 已过期，请重新登录。");
            account = AccountInfo.FromMicrosoft(stored);
            playerName = stored.Name;
        }
        else
        {
            account = AccountInfo.Offline(playerName);
        }
        var request = prepared.CreateRequest(account, preferences, overrides);

        var logPath = Path.Combine(WmclPaths.LogsDirectory,
            $"game-{versionId}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        await using var writer = new StreamWriter(logPath, append: false, Encoding.UTF8) { AutoFlush = true };

        Console.WriteLine($"启动 {versionId}");
        Console.WriteLine($"游戏目录：{instanceDirectory}");
        Console.WriteLine($"Minecraft 根目录：{gameDirectory}");
        Console.WriteLine($"Java：{java.Display}");
        Console.WriteLine($"玩家：{playerName}（{(options.ContainsKey("microsoft") ? "微软" : "离线")}）");
        Console.WriteLine($"日志：{logPath}");
        Console.WriteLine();

        var launcher = new GameLauncher();

        if (options.ContainsKey("dry-run"))
        {
            var plan = LaunchPlanBuilder.Build(request);
            Console.WriteLine(plan.Preview);
            return 0;
        }

        Console.WriteLine("启动前校验游戏文件…");
        var ruleContext = RuleContext.CreateDefault(new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["is_demo_user"] = false,
            ["has_custom_resolution"] = request.Width is not null && request.Height is not null,
            ["has_quick_plays_support"] = false,
            ["is_quick_play_singleplayer"] = false,
            ["is_quick_play_multiplayer"] = false,
            ["is_quick_play_realms"] = false
        });
        await new GameFileVerificationService(repository, ruleContext: ruleContext).EnsureAsync(versionId,
            new InstallOptions
            {
                Mirror = CliHelpers.ParseMirror(options.GetValueOrDefault("mirror")),
                MaxConcurrency = Math.Clamp(CliHelpers.GetInt(options, "threads", 16), 1, 64),
                VerifyExisting = true
            }, new SyncProgress<JobProgress>(CliHelpers.PrintProgress), ct);
        ct.ThrowIfCancellationRequested();

        var exitCode = await launcher.LaunchAsync(request, new SyncProgress<string>(line =>
        {
            Console.WriteLine(line);
            writer.WriteLine(line);
        }), ct);

        Console.WriteLine();
        Console.WriteLine($"游戏已退出，退出码：{exitCode}");
        return exitCode;
    }


    public static async Task<int> CurseForgeAsync(
        IReadOnlyList<string> positional,
        IReadOnlyDictionary<string, string> options,
        CancellationToken ct)
    {
        if (positional.Count == 0)
        {
            Console.Error.WriteLine(
                "用法：wmcl curseforge search <关键词> --game <MC版本> --loader <加载器>；wmcl curseforge install <modId> ...");
            return 1;
        }

        var subcommand = positional[0].ToLowerInvariant();
        var gameVersion = options.GetValueOrDefault("game");
        var loader = options.GetValueOrDefault("loader") ?? "fabric";
        var gameDirectory = CliHelpers.GetGameDirectory(options);
        var instance = options.GetValueOrDefault("instance");
        var modsDirectory = Path.Combine(gameDirectory, "mods");

        if (!string.IsNullOrWhiteSpace(instance))
        {
            var repository = new VersionRepository(gameDirectory);
            var version = await repository.GetVersionAsync(instance, ct);
            gameVersion ??= MinecraftVersionUtil.GetBaseGameVersion(instance, version);
            loader = MinecraftVersionUtil.GetLoader(version);
            modsDirectory = Path.Combine(
                IsolationPolicy.GetGameDirectory(
                    gameDirectory, instance, version, CliHelpers.GetIsolationMode(options)), "mods");
        }

        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            Console.Error.WriteLine("缺少 --game 或 --instance");
            return 1;
        }

        var apiKey = options.GetValueOrDefault("api-key");
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = WmclEnvironment.Get("CURSEFORGE_API_KEY") ?? "";
        var client = new CurseForgeClient(apiKey);

        if (subcommand == "search")
        {
            var query = positional.Count > 1 ? string.Join(' ', positional.Skip(1)) : "";
            var result = await client.SearchAsync(query, gameVersion, loader, ct: ct);
            Console.WriteLine($"共 {result.TotalCount} 个结果：");
            foreach (var project in result.Projects.Take(20))
                Console.WriteLine($"  {project.Id,10}  {project.Name}  ({project.Slug})");
            return 0;
        }

        if (subcommand == "install")
        {
            if (positional.Count < 2 || !long.TryParse(positional[1], out var modId))
            {
                Console.Error.WriteLine("缺少 CurseForge Mod ID");
                return 1;
            }

            var progress = new SyncProgress<JobProgress>(CliHelpers.PrintProgress);
            var result = await client.InstallAsync(
                modId, gameVersion, loader, modsDirectory, progress, ct);
            Console.WriteLine();
            Console.WriteLine(result.Files.Count == 0
                ? "没有需要安装的文件（可能已存在）。"
                : $"安装 CurseForge Mod：{string.Join("，", result.Files)}");
            return 0;
        }

        Console.Error.WriteLine($"未知子命令：{subcommand}");
        return 1;
    }


    public static async Task<int> LoginAsync(
        IReadOnlyList<string> positional,
        IReadOnlyDictionary<string, string> options,
        CancellationToken ct)
    {
        if (positional.Count == 0)
        {
            Console.Error.WriteLine("用法：wmcl login microsoft --client-id <ID> | wmcl login status | wmcl login logout");
            return 1;
        }

        var subcommand = positional[0].ToLowerInvariant();

        if (subcommand == "status")
        {
            var stored = MicrosoftAccountStore.Load();
            if (stored is null)
            {
                Console.WriteLine("未登录微软账号。");
                return 0;
            }

            Console.WriteLine($"已登录：{stored.Name} ({stored.Uuid})");
            Console.WriteLine($"AccessToken 过期时间：{stored.ExpiresAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}");
            return 0;
        }

        if (subcommand == "logout")
        {
            MicrosoftAccountStore.Clear();
            Console.WriteLine("已清除微软账号缓存。");
            return 0;
        }

        if (subcommand == "microsoft")
        {
            var clientId = options.GetValueOrDefault("client-id");
            if (string.IsNullOrWhiteSpace(clientId))
            {
                Console.Error.WriteLine("缺少 --client-id。请先在 Azure 注册公共客户端应用。");
                return 1;
            }

            var service = new MicrosoftAuthService();
            var progress = new SyncProgress<MicrosoftAuthProgress>(update =>
            {
                if (!string.IsNullOrEmpty(update.UserCode))
                {
                    Console.WriteLine();
                    Console.WriteLine($"请在浏览器打开：{update.VerificationUri}");
                    Console.WriteLine($"输入代码：{update.UserCode}");
                    Console.WriteLine();
                }

                Console.WriteLine(update.Stage + (string.IsNullOrWhiteSpace(update.Message)
                    ? ""
                    : $"（{update.Message}）"));
            });

            var account = await service.LoginAsync(clientId, progress, ct);
            Console.WriteLine();
            Console.WriteLine($"登录成功：{account.Name} ({account.Uuid})");
            return 0;
        }

        Console.Error.WriteLine($"未知子命令：{subcommand}");
        return 1;
    }


    public static async Task<int> ServerAsync(
        IReadOnlyList<string> positional,
        IReadOnlyDictionary<string, string> options,
        CancellationToken ct)
    {
        if (positional.Count < 1)
        {
            Console.Error.WriteLine("用法：wmcl server ping <地址:端口> | wmcl server list <实例>");
            return 1;
        }

        var subcommand = positional[0].ToLowerInvariant();
        if (subcommand == "ping")
        {
            if (positional.Count < 2)
            {
                Console.Error.WriteLine("用法：wmcl server ping <地址:端口>");
                return 1;
            }
            try
            {
                var result = await ServerPingService.PingAsync(
                    positional[1], timeout: TimeSpan.FromSeconds(8), ct: ct);
                if (options.GetValueOrDefault("json") == "true")
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        version = result.Version,
                        online = result.Online,
                        max = result.Max,
                        latencyMs = result.LatencyMs,
                        motd = result.Motd,
                        players = result.PlayerSample
                    }));
                    return 0;
                }
                Console.WriteLine($"版本：{result.Version ?? "未知"}");
                Console.WriteLine($"玩家：{result.Online?.ToString() ?? "?"}/{result.Max?.ToString() ?? "?"}");
                Console.WriteLine($"延迟：{result.LatencyMs} ms");
                Console.WriteLine($"MOTD：{result.Motd ?? ""}");
                if (result.PlayerSample is { Count: > 0 })
                    Console.WriteLine($"样本：{string.Join(", ", result.PlayerSample.Take(10))}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Ping 失败：" + ex.Message);
                return 1;
            }
        }

        if (subcommand == "list")
        {
            if (positional.Count < 2)
            {
                Console.Error.WriteLine("用法：wmcl server list <实例>");
                return 1;
            }
            var gameRoot = CliHelpers.GetGameDirectory(options);
            var repository = new VersionRepository(gameRoot);
            var instance = positional[1];
            try
            {
                var version = await repository.GetVersionAsync(instance, ct);
                var directory = IsolationPolicy.GetGameDirectory(
                    gameRoot, instance, version, CliHelpers.GetIsolationMode(options));
                var servers = ServerListService.Load(directory);
                if (servers.Count == 0)
                {
                    Console.WriteLine("服务器列表为空。");
                    return 0;
                }
                if (options.GetValueOrDefault("json") == "true")
                {
                    Console.WriteLine(JsonSerializer.Serialize(
                        servers.Select(server => new
                        {
                            name = server.Name,
                            address = server.Address,
                            hasIcon = !string.IsNullOrWhiteSpace(server.Icon)
                        })));
                    return 0;
                }
                foreach (var server in servers)
                    Console.WriteLine($"{server.Name}\t{server.Address}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("读取服务器列表失败：" + ex.Message);
                return 1;
            }
        }

        if (subcommand == "edit")
        {
            if (positional.Count < 3)
            {
                Console.Error.WriteLine("用法：wmcl server edit <实例> <名称或地址> [--name <新名称>] [--address <新地址>]");
                return 1;
            }
            var gameRoot = CliHelpers.GetGameDirectory(options);
            var repository = new VersionRepository(gameRoot);
            var instance = positional[1];
            try
            {
                var version = await repository.GetVersionAsync(instance, ct);
                var directory = IsolationPolicy.GetGameDirectory(
                    gameRoot, instance, version, CliHelpers.GetIsolationMode(options));
                var servers = ServerListService.Load(directory).ToList();
                var target = positional[2];
                var index = servers.FindIndex(server =>
                    string.Equals(server.Name, target, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(server.Address, target, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    Console.Error.WriteLine("没有找到匹配的服务器：" + target);
                    return 1;
                }
                var current = servers[index];
                var name = options.GetValueOrDefault("name") ?? current.Name;
                var address = options.GetValueOrDefault("address") ?? current.Address;
                servers[index] = current with { Name = name, Address = address };
                ServerListService.Save(directory, servers);
                Console.WriteLine($"已更新：{name} -> {address}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("修改服务器列表失败：" + ex.Message);
                return 1;
            }
        }

        if (subcommand is "add" or "remove")
        {
            if (positional.Count < 3)
            {
                Console.Error.WriteLine(subcommand == "add"
                    ? "用法：wmcl server add <实例> <名称> <地址:端口>"
                    : "用法：wmcl server remove <实例> <名称或地址>");
                return 1;
            }
            var gameRoot = CliHelpers.GetGameDirectory(options);
            var repository = new VersionRepository(gameRoot);
            var instance = positional[1];
            try
            {
                var version = await repository.GetVersionAsync(instance, ct);
                var directory = IsolationPolicy.GetGameDirectory(
                    gameRoot, instance, version, CliHelpers.GetIsolationMode(options));
                var servers = ServerListService.Load(directory).ToList();
                if (subcommand == "add")
                {
                    if (positional.Count < 4)
                    {
                        Console.Error.WriteLine("用法：wmcl server add <实例> <名称> <地址:端口>");
                        return 1;
                    }
                    servers.Add(new MinecraftServer(positional[2], positional[3]));
                    Console.WriteLine($"已添加：{positional[2]} -> {positional[3]}");
                }
                else
                {
                    var target = positional[2];
                    var removed = servers.RemoveAll(server =>
                        string.Equals(server.Name, target, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(server.Address, target, StringComparison.OrdinalIgnoreCase));
                    if (removed == 0)
                    {
                        Console.Error.WriteLine("没有找到匹配的服务器：" + target);
                        return 1;
                    }
                    Console.WriteLine($"已删除 {removed} 个服务器。");
                }
                ServerListService.Save(directory, servers);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("修改服务器列表失败：" + ex.Message);
                return 1;
            }
        }

        Console.Error.WriteLine("未知 server 子命令：" + subcommand);
        return 1;
    }


    public static async Task<int> UpdateAsync(
        IReadOnlyList<string> positional,
        IReadOnlyDictionary<string, string> options,
        CancellationToken ct)
    {
        if (positional.Count < 2)
        {
            Console.Error.WriteLine("用法：wmcl update check|download <latest.json 地址>");
            return 1;
        }

        var subcommand = positional[0].ToLowerInvariant();
        var url = positional[1];

        if (subcommand == "check")
        {
            var result = await UpdateService.CheckAsync(url, AppInfo.Version, ct);
            if (result.Status == UpdateCheckStatus.UpToDate)
            {
                Console.WriteLine($"已是最新版本（v{AppInfo.Version}）。");
                return 0;
            }
            if (result.Status != UpdateCheckStatus.UpdateAvailable || result.Update is null)
            {
                Console.Error.WriteLine("更新检查失败：" + (result.Error ?? result.Status.ToString()));
                return 2;
            }

            Console.WriteLine($"发现新版本 v{result.Update.Version}");
            if (!string.IsNullOrWhiteSpace(result.Update.Notes)) Console.WriteLine(result.Update.Notes);
            return 0;
        }

        if (subcommand == "download")
        {
            var result = await UpdateService.CheckAsync(url, AppInfo.Version, ct);
            if (result.Status == UpdateCheckStatus.UpToDate)
            {
                Console.WriteLine($"已是最新版本（v{AppInfo.Version}）。");
                return 0;
            }
            if (result.Status != UpdateCheckStatus.UpdateAvailable || result.Update is null)
            {
                Console.Error.WriteLine("更新检查失败：" + (result.Error ?? result.Status.ToString()));
                return 2;
            }

            var progress = new SyncProgress<JobProgress>(CliHelpers.PrintProgress);
            var path = await UpdateService.DownloadAsync(result.Update, progress, ct);
            Console.WriteLine();
            Console.WriteLine($"更新包已下载并校验：{path}");
            return 0;
        }

        Console.Error.WriteLine($"未知子命令：{subcommand}");
        return 1;
    }


    public static async Task<int> LabyModAsync(IReadOnlyList<string> positional, CancellationToken ct)
    {
        if (positional.Count == 0)
        {
            Console.Error.WriteLine("用法：wmcl labymod versions|download <production|snapshot> <保存路径>");
            return 1;
        }
        var service = new WMCL.Modding.Forgelike.LabyModService();
        var versions = await service.GetVersionsAsync(ct);
        if (positional[0].Equals("versions", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var version in versions)
                Console.WriteLine($"{version.Channel}\t{version.Version}\t{version.InstallerFileName}");
            return 0;
        }
        if (positional[0].Equals("download", StringComparison.OrdinalIgnoreCase) && positional.Count >= 3)
        {
            var version = versions.FirstOrDefault(item => item.Channel == positional[1])
                          ?? throw new InvalidOperationException("未知 LabyMod 通道：" + positional[1]);
            await service.DownloadInstallerAsync(version, positional[2], ct: ct);
            Console.WriteLine($"已下载 LabyMod {version.Channel} {version.Version}：{positional[2]}");
            return 0;
        }
        Console.Error.WriteLine("用法：wmcl labymod versions|download <production|snapshot> <保存路径>");
        return 1;
    }


    public static async Task<int> CleanroomAsync(IReadOnlyList<string> positional,
        IReadOnlyDictionary<string, string> options, CancellationToken ct)
    {
        if (positional.Count == 0)
        {
            Console.Error.WriteLine("用法：wmcl cleanroom versions|download <tag> <保存路径>");
            return 1;
        }
        var service = new WMCL.Modding.Forgelike.CleanroomService();
        var versions = await service.GetVersionsAsync(ct);
        if (positional[0].Equals("versions", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var version in versions)
            {
                Console.WriteLine($"{version.Tag}\t{version.Detail}\t{version.Published:yyyy-MM-dd}\t{version.InstallerFileName}");
            }
            return 0;
        }
        if (positional[0].Equals("download", StringComparison.OrdinalIgnoreCase) && positional.Count >= 3)
        {
            var version = versions.FirstOrDefault(item => item.Tag == positional[1])
                          ?? throw new InvalidOperationException("未找到 Cleanroom 版本：" + positional[1]);
            var bytes = await WMCL.Core.Http.Http.GetBytesAsync(version.InstallerUrl, ct, TimeSpan.FromMinutes(5));
            await File.WriteAllBytesAsync(positional[2], bytes, ct);
            Console.WriteLine($"已下载 Cleanroom {version.Tag}：{positional[2]}");
            return 0;
        }
        if (positional[0].Equals("install", StringComparison.OrdinalIgnoreCase) && positional.Count >= 2)
        {
            var version = versions.FirstOrDefault(item => item.Tag == positional[1])
                          ?? throw new InvalidOperationException("未找到 Cleanroom 版本：" + positional[1]);
            var root = CliHelpers.GetGameDirectory(options);
            Directory.CreateDirectory(root);
            string javaPath;
            if (options.TryGetValue("java", out var configured))
            {
                javaPath = (await new WMCL.Launch.Java.JavaLocator().ProbePathAsync(configured, ct)
                            ?? throw new InvalidOperationException("无法识别指定的 Java：" + configured)).Path;
            }
            else
            {
                var provisioner = new WMCL.Launch.Java.JavaProvisioner(new WMCL.Launch.Java.JavaLocator());
                var runtime = await provisioner.EnsureAsync(
                    new WMCL.Launch.Java.JavaRequirement(new Version(25, 0, 0)),
                    new SyncProgress<JobProgress>(CliHelpers.PrintProgress), ct);
                javaPath = runtime.Path;
            }
            var installer = new WMCL.Modding.Forgelike.ForgelikeInstaller(
                new WMCL.Minecraft.VersionRepository(root));
            var id = await installer.InstallAsync(
                WMCL.Modding.Forgelike.ForgelikeKind.Cleanroom,
                "1.12.2", version.Tag, javaPath,
                new SyncProgress<JobProgress>(CliHelpers.PrintProgress),
                new SyncProgress<string>(Console.WriteLine),
                ct,
                completeInstallation: !options.ContainsKey("no-complete"));
            Console.WriteLine($"Cleanroom 已安装：{id}");
            return 0;
        }
        Console.Error.WriteLine("用法：wmcl cleanroom versions|download <tag> <保存路径>|install <tag> [--java <路径>] [--dir <游戏目录>] [--no-complete]");
        return 1;
    }


    public static async Task<int> VersionsAsync(
        IReadOnlyDictionary<string, string> options,
        CancellationToken ct)
    {
        var repository = new VersionRepository(CliHelpers.GetGameDirectory(options));
        var installed = repository.ListInstalled();

        Console.WriteLine("已安装：");
        if (installed.Count == 0)
        {
            Console.WriteLine("  （无）");
        }
        else
        {
            foreach (var version in installed)
                Console.WriteLine($"  {version.Id,-24} {version.Type,-10} {version.ReleaseTime}");
        }

        Console.WriteLine();
        Console.WriteLine("远程最新：");
        try
        {
            var manifest = await repository.GetManifestAsync(forceRefresh: true, ct);
            Console.WriteLine($"  正式版：{manifest.Latest.Release}");
            Console.WriteLine($"  快照：  {manifest.Latest.Snapshot}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  获取失败：{ex.Message}");
        }

        return 0;
    }
}
