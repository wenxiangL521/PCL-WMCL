using Avalonia.Controls;
using Avalonia.VisualTree;
using WMCL.App.Animation;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class GameManageView : UserControl
{
    private GameManageViewModel? _viewModel;
    private CancellationTokenSource? _reveal;

    public GameManageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe();
        AttachedToVisualTree += (_, _) => Observe();
        DetachedFromVisualTree += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel = null;
            _reveal?.Cancel();
            _reveal?.Dispose();
            _reveal = null;
        };
    }

    private void Observe()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = DataContext as GameManageViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(GameManageViewModel.SelectedVersion)) PlayReveal();
    }

    /// <summary>切换选中版本时让右侧详情重新入场，和下载页的切换观感一致。</summary>
    private void PlayReveal()
    {
        _reveal?.Cancel();
        _reveal?.Dispose();
        var cancellation = new CancellationTokenSource();
        _reveal = cancellation;
        _ = RevealAsync(cancellation);
    }

    private async Task RevealAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await PclPageAnimation.RunAsync([VersionDetailPanel], entering: true, cancellation.Token,
                scrollViewer: this.FindAncestorOfType<ScrollViewer>());
        }
        catch (OperationCanceledException)
        {
            PclPageAnimation.Reset([VersionDetailPanel]);
        }
        finally
        {
            if (ReferenceEquals(_reveal, cancellation))
            {
                _reveal = null;
                cancellation.Dispose();
            }
        }
    }
}
