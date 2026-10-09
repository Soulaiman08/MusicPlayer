using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace MusicPlayer.Utilities;

public class BulkObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotifications;

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotifications)
            base.OnCollectionChanged(e);
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_suppressNotifications)
            base.OnPropertyChanged(e);
    }

    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        CheckReentrancy();

        var list = items as IList<T> ?? items.ToList();
        if (list.Count == 0) return;

        _suppressNotifications = true;
        try
        {
            foreach (var item in list)
                Items.Add(item);
        }
        finally
        {
            _suppressNotifications = false;
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        CheckReentrancy();

        var list = items as IList<T> ?? items.ToList();

        _suppressNotifications = true;
        try
        {
            Items.Clear();
            foreach (var item in list)
                Items.Add(item);
        }
        finally
        {
            _suppressNotifications = false;
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Appends items one at a time so the view only realizes the NEW elements —
    /// it does NOT rebuild the existing ones (a Reset would).
    /// Goes through the public Add (InsertItem) so every item raises its own
    /// single-item Add notification: writing to the protected Items list directly
    /// raises nothing and the view would silently keep showing only the previous
    /// snapshot. Multi-item ("range") notifications are also rejected by WPF's
    /// ItemCollection, so one event per item is required.
    /// Use this for progressive / batch loading to avoid full-list redraws.
    /// </summary>
    public void AddRangeIncremental(IList<T> items)
    {
        if (items == null || items.Count == 0) return;

        CheckReentrancy();

        foreach (var item in items)
            Add(item); // InsertItem → single-item Add events → view appends only the new containers
    }
}
