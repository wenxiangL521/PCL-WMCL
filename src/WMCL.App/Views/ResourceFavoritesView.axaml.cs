using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class ResourceFavoritesView : UserControl
{
    public ResourceFavoritesView()
    {
        InitializeComponent();
        var batch = this.FindControl<ResourceFavoriteBatchView>("FavoriteBatchPanel")!;
        batch.PropertyChanged += (_, args) =>
        {
            if (args.Property != IsVisibleProperty) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (this.GetVisualRoot() is null) return;
                if (batch.IsVisible) batch.FindControl<ComboBox>("BatchInstanceBox")?.Focus(NavigationMethod.Tab);
                else if (this.FindControl<Button>("BatchInstallButton") is { IsEffectivelyVisible: true } button)
                    button.Focus(NavigationMethod.Tab);
                else this.FindControl<TextBox>("FavoriteSearchBox")?.Focus(NavigationMethod.Tab);
            }, DispatcherPriority.Background);
        };
    }

    private async void OnShareSelectedClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ResourceFavoritesViewModel viewModel) return;
        var code = viewModel.CreateShareCode();
        if (string.IsNullOrWhiteSpace(code))
        {
            viewModel.Status = "请先选择要分享的收藏";
            return;
        }
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(code);
        viewModel.Status = "分享码已复制到剪贴板";
    }

    private async void OnShareNativeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ResourceFavoritesViewModel viewModel) return;
        var code = viewModel.CreateNativeShareCode();
        if (string.IsNullOrWhiteSpace(code))
        {
            viewModel.Status = "请先选择要分享的收藏";
            return;
        }
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(code);
        viewModel.Status = "CE/ME 项目 ID 分享码已复制到剪贴板";
    }

    private async void OnImportFavoritesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ResourceFavoritesViewModel viewModel) return;
        var code = await DialogService.PromptAsync("导入收藏", "粘贴分享码：", "");
        if (!string.IsNullOrWhiteSpace(code)) await viewModel.ImportShareCodeAsync(code);
    }
}
