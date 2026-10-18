using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WMCL.App.Animation;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class VersionPickerWindow : Window
{
    private readonly IReadOnlyList<VersionItem> _all;

    public VersionPickerWindow()
    {
        _all = [];
        InitializeComponent();
        Opened += async (_, _) =>
        {
            await ModalMotion.AnimateInAsync(this);
            SearchBox.Focus();
        };
    }

    public VersionPickerWindow(
        IReadOnlyList<VersionItem> versions,
        VersionItem? selected) : this()
    {
        _all = versions;
        VersionList.ItemsSource = _all;
        if (selected is not null)
        {
            VersionList.SelectedItem = _all.FirstOrDefault(v => v.Id == selected.Id);
            Opened += (_, _) => VersionList.ScrollIntoView(selected);
        }
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim() ?? "";
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _all
            : _all.Where(v =>
                v.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                v.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        var selected = VersionList.SelectedItem as VersionItem;
        VersionList.ItemsSource = filtered;
        if (selected is not null && filtered.Contains(selected))
            VersionList.SelectedItem = selected;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (VersionList.SelectedItem is VersionItem) Close(VersionList.SelectedItem);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && VersionList.SelectedItem is VersionItem selected)
        {
            Close(selected);
            e.Handled = true;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnConfirm(object? sender, RoutedEventArgs e) =>
        Close(VersionList.SelectedItem as VersionItem);
}
