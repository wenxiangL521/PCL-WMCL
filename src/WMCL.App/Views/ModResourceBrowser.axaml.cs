using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class ModResourceBrowser : ResourceBrowserView
{
    public ModResourceBrowser()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            if (_viewModel is null) return;
            _viewModel.PropertyChanging -= OnProjectChanging;
            _viewModel.PropertyChanging += OnProjectChanging;
            BindTargetSelection();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.PropertyChanging -= OnProjectChanging;
            _targetSelection?.Dispose();
            _targetSelection = null;
        };
    }

    private ModsViewModel? _viewModel;
    private ResourceBrowserTargetSelection? _targetSelection;
    private string? _resultsStatus;

    protected override void OnDataContextChanged(EventArgs args)
    {
        base.OnDataContextChanged(args);
        if (_viewModel is not null) _viewModel.PropertyChanging -= OnProjectChanging;
        _targetSelection?.Dispose();
        _targetSelection = null;
        _viewModel = DataContext as ModsViewModel;
        _resultsStatus = null;
        if (_viewModel is not null && this.GetVisualRoot() is not null)
        {
            _viewModel.PropertyChanging += OnProjectChanging;
            BindTargetSelection();
        }
    }

    private void BindTargetSelection()
    {
        _targetSelection?.Dispose();
        if (_viewModel is not { } viewModel) return;
        _targetSelection = new ResourceBrowserTargetSelection(TargetInstanceComboBox, viewModel,
            nameof(ModsViewModel.SelectedModInstance), viewModel.ModInstances, () => viewModel.SelectedModInstance);
    }

    private void OnProjectChanging(object? sender, PropertyChangingEventArgs args)
    {
        if (args.PropertyName != nameof(ModsViewModel.SelectedMod) || _viewModel is not { HasResourceDetail: false }) return;
        _resultsStatus = _viewModel.ModStatus;
        RememberResultsPosition();
    }

    private void OnTargetInstanceChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_targetSelection?.IsSynchronizing != true && !IsTargetLocked && DataContext is ModsViewModel { IsInstanceManagement: false, IsModBusy: false } viewModel &&
            sender is ComboBox { SelectedItem: string target })
            viewModel.SelectedModInstance = target;
    }

    private async void OnReturnToResultsClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is ModsViewModel viewModel)
            await ReturnToResultsAsync(ResourceDetailPanel, () =>
            {
                viewModel.ReturnToResultsCommand.Execute(null);
                if (_resultsStatus is { } status) viewModel.ModStatus = status;
            });
    }
}
