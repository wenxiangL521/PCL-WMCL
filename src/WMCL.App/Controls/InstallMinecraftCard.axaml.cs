using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WMCL.App.ViewModels;

namespace WMCL.App.Controls;

public partial class InstallMinecraftCard : UserControl
{
    public InstallMinecraftCard() => InitializeComponent();

    private async void OnRestoreClick(object? sender, RoutedEventArgs args)
    {
        await Task.Yield();
        if (DataContext is InstallViewModel model && model.RestoreMinecraftCommand.ExecutionTask is { } restore)
            await restore;
        if (this.GetVisualRoot() is not null && HeaderButton.IsEffectivelyEnabled)
            HeaderButton.Focus(NavigationMethod.Directional);
    }
}
