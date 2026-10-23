using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class ResourceFavoriteBatchView : UserControl
{
    public ResourceFavoriteBatchView()
    {
        InitializeComponent();
        var cancel = this.FindControl<Button>("BatchCancelButton")!;
        cancel.PropertyChanged += (_, args) =>
        {
            if (args.Property != IsVisibleProperty) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsEffectivelyVisible || this.GetVisualRoot() is null) return;
                if (cancel.IsVisible) cancel.Focus(NavigationMethod.Tab);
                else if (this.FindControl<Button>("BatchCheckButton") is { IsEffectivelyEnabled: true } check)
                    check.Focus(NavigationMethod.Tab);
                else this.FindControl<Button>("BatchCloseButton")?.Focus(NavigationMethod.Tab);
            }, DispatcherPriority.Background);
        };
    }

    protected override void OnKeyDown(KeyEventArgs args)
    {
        base.OnKeyDown(args);
        if (args.Key != Key.Escape || DataContext is not ResourceFavoriteBatchViewModel model) return;
        if (model.IsBusy) model.CancelCommand.Execute(null);
        else model.CloseCommand.Execute(null);
        args.Handled = true;
    }
}
