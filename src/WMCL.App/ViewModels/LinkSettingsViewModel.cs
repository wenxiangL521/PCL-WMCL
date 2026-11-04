namespace WMCL.App.ViewModels;

public sealed class LinkSettingsViewModel(LinkViewModel connection) : ViewModelBase
{
    public LinkViewModel Connection { get; } = connection;

    /// <summary>Astral 相关设置（用户名/虚拟 IP/中转），由主窗口注入。</summary>
    public AstralViewModel? Astral { get; set; }
}
