using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Core.Jobs;
using WMCL.Minecraft;
using WMCL.Modding.CurseForge;
using WMCL.Modding.Local;
using WMCL.Modding.Modrinth;

namespace WMCL.App.Features.Instances;

public partial class InstanceContentViewModel
{
    [RelayCommand]
    private async Task SearchDownloadsAsync()
    {
        if (IsDownloadBusy) return;
        _hasBrowsedResources = true;
        _resourceSearchOffset = 0;
        DownloadResults.Clear();
        ResourceVersions.Clear();
        SelectedResourceVersion = null;
        SelectedDownloadProject = null;
        CanLoadMoreResources = false;
        await FetchDownloadsAsync(append: false);
    }

    [RelayCommand]
    private async Task LoadMoreDownloadsAsync()
    {
        if (!CanLoadMoreResources || IsDownloadBusy) return;
        await FetchDownloadsAsync(append: true);
    }

    private Task FetchDownloadsAsync(bool append) => _activeResourceSearch = FetchDownloadsCoreAsync(append);

    private async Task FetchDownloadsCoreAsync(bool append)
    {
        if (IsDownloadBusy) return;
        var game = EffectiveGameVersion;
        var searchToken = ++_resourceSearchToken;
        var projectType = SelectedDownloadType switch
        {
            "光影" => "shader",
            "数据包" => "datapack",
            "世界" => "world",
            "插件" => "plugin",
            _ => "resourcepack"
        };
        if (projectType == "world" && SelectedPlatform != "CurseForge")
        {
            DownloadStatus = "世界下载目前仅支持 CurseForge";
            return;
        }
        IsDownloadBusy = true;
        DownloadStatus = $"正在搜索 {SelectedPlatform} {SelectedDownloadType}…";
        try
        {
            if (SelectedPlatform == "CurseForge")
            {
                var classId = SelectedDownloadType switch
                {
                    _ => WMCL.App.ViewModels.Content.ResourceTypeMap.CurseForgeClassId(SelectedDownloadType)
                };
                var result = await _services.CurseForge.SearchAsync(
                    DownloadSearchText, game, "", 20, default, classId, false,
                    _resourceSearchOffset);
                if (searchToken != _resourceSearchToken) return;
                foreach (var project in result.Projects)
                    DownloadResults.Add(new ResourceItem(
                        project.Name, project.Summary, "CurseForge", null, project.Id, projectType, project.Slug, project.LogoUrl));
                _resourceSearchOffset += result.Projects.Count;
                CanLoadMoreResources =
                    result.Projects.Count >= 20 && _resourceSearchOffset < result.TotalCount;
                DownloadStatus = $"共 {result.TotalCount} 个 CurseForge 结果";
            }
            else
            {
                var result = await _services.Modrinth.SearchAsync(
                    DownloadSearchText, game, "",
                    20, default, projectType, _resourceSearchOffset);
                if (searchToken != _resourceSearchToken) return;
                foreach (var project in result.Projects)
                    DownloadResults.Add(new ResourceItem(
                        project.Title, project.Description, "Modrinth", project.Id, 0, projectType, project.Slug, project.IconUrl));
                _resourceSearchOffset += result.Projects.Count;
                CanLoadMoreResources =
                    result.Projects.Count >= 20 && _resourceSearchOffset < result.TotalHits;
                DownloadStatus = $"共 {result.TotalHits} 个 Modrinth 结果";
            }
        }
        catch (Exception ex)
        {
            if (searchToken == _resourceSearchToken) DownloadStatus = "搜索失败：" + ex.Message;
        }
        finally
        {
            IsDownloadBusy = false;
            UpdateDownloadCommands();
        }
    }

    public string ProjectPageText => SelectedDownloadProject is null
        ? "打开项目页"
        : SelectedDownloadProject.Platform == "CurseForge" ? "转到 CurseForge" : "转到 Modrinth";

    [RelayCommand]
    private void CopyProjectName()
    {
        var name = SelectedDownloadProject?.Title;
        if (string.IsNullOrWhiteSpace(name)) return;
        if (WMCL.App.Services.ClipboardService.TrySetText(name))
            DownloadStatus = "已复制名称：" + name;
    }

