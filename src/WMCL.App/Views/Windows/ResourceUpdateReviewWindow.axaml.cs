using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class ResourceUpdateReviewWindow : Window
{
    public ResourceUpdateReviewWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (DataContext as ResourceUpdateReviewViewModel)?.CancelCommand.Execute(null);
    }

    public ResourceUpdateReviewWindow(ResourceUpdateReviewViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnClose(object? sender, RoutedEventArgs args) => Close();
}
