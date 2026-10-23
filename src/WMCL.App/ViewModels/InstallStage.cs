using CommunityToolkit.Mvvm.ComponentModel;

namespace WMCL.App.ViewModels;

public partial class InstallStage : ObservableObject
{
    public InstallStage(string title)
    {
        Title = title;
        Weight = title switch
        {
            "原版游戏" => 4,
            "Fabric Loader" or "Quilt Loader" or "LegacyFabric Loader" or "Forge" or "NeoForge" or "OptiFine" => 6,
            "OptiFine Mod" => 2,
            _ => 1
        };
    }

    public string Title { get; }
    public double Weight { get; }

    [ObservableProperty]
    private string _detail = "等待";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentText))]
    private double _progress;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentText))]
    private bool _isDone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentText))]
    private bool _isFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentText))]
    private bool _isCancelled;

    public string PercentText => IsDone ? "完成" : IsCancelled ? "已取消" : IsFailed ? "失败" : $"{Progress:0}%";
}
