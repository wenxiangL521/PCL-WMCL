using Avalonia;
using Avalonia.Controls;
using WMCL.App.Animation;

namespace WMCL.App.Controls;

/// <summary>骨架屏占位：按 Rows/RowHeight 生成等距灰色条，可选首行缩略图占位。</summary>
public partial class WmclSkeleton : UserControl
{
    public static readonly StyledProperty<int> RowsProperty =
        AvaloniaProperty.Register<WmclSkeleton, int>(nameof(Rows), 3);

    public static readonly StyledProperty<double> RowHeightProperty =
        AvaloniaProperty.Register<WmclSkeleton, double>(nameof(RowHeight), 12);

    public static readonly StyledProperty<bool> ShowThumbnailProperty =
        AvaloniaProperty.Register<WmclSkeleton, bool>(nameof(ShowThumbnail));

    public WmclSkeleton()
    {
        InitializeComponent();
        BuildRows();
        // 减弱动效时不挂脉冲类（而非把时长归零），避免空转占帧。
        if (!Motion.ReducedMotion) Classes.Add("animated");
    }

    static WmclSkeleton()
    {
        RowsProperty.Changed.AddClassHandler<WmclSkeleton>((control, _) => control.BuildRows());
        RowHeightProperty.Changed.AddClassHandler<WmclSkeleton>((control, _) => control.BuildRows());
        ShowThumbnailProperty.Changed.AddClassHandler<WmclSkeleton>((control, _) => control.BuildRows());
    }

    public int Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public bool ShowThumbnail
    {
        get => GetValue(ShowThumbnailProperty);
        set => SetValue(ShowThumbnailProperty, value);
    }

    private void BuildRows()
    {
        if (PART_Rows is null) return;
        PART_Rows.Children.Clear();
        var rows = Math.Max(1, Rows);
        for (var index = 0; index < rows; index++)
        {
            var row = new Border { Height = RowHeight, Classes = { "skeleton" } };
            if (index == 0 && ShowThumbnail)
            {
                var thumb = new Border
                {
                    Width = 88,
                    Height = 56,
                    Classes = { "skeleton" }
                };
                var container = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                container.Children.Add(thumb);
                Grid.SetColumn(row, 1);
                container.Children.Add(row);
                PART_Rows.Children.Add(container);
                continue;
            }
            PART_Rows.Children.Add(row);
        }
    }
}
