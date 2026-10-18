using Avalonia.Controls;

namespace PCL.App.Views;

public partial class LaunchView : UserControl
{
    public LaunchView()
    {
        InitializeComponent();

        LogBox.TextChanged += (_, _) =>
        {
            if (LogBox.Text is { Length: > 0 })
                LogBox.CaretIndex = LogBox.Text.Length;
        };
    }
}
