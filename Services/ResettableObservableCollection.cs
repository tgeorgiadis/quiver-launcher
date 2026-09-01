using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace QuiverLauncher.Services;

/// <summary>
/// ObservableCollection that can replace its contents with a single Reset notification.
/// </summary>
public sealed class ResettableObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceWith(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
