using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Animation;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class HelpView : UserControl
{
    private HelpViewModel? _viewModel;
    private CancellationTokenSource? _reveal;

    public HelpView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Observe();
        DataContextChanged += (_, _) => Observe();
    }

    private void Observe()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = DataContext as HelpViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(HelpViewModel.SelectedTopic)) PlayReveal();
    }

    /// <summary>切换帮助主题时让右侧正文重新入场，并回到顶部。</summary>
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
        // 等一次布局，确保正文已经换成新主题的内容
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (cancellation.IsCancellationRequested) return;
        try
        {
            ArticleScroll.ScrollToHome();
            await PclPageAnimation.RunAsync([ArticleHost], entering: true, cancellation.Token,
                scrollViewer: ArticleScroll);
        }
        catch (OperationCanceledException)
        {
            PclPageAnimation.Reset([ArticleHost]);
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
