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
/// 二期复合控件契约（spec §7.1、§5.11.1-2）：MySearchBox 模板部件、iconButton 尺寸档。
/// </summary>
[Trait("Kind", "Headless")]
public sealed class ControlContractTests
{
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
            Assert.Equal(13, textBox.FontSize);
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
