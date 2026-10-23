using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WMCL.Modding.Local;

namespace WMCL.App.Views;

public sealed class JarInJarRow
{
    public JarInJarRow(JarInJarEntry entry) => Entry = entry;
    public JarInJarEntry Entry { get; }
    public string DisplayName => Entry.DisplayName;
    public string Detail => Entry.Detail;
    public string SizeText => Entry.SizeText;
    public Thickness IndentMargin => new(Entry.Depth * 18, 0, 0, 0);
}

public partial class JarInJarWindow : Window
{
    private readonly string? _modPath;
    private readonly ObservableCollection<JarInJarRow> _rows = [];
    private readonly ObservableCollection<string> _warnings = [];

    public JarInJarWindow()
    {
        InitializeComponent();
    }

    public JarInJarWindow(string modPath, string modName) : this()
    {
        _modPath = modPath;
        Title = "Jar-in-Jar — " + modName;
        ModNameText.Text = modName;
        EntryList.ItemsSource = _rows;
        WarningsList.ItemsSource = _warnings;
        Opened += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(_modPath) || !File.Exists(_modPath))
        {
            StatusText.Text = "Mod 文件不存在。";
            EmptyState.IsVisible = true;
            return;
        }
        try
        {
            StatusText.Text = "正在扫描内嵌 Jar…";
            var result = await Task.Run(() => JarInJarService.ScanDetailed(_modPath));
            _rows.Clear();
            _warnings.Clear();
            AddRows(result.Entries);
            foreach (var warning in result.Warnings) _warnings.Add(warning);
            WarningsPanel.IsVisible = _warnings.Count > 0;
            EmptyState.IsVisible = _rows.Count == 0;
            StatusText.Text = _rows.Count == 0
                ? ""
                : $"共 {_rows.Count} 个内嵌 Jar" + (_warnings.Count > 0 ? $"，{_warnings.Count} 条告警。" : "。");
        }
        catch (Exception ex)
        {
            EmptyState.IsVisible = true;
            StatusText.Text = "扫描失败：" + ex.Message;
        }
    }

    private void AddRows(IEnumerable<JarInJarEntry> entries)
    {
        foreach (var entry in entries)
        {
            _rows.Add(new JarInJarRow(entry));
            AddRows(entry.Children);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
