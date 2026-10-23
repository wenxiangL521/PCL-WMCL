namespace WMCL.App.ViewModels;

public sealed class LinkSettingsViewModel(LinkViewModel connection) : ViewModelBase
{
    public LinkViewModel Connection { get; } = connection;
}
