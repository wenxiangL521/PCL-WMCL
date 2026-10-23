using WMCL.Core.Jobs;
using WMCL.Modding.Modpack;

namespace WMCL.App.Services;

public sealed class ModpackImportWorkflow(
    ModpackInstaller modrinth,
    CurseForgeModpackInstaller curseforge,
    LocalModpackInstaller local,
    Func<string, string, CancellationToken, Task<bool>>? confirm = null)
{
    private readonly Func<string, string, CancellationToken, Task<bool>> _confirm = confirm ?? DialogService.ConfirmAsync;

    public Task<string> InstallLocalAsync(string path, string? name, bool includeOptional,
        IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (LocalModpackInstaller.Detect(path) != LocalModpackKind.Unknown)
            return local.InstallFromFileAsync(path, name, progress, ct);
        return CurseForgeModpackInstaller.IsArchive(path)
            ? InstallCurseForgeAsync(path, name, includeOptional, progress, ct)
            : InstallModrinthAsync(path, name, includeOptional, progress, ct);
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