    [RelayCommand]
    private void OpenSelectedProject()
    {
        if (SelectedDownloadProject is null) return;
        var selected = SelectedDownloadProject;
        var type = SelectedDownloadType switch
        {
            "光影" => "shader",
            "数据包" => "datapack",
            "世界" => "world",
            "插件" => "plugin",
            _ => "resourcepack"
        };
        var url = selected.Platform == "CurseForge"
            ? selected.CurseForgeId > 0
                ? $"https://www.curseforge.com/minecraft/{type switch
                {
                    "shader" => "shaders",
                    "datapack" => "data-packs",
                    "world" => "worlds",
                    "plugin" => "bukkit-plugins",
                    _ => "texture-packs"
                }}/{selected.CurseForgeId}"
                : null
            : string.IsNullOrWhiteSpace(selected.ModrinthId)
                ? null
                : $"https://modrinth.com/{type}/{selected.ModrinthId}";
        if (url is null)
        {
            DownloadStatus = "该项目没有可用的项目页链接";
            return;
        }
        try
        {
            WMCL.App.Services.ExternalLauncher.Open(url);
            DownloadStatus = "已在浏览器中打开项目页";
        }
        catch (Exception ex)
        {
            DownloadStatus = "打开项目页失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task InstallDownloadAsync()
    {
        if (SelectedDownloadProject is null || IsDownloadBusy) return;
        var root = TargetGameRoot;

        var projectType = SelectedDownloadType switch
        {
            "光影" => "shader",
            "数据包" => "datapack",
            "世界" => "world",
            "插件" => "plugin",
            _ => "resourcepack"
        };
        if (projectType == "world")
        {
            await InstallWorldAsync();
            return;
        }
        var target = projectType switch
        {
            "datapack" => Path.Combine(
                GameDirectory, "saves", SelectedDataPackWorld ?? "", "datapacks"),
            "shader" => Path.Combine(GameDirectory, "shaderpacks"),
            "plugin" => string.IsNullOrWhiteSpace(PluginTargetDirectory)
                ? Path.Combine(GameDirectory, "plugins")
                : PluginTargetDirectory,
            _ => Path.Combine(GameDirectory, "resourcepacks")
        };
        IsDownloadBusy = true;
        DownloadProgress = 0;
        DownloadStatus = $"正在安装 {SelectedDownloadProject.Title}…";
        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;
        UpdateDownloadCommands();
        try
        {
            using var operation = _services.GameOperations.Acquire(root, "安装资源");
            var progress = new Progress<JobProgress>(update =>
            {
                DownloadProgress = (update.Percent ?? 0) * 100;
                DownloadProgressText = ProgressFormatter.Format(update);
            });
            IReadOnlyList<string> files;
            if (SelectedDownloadProject.Platform == "CurseForge")
            {
                var result = SelectedResourceVersion?.CurseForge is { } selected
                    ? await _services.CurseForge.InstallContentVersionAsync(
                        SelectedDownloadProject.CurseForgeId, selected.Id, target, progress, ct)
                    : await _services.CurseForge.InstallContentAsync(
                        SelectedDownloadProject.CurseForgeId, _gameVersion, target,
                        WMCL.App.ViewModels.Content.ResourceTypeMap.CurseForgeClassId(SelectedDownloadType), progress, ct);
                files = result.Files;
            }
            else
            {
                var result = SelectedResourceVersion?.Modrinth is { } selected
                    ? await _services.Modrinth.InstallContentVersionAsync(
                        selected, target, projectType, progress, ct)
                    : await _services.Modrinth.InstallContentAsync(
                        SelectedDownloadProject.ModrinthId!, _gameVersion, target, projectType, progress, ct);
                files = result.Files;
            }
            DownloadStatus = files.Count == 0
                ? "文件已存在，无需重复安装"
                : $"已安装：{string.Join("，", files)}";
            DownloadProgress = 100;
            Refresh();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            DownloadStatus = "已取消安装";
        }
        catch (Exception ex)
        {
            LastTaskFailure = TaskFailureInfo.From(ex, "下载资源");
            _lastRetryPayload = TaskRetryPayload.Create(
                TaskRetryKind.ResourceInstall, TargetGameRoot,
                resourceProjectId: SelectedDownloadProject?.ModrinthId ?? SelectedDownloadProject?.CurseForgeId.ToString(),
                options: new Dictionary<string, string> { ["type"] = SelectedDownloadType });
            DownloadStatus = "安装失败：" + ex.Message;
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
            IsDownloadBusy = false;
            DownloadProgress = 0;
            DownloadProgressText = "";
            UpdateDownloadCommands();
        }
    }

    private async Task InstallWorldAsync()
    {
        var project = SelectedDownloadProject!;
        var selectedVersion = SelectedResourceVersion;
        var gameVersion = _gameVersion;
        var gameDirectory = GameDirectory;
        var root = TargetGameRoot;
        IsDownloadBusy = true;
        DownloadProgress = 0;
        DownloadStatus = $"正在安装世界 {project.Title}…";
        string? temp = null;
        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;
        UpdateDownloadCommands();
        try
        {
            using var operation = _services.GameOperations.Acquire(root, "安装世界");
            var progress = new Progress<JobProgress>(update =>
            {
                DownloadProgress = (update.Percent ?? 0) * 100;
                DownloadProgressText = ProgressFormatter.Format(update);
            });
            var file = selectedVersion?.CurseForge
                       ?? await _services.CurseForge.FindFileAsync(
                           project.CurseForgeId, gameVersion, null)
                       ?? throw new InvalidOperationException("没有找到适用于当前版本的世界文件。");
            temp = Path.Combine(Path.GetTempPath(), "wmcl-world-" + Guid.NewGuid().ToString("N") + ".zip");
            await CurseForgeClient.DownloadFileAsync(file, temp, progress, ct);
            var destination = WorldInstaller.Install(
                temp, Path.Combine(gameDirectory, "saves"), project.Title);
            DownloadStatus = "世界已安装：" + Path.GetFileName(destination);
            DownloadProgress = 100;
            Refresh();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            DownloadStatus = "已取消安装世界";
        }
        catch (Exception ex)
        {
            DownloadStatus = "世界安装失败：" + ex.Message;
        }
        finally
        {
            if (temp is not null && File.Exists(temp)) File.Delete(temp);
            _downloadCts?.Dispose();
            _downloadCts = null;
            IsDownloadBusy = false;
            DownloadProgress = 0;
            DownloadProgressText = "";
            UpdateDownloadCommands();
        }
    }

    private void UpdateDownloadCommands()
    {
        CanDownload = !IsDownloadBusy &&
                      !string.IsNullOrWhiteSpace(GameDirectory) &&
                      SelectedDownloadProject is not null &&
                      (!RequiresDataPackWorld || !string.IsNullOrWhiteSpace(SelectedDataPackWorld));
        CanCancelDownload = IsDownloadBusy && _downloadCts is not null;
        CanSaveDownload = !IsDownloadBusy && SelectedDownloadProject is not null &&
                          SelectedDownloadType != "整合包" &&
                          (SelectedResourceVersion ?? ResourceVersions.FirstOrDefault(version => version.IsCompatible)
                              ?? ResourceVersions.FirstOrDefault()) is not null;
    }

    public string SaveFileName
    {
        get
        {
            var version = SelectedResourceVersion ??
                          ResourceVersions.FirstOrDefault(item => item.IsCompatible) ??
                          ResourceVersions.FirstOrDefault();
            return version?.FileName ?? "resource.zip";
        }
    }

    public async Task SaveDownloadAsync(string destination, CancellationToken ct = default)
    {
        var project = SelectedDownloadProject;
        var version = SelectedResourceVersion ??
                      ResourceVersions.FirstOrDefault(item => item.IsCompatible) ??
                      ResourceVersions.FirstOrDefault();
        if (project is null || version is null || IsDownloadBusy) return;
        SelectedResourceVersion = version;
        var projectType = SelectedDownloadType switch
        {
            "光影" => "shader",
            "数据包" => "datapack",
            "世界" => "world",
            "插件" => "plugin",
            _ => "resourcepack"
        };
        var temp = Path.Combine(Path.GetTempPath(), "wmcl-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        IsDownloadBusy = true;
        DownloadProgress = 0;
        DownloadStatus = $"正在保存 {version.FileName}…";
        _downloadCts?.Dispose();
        _downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _downloadCts.Token;
        UpdateDownloadCommands();
        try
        {
            var progress = new Progress<JobProgress>(update =>
            {
                DownloadProgress = (update.Percent ?? 0) * 100;
                DownloadProgressText = ProgressFormatter.Format(update);
            });
            string? file;
            if (version.CurseForge is { } curse)
            {
                var result = await _services.CurseForge.InstallContentVersionAsync(
                    project.CurseForgeId, curse.Id, temp, progress, token);
                file = result.Files.FirstOrDefault();
            }
            else if (version.Modrinth is { } modrinth)
            {
                var result = await _services.Modrinth.InstallContentVersionAsync(
                    modrinth, temp, projectType, progress, token);
                file = result.Files.FirstOrDefault();
            }
            else
            {
                throw new InvalidOperationException("该版本没有可下载文件。");
            }
            if (string.IsNullOrWhiteSpace(file))
                throw new InvalidOperationException("下载结果为空。");
            var source = Path.Combine(temp, file);
            if (!File.Exists(source))
            {
                source = Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories).FirstOrDefault()
                         ?? throw new FileNotFoundException("下载文件不存在。", source);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");
            File.Move(source, destination, true);
            DownloadProgress = 100;
            DownloadStatus = "已保存：" + destination;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            DownloadStatus = "已取消保存";
        }
        catch (Exception ex)
        {
            DownloadStatus = "保存失败：" + ex.Message;
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch (IOException) { /* 已被占用或不存在 */ } catch (UnauthorizedAccessException) { /* 无权限，忽略 */ }
            _downloadCts?.Dispose();
            _downloadCts = null;
            IsDownloadBusy = false;
            DownloadProgress = 0;
            DownloadProgressText = "";
            UpdateDownloadCommands();
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        if (_downloadCts is null) return;
        _downloadCts.Cancel();
        DownloadStatus = "正在取消安装…";
    }

    /// <summary>Batch operations for multi-selection in the resource/shader/world lists.</summary>
}
