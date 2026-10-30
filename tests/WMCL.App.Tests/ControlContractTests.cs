using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Controls;

namespace WMCL.App.Tests;

/// <summary>
/// 二期复合控件契约（spec §7.1、§5.11.1-2）：MyHint 三级、MySearchBox 模板部件、iconButton 尺寸档。
/// </summary>
public sealed class ControlContractTests
{
    [AvaloniaFact]
    public void MyHint_ErrorLevel_UsesErrorPaletteAndAccentBorder()
    {
        var hint = new MyHint { Level = HintLevel.Error, Content = "出错", Action = new Button { Content = "重试" } };
        var window = new Window { Content = hint, Width = 400, Height = 120 };
        try
        {
            window.Show();
            for (var i = 0; i < 3; i++)
            {
                hint.ApplyTemplate();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
            var border = hint.GetVisualDescendants().OfType<Border>()
                .First(candidate => candidate.Classes.Contains("hint"));
            Assert.True(border.Classes.Contains("error"));
            Assert.Equal(new Thickness(3, 0, 0, 0), border.BorderThickness);
            Assert.Equal(new CornerRadius(2), border.CornerRadius);
            // 色值由 L1 截图裁决（模板内 DynamicResource 在 headless 首帧可能未解析）。
            Assert.NotNull(hint.FindResource("WmclErrorBackground"));
            var actionSlot = hint.GetVisualDescendants().OfType<ContentPresenter>()
                .FirstOrDefault(presenter => presenter.Name == "PART_Action");
            Assert.NotNull(actionSlot);
            Assert.True(actionSlot!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MySearchBox_ExposesTemplatePartsAndInputFont()
    {
        var searchBox = new MySearchBox { Watermark = "搜索", Text = "mc" };
        var window = new Window { Content = searchBox, Width = 300, Height = 60 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            searchBox.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();

            var textBox = searchBox.GetVisualDescendants().OfType<TextBox>().First();
            var clearButton = searchBox.GetVisualDescendants().OfType<Button>().First();
            Assert.NotNull(textBox);
            Assert.Equal(13.5, textBox.FontSize);
            Assert.True(searchBox.IsClearButtonVisible);
            Assert.True(clearButton.IsVisible);

            clearButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("", searchBox.Text);
            Assert.False(searchBox.IsClearButtonVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void IconButton_SizesMatchContract()
    {
        var button = new Button { Classes = { "iconButton", "size20" } };
        var window = new Window { Content = button, Width = 120, Height = 60 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(20, button.Width);
            Assert.Equal(20, button.Height);
            Assert.Equal(0, button.BorderThickness.Left);
            Assert.Equal(0, button.MinHeight);
        }
        finally
        {
            window.Close();
        }
    }
}
