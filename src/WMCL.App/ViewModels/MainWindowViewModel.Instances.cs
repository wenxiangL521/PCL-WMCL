using WMCL.App.Services;

namespace WMCL.App.ViewModels;

public partial class MainWindowViewModel
{
    internal Task InstanceRefreshTask { get; private set; } = Task.CompletedTask;
    internal Task InstanceInstallPreparationTask { get; private set; } = Task.CompletedTask;

    private async Task RefreshInstancesAsync(InstanceChangeEventArgs change)
    {
        try
        {
            Services.ApplyInstanceChange(change);
            Install.ApplyInstanceChange(change);
            if (Services.Settings.GameDirectory != change.GameDirectory) return;
            var launchId = change.Remap(Launch.SelectedVersion?.Id);
            var instanceId = change.Remap(Instance.SelectedInstance?.Id);
            VersionSelect.RemapSelection(change);
            Mods.SelectedModInstance = change.Remap(Mods.SelectedModInstance);
            Mods.SelectedVanillaInstance = change.Remap(Mods.SelectedVanillaInstance);
            Content.SelectedInstance = change.Remap(Content.SelectedInstance);
            if (!Modpack.IsBusy) Modpack.GameVersion = change.Remap(Modpack.GameVersion) ?? "";
            await Task.WhenAll(Launch.LoadAsync(launchId), Instance.LoadAsync(), Mods.LoadAsync(), Content.LoadAsync(), Modpack.LoadAsync());
            if (Services.Settings.GameDirectory == change.GameDirectory && instanceId is not null)
                Instance.SelectedInstance = Instance.Instances.FirstOrDefault(instance => instance.Id == instanceId) ?? Instance.SelectedInstance;
        }
        catch (Exception ex)
        {
            if (Services.Settings.GameDirectory == change.GameDirectory)
                Instance.Status = "实例文件已更新，但刷新页面失败：" + ex.Message;
        }
    }
}
