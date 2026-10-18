using CommunityToolkit.Mvvm.ComponentModel;

namespace PCL.App.ViewModels;

public partial class InstallStage : ObservableObject
{
    public InstallStage(string title) => Title = title;

    public string Title { get; }

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
