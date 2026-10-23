using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.Animation;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class ModsView : UserControl
{
    public ModsView()
    {
        InitializeComponent();
    }
    private bool _returningToResults;

    private void OnJarInJarClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ModsViewModel { SelectedLocalMod: { } mod } ||
            TopLevel.GetTopLevel(this) is not Window owner) return;
        _ = new JarInJarWindow(mod.FilePath, mod.DisplayName).ShowDialog(owner);
    }

    private async void OnReturnToResultsClick(object? sender, RoutedEventArgs e)
    {
        if (_returningToResults || DataContext is not ModsViewModel viewModel) return;
        _returningToResults = true;
        try
        {
            if (this.FindControl<StackPanel>("ResourceDetailPanel") is { } panel)
                await ResourceDetailMotion.ExitAsync(panel);
            viewModel.ReturnToResultsCommand.Execute(null);
        }
        finally { _returningToResults = false; }
    }

}
