using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace PCL.App.Services;

public static class DialogService
{
    public static async Task<bool> ConfirmAsync(string title, string message)
    {
        if (Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            return false;

        var window = new Views.ConfirmWindow(title, message);
        return await window.ShowDialog<bool>(owner);
    }
}
