using System.Collections.Specialized;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class ResettableObservableCollectionTests
{
    [Fact]
    public void ReplaceWith_swaps_items_and_raises_a_single_reset()
    {
        var collection = new ResettableObservableCollection<string> { "old" };
        var changes = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, e) => changes.Add(e.Action);

        collection.ReplaceWith(["a", "b", "c"]);

        collection.Should().Equal("a", "b", "c");
        changes.Should().Equal(NotifyCollectionChangedAction.Reset);
    }

    [Fact]
    public void ReplaceWith_empty_clears_the_collection()
    {
        var collection = new ResettableObservableCollection<int> { 1, 2 };

        collection.ReplaceWith([]);

        collection.Should().BeEmpty();
    }
}
