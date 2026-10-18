using Avalonia;
using Avalonia.Animation;
using AvaloniaAnimation = Avalonia.Animation.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using WMCL.App.Animation;

namespace WMCL.App.Views;

public partial class InstallView : UserControl
{
    public InstallView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ResetScroll();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ResetScroll();
        await AnimatePageAsync();
    }

    private async Task AnimatePageAsync()
    {
        PageScroll.RenderTransform = new TranslateTransform(0, 8);
        PageScroll.Opacity = 0;
        var animation = new AvaloniaAnimation
        {
            Duration = MotionDurations.Entrance,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(TranslateTransform.YProperty, 8d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(TranslateTransform.YProperty, 0d)
                    }
                }
            }
        };
        await animation.RunAsync(PageScroll);
    }

    private void OnComponentHeaderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string kind } &&
            DataContext is ViewModels.InstallViewModel viewModel)
            viewModel.SelectComponent(kind);
    }

    private void OnUseVanillaClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.InstallViewModel viewModel)
            viewModel.UseVanilla();
    }

    private void OnToggleMinecraftClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.InstallViewModel viewModel)
            viewModel.IsMinecraftExpanded = !viewModel.IsMinecraftExpanded;
    }

    private void ResetScroll()
    {
        // PCL2 切页后从页面顶部开始，不能保留上一次的内部滚动位置。
        Dispatcher.UIThread.Post(() => PageScroll.Offset = default, DispatcherPriority.Loaded);
    }
}
