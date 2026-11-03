using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.ViewModels;
using WMCL.Modding.Local;

namespace WMCL.App.Views;

public partial class InstanceContentView : UserControl
{
    public InstanceContentView()
    {
        InitializeComponent();
    }

    private static IReadOnlyList<InstanceContentItem> Selected(ListBox list) =>
        list.SelectedItems?.OfType<InstanceContentItem>().ToArray() ?? [];

    private void OnToggleResourcePacksClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InstanceContentViewModel viewModel) return;
        var selected = Selected(ResourcePackList);
        if (selected.Count == 0) return;
        viewModel.SetResourcePacksEnabled(selected, !selected.All(item => item.IsEnabled));
    }

    private async void OnDeleteResourcePacksClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InstanceContentViewModel viewModel) return;
        await viewModel.DeleteInstanceContentAsync(InstanceContentKind.ResourcePack, Selected(ResourcePackList));
    }

    private async void OnDeleteShaderPacksClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InstanceContentViewModel viewModel) return;
        await viewModel.DeleteInstanceContentAsync(InstanceContentKind.ShaderPack, Selected(ShaderPackList));
    }

    private async void OnDeleteWorldsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InstanceContentViewModel viewModel) return;
        await viewModel.DeleteInstanceContentAsync(InstanceContentKind.World, Selected(WorldList));
    }

}
