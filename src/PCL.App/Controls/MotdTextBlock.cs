using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using PCL.Minecraft;

namespace PCL.App.Controls;

/// <summary>把 MOTD runs 渲染为彩色 Inlines 的 TextBlock。</summary>
public sealed class MotdTextBlock : TextBlock
{
    public static readonly StyledProperty<IReadOnlyList<MotdRun>?> RunsProperty =
        AvaloniaProperty.Register<MotdTextBlock, IReadOnlyList<MotdRun>?>(nameof(Runs));

    public IReadOnlyList<MotdRun>? Runs
    {
        get => GetValue(RunsProperty);
        set => SetValue(RunsProperty, value);
    }

    static MotdTextBlock()
    {
        RunsProperty.Changed.AddClassHandler<MotdTextBlock>((block, _) => block.Rebuild());
    }

    private void Rebuild()
    {
        if (Inlines is null) return;
        Inlines.Clear();
        if (Runs is null) return;
        foreach (var item in Runs)
        {
            var run = new Run(item.Text)
            {
                FontWeight = item.Bold ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = item.Italic ? FontStyle.Italic : FontStyle.Normal,
                TextDecorations = BuildDecorations(item)
            };
            if (!string.IsNullOrWhiteSpace(item.Color))
            {
                try { run.Foreground = new SolidColorBrush(Color.Parse(item.Color)); }
                catch { /* invalid color */ }
            }
            Inlines.Add(run);
        }
    }

    private static TextDecorationCollection? BuildDecorations(MotdRun item)
    {
        if (!item.Underline && !item.Strikethrough) return null;
        var decorations = new TextDecorationCollection();
        if (item.Underline)
        {
            decorations.Add(new TextDecoration
            {
                Location = TextDecorationLocation.Underline
            });
        }
        if (item.Strikethrough)
        {
            decorations.Add(new TextDecoration
            {
                Location = TextDecorationLocation.Strikethrough
            });
        }
        return decorations;
    }
}
