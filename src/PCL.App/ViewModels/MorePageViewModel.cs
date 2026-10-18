using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PCL.App.ViewModels;

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
            var startInfo = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("explorer.exe", ActionUrl)
                : OperatingSystem.IsMacOS()
                    ? new ProcessStartInfo("open", ActionUrl)
                    : new ProcessStartInfo("xdg-open", ActionUrl);
            startInfo.UseShellExecute = false;
            Process.Start(startInfo);
            Status = "已在浏览器中打开";
        }
        catch (Exception ex)
        {
            Status = "打开失败：" + ex.Message;
        }
    }
}
