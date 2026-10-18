using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace PCL.App.Services;

public static class DialogService
{
    public static async Task<string?> PromptAsync(
        string title, string message, string initial = "")
    {
        if (Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            return null;

        var window = new Views.InputWindow(title, message, initial);
        return await window.ShowDialog<string?>(owner);
    }

    public static async Task<bool> ConfirmAsync(string title, string message)
    {
        if (Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            return false;

        var window = new Views.ConfirmWindow(title, message);
        return await window.ShowDialog<bool>(owner);
    }
}
