using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;

namespace WMCL.App.Views;

internal sealed class ResourceBrowserTargetSelection : IDisposable
{
    private readonly ComboBox _picker;
    private readonly INotifyPropertyChanged _model;
    private readonly string _propertyName;
    private readonly INotifyCollectionChanged _targets;
    private readonly Func<string?> _readTarget;
    private bool _restoreQueued;
    private bool _disposed;

    public ResourceBrowserTargetSelection(ComboBox picker, INotifyPropertyChanged model, string propertyName,
        INotifyCollectionChanged targets, Func<string?> readTarget)
    {
        _picker = picker;
        _model = model;
        _propertyName = propertyName;
        _targets = targets;
        _readTarget = readTarget;
        _model.PropertyChanged += OnTargetChanged;
        _targets.CollectionChanged += OnTargetsChanged;
        Synchronize();
    }

    public bool IsSynchronizing { get; private set; }

    private void OnTargetChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == _propertyName)
            Synchronize();
    }

    private void OnTargetsChanged(object? sender, NotifyCollectionChangedEventArgs args) => Synchronize();

    private void Synchronize()
    {
        Restore();
        if (_restoreQueued || _disposed) return;
        _restoreQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _restoreQueued = false;
            Restore();
        }, DispatcherPriority.Loaded);
    }

    private void Restore()
    {
        if (_disposed) return;
        var target = _readTarget();
        if (target is not null && !_picker.Items.Contains(target)) return;
        if (Equals(_picker.SelectedItem, target)) return;
        IsSynchronizing = true;
        try { _picker.SetCurrentValue(ComboBox.SelectedItemProperty, target); }
        finally { IsSynchronizing = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _model.PropertyChanged -= OnTargetChanged;
        _targets.CollectionChanged -= OnTargetsChanged;
    }
}
