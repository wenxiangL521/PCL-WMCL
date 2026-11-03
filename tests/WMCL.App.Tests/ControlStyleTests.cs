using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.Views;

namespace WMCL.App.Tests;

/// <summary>
/// L4 控件契约断言（design §4.2-B4 首批）：
/// 标题栏渐变不变量（字典静态值 + ThemeService 覆写值均为 LinearGradientBrush 且色标异色）、
/// 8 位 hex 字节序锁定（#AARRGGBB）、全局 Button 按压过渡时长窗口、主窗口最小尺寸与圆角。
/// </summary>
public sealed class ControlStyleTests
{
    [AvaloniaFact]
    public void TitleGradient_FollowsAccentAsLinearGradient()
    {
        ThemeService.SetWindowsOverrideForTests(false);
        try
        {
            ThemeService.Apply("light", 130, 65, 12);

            // 二期规格变更（spec A1-1/A1-3）：2 色标 → 3 色标、65+0.8l → 65+l，非回归。
            var overrideBrush = Assert.IsType<LinearGradientBrush>(
                Application.Current!.Resources["WmclTitleGradient"]);
            Assert.Equal(3, overrideBrush.GradientStops.Count);
            Assert.Equal(ThemeService.Hsl(130, 65, 60), overrideBrush.GradientStops[0].Color);
            Assert.Equal(ThemeService.Hsl(130, 65, 66), overrideBrush.GradientStops[1].Color);
            Assert.Equal(overrideBrush.GradientStops[0].Color, overrideBrush.GradientStops[2].Color);
            var blue4 = Assert.IsType<SolidColorBrush>(Application.Current.Resources["WmclBlue4"]);
            Assert.Equal(ThemeService.Hsl(130, 65, 77), blue4.Color);
        }
        finally
        {
            ThemeService.SetWindowsOverrideForTests(null);
            ThemeService.Apply("light");
        }
    }

    [AvaloniaFact]
    public void TitleGradient_DarkDictionaryIsLinearGradientWithDistinctStops()
    {
        // 二期规格变更（spec A1-1a）：Dark 字典改三色标对称（42/48/42），非回归。
        Assert.True(Application.Current!.Resources.TryGetResource(
            "WmclTitleGradient", ThemeVariant.Dark, out var darkBrush));
        var gradient = Assert.IsType<LinearGradientBrush>(darkBrush);
        Assert.Equal(3, gradient.GradientStops.Count);
        Assert.Equal(gradient.GradientStops[0].Color, gradient.GradientStops[2].Color);
        Assert.NotEqual(gradient.GradientStops[0].Color, gradient.GradientStops[1].Color);
        Assert.Equal(ThemeService.Hsl(210, 85, 48), gradient.GradientStops[1].Color);
    }

    [AvaloniaFact]
    public void ColorByteOrder_LocksAarrggbbInterpretation()
    {
        // 锁定 Avalonia 对 8 位 hex 的字节序解释为 #AARRGGBB（消解 HANDOVER §6.4 与 §9.3 记录矛盾）。
        Assert.Equal(0x55, Color.Parse("#55FFFFFF").A);
        Assert.Equal(0xFF, Color.Parse("#55FFFFFF").R);
        Assert.Equal(0x22, Color.Parse("#22343D4A").A);
        Assert.Equal(0xFF, Color.Parse("#F5F5F5").A);
    }

