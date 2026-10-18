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
            "Fabric Loader" or "Forge" or "NeoForge" or "OptiFine" => 6,
            "OptiFine Mod" => 2,
            _ => 1
        };
    }

    public string Title { get; }
    public double Weight { get; }

    [ObservableProperty]
    private string _detail = "等待";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isDone;

    [ObservableProperty]
    private bool _isFailed;

    public string PercentText => IsDone ? "完成" : IsFailed ? "失败" : $"{Progress:0}%";
}
