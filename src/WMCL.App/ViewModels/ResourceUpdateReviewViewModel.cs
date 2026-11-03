using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WMCL.App.ViewModels;

public partial class ResourceUpdateReviewItem : ObservableObject
{
    internal Func<CancellationToken, Task> Apply { get; }

    public ResourceUpdateReviewItem(string title, string currentVersion, string newVersion, string detail,
        Func<CancellationToken, Task> apply)
    {
        Title = title;
        CurrentVersion = currentVersion;
        NewVersion = newVersion;
        Detail = detail;
        Apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    public string Title { get; }
    public string CurrentVersion { get; }
    public string NewVersion { get; }
    public string Detail { get; }
    public string VersionChange => $"{CurrentVersion} → {NewVersion}";

    [ObservableProperty]
    private bool _isSelected = true;
    [ObservableProperty]
    private string _status = "待更新";

    private bool _isCompleted;
    public bool IsCompleted
    {
        get => _isCompleted;
        internal set
        {
            if (!SetProperty(ref _isCompleted, value)) return;
            OnPropertyChanged(nameof(CanSelect));
            if (value) IsSelected = false;
        }
    }

    public bool CanSelect => !IsCompleted;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && IsCompleted) IsSelected = false;
    }
}

public partial class ResourceUpdateReviewViewModel : ViewModelBase
{
    private readonly Func<bool>? _isTargetCurrent;
    private CancellationTokenSource? _operation;

    public ResourceUpdateReviewViewModel(string typeName, IReadOnlyList<ResourceUpdateReviewItem> items,
        Func<bool>? isTargetCurrent = null)
    {
        TypeName = typeName;
        _isTargetCurrent = isTargetCurrent;
        Items = new ObservableCollection<ResourceUpdateReviewItem>(items);
        foreach (var item in Items) item.PropertyChanged += OnItemChanged;
        Items.CollectionChanged += (_, change) =>
        {
            if (change.OldItems is not null)
                foreach (ResourceUpdateReviewItem item in change.OldItems) item.PropertyChanged -= OnItemChanged;
            if (change.NewItems is not null)
                foreach (ResourceUpdateReviewItem item in change.NewItems) item.PropertyChanged += OnItemChanged;
            RefreshSelection();
            OnPropertyChanged(nameof(IsEmpty));
        };
        Status = items.Count == 0 ? "没有可更新的资源。" : "请确认版本并选择需要更新的资源。";
    }

    public string TypeName { get; }
    public string Title => $"{TypeName}更新确认";
    public ObservableCollection<ResourceUpdateReviewItem> Items { get; }
    public bool IsEmpty => Items.Count == 0;
    public int SelectedCount => Items.Count(item => item.IsSelected && !item.IsCompleted);
    public bool CanApply => !IsBusy && SelectedCount > 0;

    [ObservableProperty]
    private bool _isBusy;
    [ObservableProperty]
    private string _status = "";

    partial void OnIsBusyChanged(bool value) => RefreshSelection();

    private void OnItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ResourceUpdateReviewItem.IsSelected) or nameof(ResourceUpdateReviewItem.IsCompleted))
            RefreshSelection();
    }

    private void RefreshSelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanApply));
        ApplySelectedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectAll()
    {
        if (IsBusy) return;
        foreach (var item in Items.Where(item => !item.IsCompleted)) item.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        if (IsBusy) return;
        foreach (var item in Items) item.IsSelected = false;
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplySelectedAsync()
    {
        if (!CanApply) return;
        var selected = Items.Where(item => item.IsSelected && !item.IsCompleted).ToArray();
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        IsBusy = true;
        var completed = 0;
        var failed = 0;
        try
        {
            foreach (var item in selected)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (_isTargetCurrent?.Invoke() == false)
                {
                    item.Status = "目标已改变，未更新";
                    Status = $"目标已改变，已停止更新（成功 {completed} 项，失败 {failed} 项）。请重新检查目标。";
                    return;
                }
                if (item.IsCompleted) continue;
                item.Status = "正在更新…";
                Status = $"正在更新 {item.Title}（成功 {completed} 项，失败 {failed} 项）…";
                try
                {
                    await item.Apply(cancellation.Token);
                    item.IsCompleted = true;
                    item.Status = "更新成功";
                    completed++;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    item.Status = "已取消，可重试";
                    throw;
                }
                catch (Exception exception)
                {
                    item.IsSelected = true;
                    item.Status = "更新失败：" + exception.Message;
                    failed++;
                }
            }
            Status = $"更新结束：成功 {completed} 项，失败 {failed} 项。";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = $"已取消更新：成功 {completed} 项，失败 {failed} 项，剩余资源未更新。";
        }
        finally
        {
            _operation = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _operation?.Cancel();
        if (!IsBusy) Status = "已取消，未提交新的更新。";
    }
}
