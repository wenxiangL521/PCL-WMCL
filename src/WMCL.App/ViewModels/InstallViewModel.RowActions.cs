using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using WMCL.Core.Jobs;
using WMCL.App.Services;
using WMCL.Modding.Forgelike;
using WMCL.Modding.Loaders;

namespace WMCL.App.ViewModels;

public partial class InstallViewModel
{
    private readonly ComponentRowActionService _rowActions;
    private CancellationTokenSource? _componentRowActionCancellation;

    [ObservableProperty]
    private bool _isSavingComponentInstaller;
    [ObservableProperty]
    private string _componentInstallerName = "";
    [ObservableProperty]
    private string _componentInstallerStatus = "";
    [ObservableProperty]
    private double _componentInstallerProgress;

    [RelayCommand]
    private void CancelComponentRowActions() => _componentRowActionCancellation?.Cancel();

    internal IReadOnlyList<InstallComponentAction> CreateComponentRowActions(string kind,
        InstallComponentChoice choice, Func<bool> current)
    {
        var minecraft = Download.SelectedVersion?.Id;
        if (minecraft is null) return [];
        var requests = new List<ComponentRowActionRequest>();
        switch (kind)
        {
            case "OptiFine" when choice.OptiFine is { } optifine:
                requests.Add(new("另存为", OptiFine: optifine));
                requests.Add(new("更新日志", "https://optifine.net/changelog?f=" + Uri.EscapeDataString(optifine.FileName)));
                break;
            case "Forge":
                requests.Add(new("另存为", Artifact: ComponentInstallerSources.Forge(minecraft,
                    choice.Forge ?? new ForgeVersionInfo(choice.Version, null))));
                requests.Add(new("更新日志", ComponentInstallerSources.ForgeChangelog(minecraft, choice.Version)));
                break;
            case "NeoForge":
                requests.Add(new("另存为", Artifact: ComponentInstallerSources.NeoForge(minecraft, choice.Version)));
                requests.Add(new("更新日志", ComponentInstallerSources.NeoForgeBase(minecraft, choice.Version) + "-changelog.txt"));
                break;
            case "LiteLoader" when choice.LiteLoader is { } lite:
                requests.Add(new("保存安装器", Artifact: ComponentInstallerSources.LiteLoader(lite)));
                if (!lite.IsLegacy) requests.Add(new("查看全部版本", "https://jenkins.liteloader.com/view/" + lite.Minecraft));
                break;
        }
        return requests.Select(request => new InstallComponentAction(request.Title,
            request.Title == "更新日志" ? InstallComponentAction.InfoIcon :
                request.Title == "查看全部版本" ? InstallComponentAction.ListIcon : InstallComponentAction.SaveIcon,
            request.Title == "更新日志" ? 1.05 : 1,
            new AsyncRelayCommand(() => ExecuteComponentRowActionAsync(request, current),
                () => CanEditInstall && current()))).ToArray();
    }

    private async Task ExecuteComponentRowActionAsync(ComponentRowActionRequest request, Func<bool> current)
    {
        if (!CanEditInstall || !current()) return;
        if (request.Url is not null)
        {
            try { await _rowActions.ExecuteAsync(request, () => CanEditInstall && current(), CancellationToken.None); }
            catch (Exception exception)
            {
                if (current())
                {
                    CombinedStatus = "打开失败：" + exception.Message;
                    TaskFinished?.Invoke(this, new TaskEntry(request.Title, CombinedStatus, 0, "失败", null));
                }
            }
            return;
        }
        if (_componentRowActionCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _componentRowActionCancellation = cancellation;
        var savingStatus = "正在保存 " + request.SuggestedFileName + "……";
        bool CanReport() => CanEditInstall && current() && CombinedStatus == savingStatus;
        ComponentInstallerName = request.SuggestedFileName;
        ComponentInstallerStatus = savingStatus;
        ComponentInstallerProgress = 0;
        IsSavingComponentInstaller = true;
        try
        {
            CombinedStatus = savingStatus;
            var progress = new Progress<JobProgress>(update =>
            {
                if (!ReferenceEquals(_componentRowActionCancellation, cancellation) || cancellation.IsCancellationRequested) return;
                ComponentInstallerProgress = (update.Percent ?? 0) * 100;
                ComponentInstallerStatus = update.Stage + (update.Detail is null ? "" : " · " + update.Detail);
            });
            var saved = await _rowActions.ExecuteAsync(request, () => CanEditInstall && current(), cancellation.Token, progress);
            ComponentInstallerProgress = saved ? 100 : ComponentInstallerProgress;
            ComponentInstallerStatus = saved ? "已保存 " + request.SuggestedFileName : "已取消保存安装器";
            if (CanReport()) CombinedStatus = ComponentInstallerStatus;
        }
        catch (OperationCanceledException)
        {
            ComponentInstallerStatus = "已取消保存安装器";
            if (CanReport()) CombinedStatus = ComponentInstallerStatus;
        }
        catch (Exception exception)
        {
            ComponentInstallerStatus = "保存安装器失败：" + exception.Message;
            if (CanReport()) CombinedStatus = ComponentInstallerStatus;
        }
        finally
        {
            _componentRowActionCancellation = null;
            IsSavingComponentInstaller = false;
        }
    }
}
