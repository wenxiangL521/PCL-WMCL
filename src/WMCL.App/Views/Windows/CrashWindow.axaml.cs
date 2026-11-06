using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WMCL.App.Views;

public partial class CrashWindow : Window
{
    public CrashWindow() => InitializeComponent();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
