using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace WMCL.App.Controls;

/// <summary>不可输入的版本选择：点击展开固定大小列表，带淡入动画。</summary>
public partial class VersionPicker : UserControl
{
    public static readonly StyledProperty<IEnumerable<string>?> ItemsSourceProperty =
        AvaloniaProperty.Register<VersionPicker, IEnumerable<string>?>(nameof(ItemsSource));

    public static readonly StyledProperty<string?> SelectedItemProperty =
        AvaloniaProperty.Register<VersionPicker, string?>(nameof(SelectedItem),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<VersionPicker, string?>(nameof(Watermark), "全部");

    public VersionPicker()
    {
        InitializeComponent();
        PickerButton.Click += OnButtonClick;
        PickerList.SelectionChanged += OnSelectionChanged;
        SelectedItemProperty.Changed.AddClassHandler<VersionPicker>((picker, _) => picker.UpdateText());
        ItemsSourceProperty.Changed.AddClassHandler<VersionPicker>((picker, _) => picker.SyncItems());
    }

    public IEnumerable<string>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    private void UpdateText()
    {
        if (ValueText is null) return;
        ValueText.Text = string.IsNullOrWhiteSpace(SelectedItem) ? Watermark ?? "全部" : SelectedItem;
    }

    private void SyncItems()
    {
        if (PickerList is null) return;
        PickerList.ItemsSource = ItemsSource;
        PickerList.SelectedItem = SelectedItem;
        var empty = ItemsSource is null || !ItemsSource.Any();
        EmptyHint.IsVisible = empty;
        PickerList.IsVisible = !empty;
        UpdateText();
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (PickerPopup.IsOpen)
        {
            PickerPopup.IsOpen = false;
            return;
        }
        SyncItems();
        // 宽度与选择框对齐
        if (PickerButton.Bounds.Width > 0) PickerPanel.MinWidth = PickerButton.Bounds.Width;
        PickerPanel.Classes.Set("open", false);
        PickerPopup.IsOpen = true;
        Dispatcher.UIThread.Post(() => PickerPanel.Classes.Set("open", true), DispatcherPriority.Render);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PickerList.SelectedItem is not string value) return;
        SelectedItem = value;
        PickerPopup.IsOpen = false;
    }
}
