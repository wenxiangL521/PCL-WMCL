using WMCL.App.Services.Infrastructure;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.Core.IO;
using WMCL.Minecraft;
using WMCL.Modding.Modpack;

namespace WMCL.App.Features.Instances;

public partial class ExportFileChoice(string relativePath, string fullPath, long size, bool isDirectory) : ObservableObject
{
    public string RelativePath { get; } = relativePath;
    public string FullPath { get; } = fullPath;
    public bool IsDirectory { get; } = isDirectory;
    public string SizeText => IsDirectory ? "文件夹" : size >= 1048576 ? $"{size / 1048576d:0.0} MB" : $"{size / 1024d:0.0} KB";
    public Services.Infrastructure.LocalResourceKind IconKind => IsDirectory ? Services.Infrastructure.LocalResourceKind.Folder : Services.Infrastructure.LocalResourceKind.File;
    [ObservableProperty]
    private bool _isSelected = true;
}

public partial class InstanceViewModel
{
    public ObservableCollection<ExportFileChoice> ExportFiles { get; } = [];
    private int _exportFilesRequest;
    private string? _exportFilesTarget;
    internal Task ExportFilesPreparationTask { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    private bool _useExportFileSelection;
    [ObservableProperty]
    private bool _isLoadingExportFiles;
    [ObservableProperty]
    private string _exportFileSelectionStatus = "可直接按内容类别导出，也可启用具体文件选择。";

    public string ExportSelectionCount => $"已选 {ExportFiles.Count(file => file.IsSelected)}/{ExportFiles.Count} 项";

    partial void OnUseExportFileSelectionChanged(bool value)
    {
        if (value) ExportFilesPreparationTask = RefreshExportFilesAsync();
    }

    private void ExportScopeChanged()
    {
        if (!UseExportFileSelection) return;
        ExportFilesPreparationTask = RefreshExportFilesAsync();
    }

    private string CurrentExportTarget => GameDirectory + "\n" + SelectedInstance?.Id + "\n" + _services.Settings.IsolationMode;

    private void ResetExportFileSelection()
    {
        ++_exportFilesRequest;
        _exportFilesTarget = null;
        ExportFiles.Clear();
        OnPropertyChanged(nameof(ExportSelectionCount));
        if (UseExportFileSelection && SelectedInstance is not null)
            ExportFilesPreparationTask = RefreshExportFilesAsync();
    }

    /// <summary>列出实例隔离目录的第一层文件与文件夹；文件夹保持折叠，导出时再展开。</summary>
    [RelayCommand]
    private async Task RefreshExportFilesAsync()
    {
        var request = ++_exportFilesRequest;
        var root = GameDirectory;
        var id = SelectedInstance?.Id;
        var target = CurrentExportTarget;
        if (id is null) { ExportFileSelectionStatus = "请先选择实例。"; return; }
        var previous = _exportFilesTarget == target
            ? ExportFiles.ToDictionary(file => file.RelativePath, file => file.IsSelected, StringComparer.Ordinal)
            : [];
        IsLoadingExportFiles = true;
        ExportFileSelectionStatus = "正在读取实例目录…";
        try
        {
            var version = await new VersionRepository(root).GetLocalVersionAsync(id);
            var directory = IsolationPolicy.GetGameDirectory(root, id, version, _services.Settings.IsolationMode);
            var isolated = string.Equals(
                Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(Path.Combine(root, "versions", id)).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.Ordinal);
            if (!isolated)
            {
                if (request != _exportFilesRequest || target != CurrentExportTarget) return;
                ExportFiles.Clear();
                _exportFilesTarget = null;
                ExportFileSelectionStatus = "该实例使用共享游戏目录，无法按文件选择导出；请先启用实例隔离。";
                OnPropertyChanged(nameof(ExportSelectionCount));
                return;
            }
            var entries = await Task.Run(() => Directory.EnumerateFileSystemEntries(directory)
                .Where(path => ExportSafety.ValidatePath(path))
                .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                .Select(path =>
                {
                    var isDirectory = (File.GetAttributes(path) & FileAttributes.Directory) != 0;
                    var size = isDirectory ? 0 : new FileInfo(path).Length;
                    return new ExportFileChoice(Path.GetFileName(path), path, size, isDirectory);
                })
                .OrderByDescending(entry => entry.IsDirectory)
                .ThenBy(entry => entry.RelativePath, StringComparer.Ordinal)
                .ToArray());
            if (request != _exportFilesRequest || target != CurrentExportTarget) return;
            ExportFiles.Clear();
            foreach (var entry in entries)
            {
                if (previous.TryGetValue(entry.RelativePath, out var selected)) entry.IsSelected = selected;
                entry.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(ExportFileChoice.IsSelected))
                        OnPropertyChanged(nameof(ExportSelectionCount));
                };
                ExportFiles.Add(entry);
            }
            _exportFilesTarget = target;
            ExportFileSelectionStatus = "文件夹按整目录导出，保持折叠；只导出勾选项，整合包索引与加载器依赖始终保留。";
            OnPropertyChanged(nameof(ExportSelectionCount));
        }
        catch (Exception exception)
        {
            if (request != _exportFilesRequest || target != CurrentExportTarget) return;
            _exportFilesTarget = null;
            ExportFiles.Clear();
            ExportFileSelectionStatus = "无法读取实例目录：" + exception.Message;
        }
        finally { if (request == _exportFilesRequest) IsLoadingExportFiles = false; }
    }

    [RelayCommand]
    private void SelectAllExportFiles()
    {
        foreach (var file in ExportFiles) file.IsSelected = true;
    }

    [RelayCommand]
    private void ClearExportFileSelection()
    {
        foreach (var file in ExportFiles) file.IsSelected = false;
    }

    /// <summary>把勾选的文件夹展开成具体文件，交给导出服务做路径与链接校验。</summary>
    private HashSet<string> BuildExportWhitelist(string directory)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in ExportFiles.Where(file => file.IsSelected))
        {
            if (!entry.IsDirectory) { result.Add(entry.RelativePath); continue; }
            foreach (var file in ExportSafety.EnumerateFiles(directory, entry.RelativePath))
                result.Add(Path.GetRelativePath(directory, file).Replace('\\', '/'));
        }
        return result;
    }

    private ModpackExportOptions BuildModpackExportOptions(IReadOnlySet<string>? included) =>
        new(ExportMods, ExportConfigs, ExportResourcePacks, ExportShaderPacks, ExportSaves, ExportWmcl, ExportOptions, included);
}
