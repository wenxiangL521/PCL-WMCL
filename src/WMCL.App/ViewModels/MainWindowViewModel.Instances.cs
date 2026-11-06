using System.ComponentModel;
using Avalonia.Threading;
using WMCL.App.Services;

namespace WMCL.App.ViewModels;

public partial class MainWindowViewModel
{
    internal Task InstanceRefreshTask { get; private set; } = Task.CompletedTask;
    internal Task InstanceInstallPreparationTask { get; private set; } = Task.CompletedTask;
    internal Task InstanceSectionPreparationTask { get; private set; } = Task.CompletedTask;
    private int _instanceSectionRequest;
    private bool _refreshingInstances;

    private void OnInstanceNavigationChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName is not (nameof(InstanceViewModel.SelectedSectionIndex) or
            nameof(InstanceViewModel.SelectedInstance))) return;
        if (_refreshingInstances) return;
        if (!ReferenceEquals(CurrentPage, Instance))
        {
            ++_instanceSectionRequest;
            Instance.EmbeddedPage = null;
            Instance.IsSectionPreparing = false;
            return;
        }
        QueueInstanceSectionPreparation();
    }

    private void QueueInstanceSectionPreparation()
    {
        var preparation = PrepareInstanceSectionAsync();
        InstanceSectionPreparationTask = preparation;
        if (Instance.SelectedSectionIndex == 2) InstanceInstallPreparationTask = preparation;
    }

    private void OpenInstanceSection(int index)
    {
        if (Instance.SelectedInstance is null)
        {
            Instance.Status = "请先选择实例";
            return;
        }
        var alreadyOpen = ReferenceEquals(CurrentPage, Instance);
        var changed = Instance.SelectedSectionIndex != index;
        Instance.SelectedSectionIndex = index;
        if (!alreadyOpen) PushPage(Instance, "实例设置");
        else if (!changed) QueueInstanceSectionPreparation();
    }

    private async Task PrepareInstanceSectionAsync()
    {
        var request = ++_instanceSectionRequest;
        var root = Services.Settings.GameDirectory;
        var id = Instance.SelectedInstance?.Id;
        var section = Instance.SelectedSectionIndex;
        ViewModelBase? expectedPage = section switch
        {
            2 => Install,
            3 => Mods,
            6 or 7 or 8 => Content,
            _ => null
        };
        if (!ReferenceEquals(Instance.EmbeddedPage, expectedPage)) Instance.EmbeddedPage = null;
        Instance.IsSectionPreparing = false;
        if (id is null || section is not (2 or 3 or 6 or 7 or 8 or 9)) return;
        Instance.IsSectionPreparing = true;
        // 先把加载动画交给渲染线程，再做读取；否则同步 IO 会占住 UI 线程，动画根本画不出来。
        await UiDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        var startedAt = Environment.TickCount64;
        try
        {
            ViewModelBase? page = null;
            switch (section)
            {
                case 2:
                    await Install.OpenInstanceAsync(root, id);
                    page = Install;
                    break;
                case 3:
                    await Mods.OpenManagementAsync(id);
                    page = Mods;
                    break;
                case 6:
                case 7:
                case 8:
                    await Content.OpenManagementAsync(id, section switch
                    {
                        7 => "content-world",
                        8 => "content-shader",
                        _ => "content-resource"
                    });
                    page = Content;
                    break;
                case 9:
                    await Instance.InstanceTargetPreparationTask;
                    break;
            }
            if (IsCurrentTarget()) Instance.EmbeddedPage = page;
        }
        catch (Exception exception)
        {
            if (IsCurrentTarget()) Instance.Status = "读取实例页面失败：" + exception.Message;
        }
        finally
        {
            if (IsCurrentTarget())
            {
                // 极快的读取也要让动画露一下脸，避免闪一下就没了。
                var remaining = 220 - (Environment.TickCount64 - startedAt);
                if (remaining > 0) await Task.Delay((int)remaining);
                if (IsCurrentTarget()) Instance.IsSectionPreparing = false;
            }
        }

        bool IsCurrentTarget() => request == _instanceSectionRequest &&
            root == Services.Settings.GameDirectory && id == Instance.SelectedInstance?.Id &&
            section == Instance.SelectedSectionIndex;
    }

    private async Task RefreshInstancesAsync(InstanceChangeEventArgs change)
    {
        var selectedId = Instance.SelectedInstance?.Id;
        _refreshingInstances = true;
        ++_instanceSectionRequest;
        Instance.EmbeddedPage = null;
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
            if (!Modpack.IsBusy) Modpack.SearchGameVersion = change.Remap(Modpack.SearchGameVersion) ?? "";
            await Task.WhenAll(Launch.LoadAsync(launchId), Instance.LoadAsync(), Mods.LoadAsync(), Content.LoadAsync(), Modpack.LoadAsync());
            if (Services.Settings.GameDirectory == change.GameDirectory && instanceId is not null)
                Instance.SelectedInstance = Instance.Instances.FirstOrDefault(instance => instance.Id == instanceId) ?? Instance.SelectedInstance;
        }
        catch (Exception ex)
        {
            if (Services.Settings.GameDirectory == change.GameDirectory)
                Instance.Status = "实例文件已更新，但刷新页面失败：" + ex.Message;
        }
        finally
        {
            _refreshingInstances = false;
            if (selectedId is not null && change.Remap(selectedId) is null)
                Instance.SelectedSectionIndex = 0;
            if (ReferenceEquals(CurrentPage, Instance))
            {
                QueueInstanceSectionPreparation();
                await InstanceSectionPreparationTask;
            }
        }
    }
}
