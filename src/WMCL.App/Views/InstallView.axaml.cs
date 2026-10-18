using Avalonia.Controls;
using Avalonia.Threading;

namespace WMCL.App.Views;

public partial class InstallView : UserControl
{
    public InstallView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ResetScroll();
        Loaded += (_, _) => ResetScroll();
    }

    private void ResetScroll()
    {
        // PCL2 切页后从页面顶部开始，不能保留上一次的内部滚动位置。
        Dispatcher.UIThread.Post(() => PageScroll.Offset = default, DispatcherPriority.Loaded);
    }
}
