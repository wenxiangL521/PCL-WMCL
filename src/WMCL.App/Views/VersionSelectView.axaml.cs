using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class VersionSelectView : UserControl
{
    public VersionSelectView() => InitializeComponent();

    private void OnVersionListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // PCL2 PageSelect：单击版本即选中并返回启动页；异步首次选中不会触发。
        if (DataContext is not VersionSelectViewModel viewModel) return;
        if (viewModel.SelectedVersion is null) return;
        viewModel.ConfirmCommand.Execute(null);
    }
}
