using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.ViewModels;
using WMCL.Modding.Local;

namespace WMCL.App.Features.Mods;

public partial class ModsView : UserControl
{
    public ModsView()
    {
        InitializeComponent();
    }
    private void OnToggleSelectedModsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ModsViewModel viewModel) return;
        var selected = LocalModList.SelectedItems?.OfType<LocalMod>().ToArray() ?? [];
        if (selected.Length == 0) return;
        viewModel.SetLocalModsEnabled(selected, !selected.All(mod => mod.IsEnabled));
    }

    private async void OnDeleteSelectedModsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ModsViewModel viewModel) return;
        var selected = LocalModList.SelectedItems?.OfType<LocalMod>().ToArray() ?? [];
        await viewModel.DeleteLocalModsAsync(selected);
    }

    private void OnJarInJarClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ModsViewModel { SelectedLocalMod: { } mod } ||
            TopLevel.GetTopLevel(this) is not Window owner) return;
        _ = new JarInJarWindow(mod.FilePath, mod.DisplayName).ShowDialog(owner);
    }

}
