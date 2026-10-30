using System.ComponentModel;
using Avalonia.Controls;
using WMCL.App.Controls;
using WMCL.App.ViewModels;

namespace WMCL.App.Views;

public partial class LinkView : UserControl
{
    private LinkViewModel? _model;

    public LinkView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_model is not null) _model.PropertyChanged -= OnModelPropertyChanged;
        _model = DataContext as LinkViewModel;
        if (_model is not null) _model.PropertyChanged += OnModelPropertyChanged;
        UpdateStatus();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LinkViewModel.IsIdle) or nameof(LinkViewModel.IsBusy)
            or nameof(LinkViewModel.IsRunning) or nameof(LinkViewModel.RoomStatus))
            UpdateStatus();
    }

    /// <summary>既有状态 → 状态点四态（View 层映射，不新增 VM 属性）。</summary>
    private void UpdateStatus()
    {
        if (LinkStatusDot is null) return;
        if (_model is null)
        {
            LinkStatusDot.State = WmclStatusState.Idle;
            LinkStatusDot.Text = "";
            return;
        }
        var status = _model.RoomStatus ?? "";
        if (status.Contains("失败", StringComparison.Ordinal) || status.Contains("错误", StringComparison.Ordinal))
            LinkStatusDot.State = WmclStatusState.Failed;
        else if (_model.IsIdle)
            LinkStatusDot.State = WmclStatusState.Idle;
        else if (status.Contains("运行", StringComparison.Ordinal) || status.Contains("已连接", StringComparison.Ordinal))
            LinkStatusDot.State = WmclStatusState.Ok;
        else
            LinkStatusDot.State = WmclStatusState.Busy;
        LinkStatusDot.Text = status;
    }
}
