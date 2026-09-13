using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace QuiverLauncher.Services;

/// <summary>
/// ObservableCollection that can replace its contents with a single Reset notification.
/// </summary>
public sealed class ResettableObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>Preserves existing items for small edits; large changes use a single reset.</summary>
    public void UpdateWith(IEnumerable<T> items)
    {
        var next = items.ToList();
        if (this.SequenceEqual(next)) return;
        var retained = new HashSet<T>(next);
        var current = new HashSet<T>(this);
        if (this.Count(item => !retained.Contains(item)) + next.Count(item => !current.Contains(item)) > 32)
        {
            ReplaceWith(next);
            return;
        }
        for (var i = Count - 1; i >= 0; i--)
            if (!retained.Contains(this[i])) RemoveAt(i);
        for (var i = 0; i < next.Count; i++)
        {
            if (i < Count && EqualityComparer<T>.Default.Equals(this[i], next[i])) continue;
            var existing = IndexOf(next[i]);
            if (existing >= i) Move(existing, i);
            else Insert(i, next[i]);
        }
        while (Count > next.Count) RemoveAt(Count - 1);
    }

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
