using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        InitializeComponent();
        DataContext = new WelcomeViewModel();
        Closing += (_, _) => (DataContext as WelcomeViewModel)?.Dispose();
    }
}
