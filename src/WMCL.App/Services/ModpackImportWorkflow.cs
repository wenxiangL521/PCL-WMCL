using System.IO.Compression;
using WMCL.Minecraft;
using WMCL.Core.Jobs;
using WMCL.Modding.Modpack;

namespace WMCL.App.Services;

public sealed class ModpackImportWorkflow(
    ModpackInstaller modrinth,
    CurseForgeModpackInstaller curseforge,
    LocalModpackInstaller local,
    Func<string, string, CancellationToken, Task<bool>>? confirm = null,
    Func<string, string?, IProgress<JobProgress>?, CancellationToken, Task<string>>? importBackup = null)
{
    private readonly Func<string, string, CancellationToken, Task<bool>> _confirm = confirm ?? DialogService.ConfirmAsync;

    public Task<string> InstallLocalAsync(string path, string? name, bool includeOptional,
        IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (IsInstanceBackup(path))
            return importBackup is not null ? importBackup(path, name, progress, ct)
                : throw new InvalidOperationException("当前导入入口未配置实例备份恢复目标。");
        if (LocalModpackInstaller.Detect(path) != LocalModpackKind.Unknown)
            return local.InstallFromFileAsync(path, name, progress, ct);
        return CurseForgeModpackInstaller.IsArchive(path)
            ? InstallCurseForgeAsync(path, name, includeOptional, progress, ct)
            : InstallModrinthAsync(path, name, includeOptional, progress, ct);
    }

    internal static bool IsInstanceBackup(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var markers = archive.Entries.Where(entry => entry.FullName.Equals("wmcl-export.json", StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.Equals("pcl-export.json", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (markers.Length > 1) throw new InvalidDataException("导入包包含重复或不明确的实例备份标记。");
        return markers.Length == 1;
    }

    public async Task<string> InstallCurseForgeAsync(string path, string? name, bool includeOptional,
        IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlySet<long>? selection = null;
        if (includeOptional)
        {
            var optional = await curseforge.GetOptionalFilesAsync(path, ct);
            var selected = new HashSet<long>();
            foreach (var file in optional)
            {
                ct.ThrowIfCancellationRequested();
                if (await _confirm("下载可选文件", $"是否要下载整合包中的可选文件 {file.DisplayName}？", ct))
                    selected.Add(file.FileId);
            }
            selection = selected;
        }
        ct.ThrowIfCancellationRequested();
        return await curseforge.InstallFromFileAsync(path, name, progress, ct, includeOptional, selection);
    }

    public async Task<string> InstallModrinthAsync(string path, string? name, bool includeOptional,
        IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlySet<string>? selection = null;
        if (includeOptional)
        {
            var optional = await ModpackInstaller.GetOptionalFilesAsync(path, ct);
            var selected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in optional)
            {
                ct.ThrowIfCancellationRequested();
                if (await _confirm("下载可选文件", $"是否要下载整合包中的可选文件 {file.Path}？", ct))
                    selected.Add(file.Path);
            }
            selection = selected;
        }
        ct.ThrowIfCancellationRequested();
        return await modrinth.InstallFromFileAsync(path, name, progress, ct, includeOptional, selection);
    }
}
