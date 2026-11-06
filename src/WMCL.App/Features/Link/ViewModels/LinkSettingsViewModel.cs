namespace WMCL.App.Features.Link;

public sealed class LinkSettingsViewModel(LinkViewModel connection) : ViewModelBase
{
    public LinkViewModel Connection { get; } = connection;
}