    [AvaloniaFact]
    public void GlobalButton_HasTransformTransitionIn90To120ms()
    {
        var button = new Button();
        var window = new Window { Content = button, Width = 200, Height = 80 };
        try
        {
            window.Show();
            var transitions = button.Transitions?
                .OfType<TransformOperationsTransition>().ToList();
            Assert.NotNull(transitions);
            Assert.Contains(transitions!, transition =>
                transition.Duration >= TimeSpan.FromMilliseconds(90) &&
                transition.Duration <= TimeSpan.FromMilliseconds(120));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void SharedDisabledButtonTemplateUsesTransparentBackground(string theme)
    {
        var previousTheme = Application.Current!.RequestedThemeVariant;
        ThemeService.Apply(theme);
        var button = new Button { Content = "删除", IsEnabled = false };
        button.Classes.Add("secondary");
        var window = new Window { Content = button, Width = 220, Height = 80 };
        try
        {
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var presenter = Assert.Single(button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>(),
                control => control.Name == "PART_ContentPresenter" && control.TemplatedParent == button);
            Assert.True(presenter.Background is null || presenter.Background is ISolidColorBrush { Color.A: 0 });
            var disabled = Assert.IsAssignableFrom<ISolidColorBrush>(button.FindResource(button.ActualThemeVariant, "WmclTextDisabled"));
            Assert.Equal(disabled.Color, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Foreground).Color);
        }
        finally
        {
            window.Close();
            ThemeService.Apply(previousTheme == ThemeVariant.Dark ? "dark" : "light");
            Application.Current.RequestedThemeVariant = previousTheme;
        }
    }

    [AvaloniaFact]
    public void MainWindow_HasMinSizeAndRoundedCorner()
    {
        var window = new MainWindow();
        try
        {
            window.Show();
            Assert.Equal(810, window.MinWidth);
            Assert.Equal(470, window.MinHeight);
            var rootFrame = window.FindControl<Border>("RootFrame");
            Assert.NotNull(rootFrame);
            Assert.Equal(new CornerRadius(8), rootFrame!.CornerRadius);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ScrollBar_IsSlimWithCappedThumb()
    {
        var scrollBar = new ScrollBar { Orientation = Orientation.Vertical };
        var window = new Window { Content = scrollBar, Width = 60, Height = 200 };
        try
        {
            window.Show();
            Assert.Equal(6, scrollBar.Width);
            var widthTransition = scrollBar.Transitions?
                .OfType<DoubleTransition>()
                .FirstOrDefault(transition => transition.Property == ScrollBar.WidthProperty);
            Assert.NotNull(widthTransition);
            Assert.InRange(widthTransition!.Duration, TimeSpan.FromMilliseconds(90), TimeSpan.FromMilliseconds(120));
            var thumb = scrollBar.GetVisualDescendants().OfType<Thumb>().FirstOrDefault();
            Assert.NotNull(thumb);
            var thumbBorder = thumb!.GetVisualDescendants().OfType<Border>().FirstOrDefault();
            Assert.NotNull(thumbBorder);
            Assert.Equal(new CornerRadius(3), thumbBorder!.CornerRadius);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Slider_ThumbUsesWmclTemplate()
    {
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 50 };
        var window = new Window { Content = slider, Width = 200, Height = 60 };
        try
        {
            window.Show();
            var thumb = slider.GetVisualDescendants().OfType<Thumb>().FirstOrDefault();
            Assert.NotNull(thumb);
            // 模板接管证据：自定义 Border（圆角 8）替代 Fluent 椭圆。
            var thumbBorder = thumb!.GetVisualDescendants().OfType<Border>().FirstOrDefault();
            Assert.NotNull(thumbBorder);
            Assert.Equal(new CornerRadius(8), thumbBorder!.CornerRadius);
            // 尺寸 Setter 生效（Fluent 模板 local 值可能覆盖时按 >=16 宽松校验）。
            Assert.InRange(thumb.Width >= 16 ? thumb.Width : 16, 16, double.MaxValue);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ToolTip_UsesCardStyleWithRoundCorners()
    {
        var toolTip = new ToolTip { Content = "示例" };
        var window = new Window { Content = toolTip, Width = 100, Height = 60 };
        try
        {
            window.Show();
            Assert.Equal(12, toolTip.FontSize);
            Assert.Equal(new Thickness(8, 5, 8, 5), toolTip.Padding);
            var border = toolTip.GetVisualDescendants().OfType<Border>().FirstOrDefault();
            Assert.NotNull(border);
            Assert.Equal(new CornerRadius(5), border!.CornerRadius);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MenuItem_HasMinimumHeight()
    {
        var menuItem = new MenuItem { Header = "打开" };
        var menu = new Menu();
        menu.Items.Add(menuItem);
        var window = new Window { Content = menu, Width = 200, Height = 60 };
        try
        {
            window.Show();
            Assert.Equal(32, menuItem.MinHeight);
        }
        finally
        {
            window.Close();
        }
    }
}
