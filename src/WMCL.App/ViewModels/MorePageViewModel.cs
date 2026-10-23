using WMCL.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WMCL.App.ViewModels;

/// <summary>PCL2 “更多”下无需独立业务状态的页面（帮助/百宝箱/反馈/投票）。</summary>
public partial class MorePageViewModel : ViewModelBase
{
    public MorePageViewModel(string title, string subtitle, string body,
        string? actionText = null, string? actionUrl = null)
    {
        Title = title;
        Subtitle = subtitle;
        Body = body;
        ActionText = actionText ?? "";
        ActionUrl = actionUrl;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string Body { get; }
    public string ActionText { get; }
    public string? ActionUrl { get; }
    public bool HasAction => !string.IsNullOrEmpty(ActionText);

    [ObservableProperty]
    private string _status = "";

    [RelayCommand]
    private void OpenAction()
    {
        if (string.IsNullOrWhiteSpace(ActionUrl)) return;
        try
        {
            ExternalLauncher.Open(ActionUrl);
            Status = "已在浏览器中打开";
        }
        catch (Exception ex)
        {
            Status = "打开失败：" + ex.Message;
        }
    }
}
