using System.Collections.Specialized;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class ResettableObservableCollectionTests
{
    [Fact]
    public void UpdateWith_preserves_small_edits_and_skips_identical_results()
    {
        var collection = new ResettableObservableCollection<string> { "a", "b", "c" };
        var changes = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, e) => changes.Add(e.Action);
        collection.UpdateWith(["a", "b", "c"]);
        changes.Should().BeEmpty();
        collection.UpdateWith(["a", "c"]);
        changes.Should().Equal(NotifyCollectionChangedAction.Remove);
        collection.UpdateWith(["c", "new", "a"]);
        collection.Should().Equal("c", "new", "a");
        changes.Should().NotContain(NotifyCollectionChangedAction.Reset);
        collection.UpdateWith(Enumerable.Range(0, 100).Select(i => i.ToString()));
        changes.Last().Should().Be(NotifyCollectionChangedAction.Reset);
        collection.Should().HaveCount(100);
    }

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
