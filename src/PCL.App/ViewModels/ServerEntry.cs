using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PCL.Minecraft;

namespace PCL.App.ViewModels;

public sealed partial class ServerEntry : ObservableObject
{
    public required string Name { get; set; }
    public required string Address { get; set; }
    public string? Icon { get; set; }

    [ObservableProperty]
    private Bitmap? _iconImage;

    [ObservableProperty]
    private string _pingText = "";

    public bool HasIcon => IconImage is not null;

    partial void OnIconImageChanged(Bitmap? value) =>
        OnPropertyChanged(nameof(HasIcon));

    public MinecraftServer ToServer() => new(Name, Address, Icon);
}
